using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void WifiPage()
        {
            Sub("Wifi name & password");
            var p = new StackPanel(); Detail(p, "Wifi name", J.S(network, "name")); page.Children.Add(Card(p));
            ActionRow(
                LocalButton("Change wifi name", delegate { Edit("Wifi name", new[] { new Field("name", "Wifi name", J.S(network, "name")) }, async v => { ValidateSsid(V(v, "name")); if (!Confirm("Change the wifi name? Devices may need to reconnect.")) return; await Change("PUT", networkUrl, J.Obj("name", V(v, "name").Trim()), true, "Wifi name updated."); }); }),
                LocalButton("Change wifi password", delegate { Edit("Wifi password", new[] { new Field("password", "New password", null, "password") }, async v => { ValidatePassword(V(v, "password")); if (!Confirm("Change the wifi password? Devices will need the new password to reconnect.")) return; await Change("PUT", NetResource("password"), J.Obj("password", V(v, "password")), true, "Wifi password updated."); }); }),
                Button("Show password", async delegate { ShowPassword(await WifiPassword(network, NetResource("password"))); }),
                Button("Share QR code", async delegate { ShowWifiQr(J.S(network, "name"), await WifiPassword(network, NetResource("password"))); }),
                Button("Copy wifi details", async delegate { string password = await WifiPassword(network, NetResource("password")); Clipboard.SetText("Wifi: " + J.S(network, "name") + Environment.NewLine + "Password: " + password); SetNotice("Wifi details copied to the Windows clipboard."); })
            );
        }
        private async Task<string> WifiPassword(object data, string url)
        {
            object result = await Read(url);
            string password = result is string ? (string)result : J.S(result, "password");
            if (String.IsNullOrEmpty(password)) throw new InvalidOperationException("eero did not return the wifi password for this account.");
            return password;
        }
        private void ShowPassword(string password)
        {
            var p = new StackPanel(); p.Children.Add(Text("Wifi password", 24, "#17263C", true));
            var t = new TextBox { Text = password, IsReadOnly = true }; p.Children.Add(t); var w = Dialog("Wifi password", p); w.Height = 230; w.MinHeight = 220; w.Closed += delegate { t.Clear(); }; w.ShowDialog();
        }
        private void GuestPage(object data)
        {
            Sub("A separate wifi network for your guests.");
            if (data == null) { page.Children.Add(Text("Loading guest network…", 14, "#64738A")); return; }
            var p = new StackPanel(); Detail(p, "Guest network", J.S(data, "name")); Detail(p, "Guest access", J.B(data, "enabled") ? "Enabled" : "Disabled"); page.Children.Add(Card(p));
            string guestUrl = ObjectUrl(data, NetResource("guestnetwork")); string passwordUrl = Resource(data, "password", guestUrl + "/password");
            ActionRow(
                LocalButton("Edit guest network", delegate { Edit("Guest wifi", new[] { new Field("name", "Guest wifi name", J.S(data, "name")), new Field("enabled", "Enable guest wifi", J.B(data, "enabled"), "bool") }, async v => { ValidateSsid(V(v, "name")); await Change("PUT", guestUrl, J.Obj("name", V(v, "name").Trim(), "enabled", Flag(v, "enabled")), true, "Guest network updated."); }); }),
                LocalButton("Change guest password", delegate { Edit("Guest password", new[] { new Field("password", "New guest password", null, "password") }, async v => { ValidatePassword(V(v, "password")); await Change("PUT", passwordUrl, J.Obj("password", V(v, "password")), true, "Guest password updated."); }); }),
                Button("Show guest password", async delegate { ShowPassword(await WifiPassword(data, passwordUrl)); }),
                Button("Share guest QR code", async delegate { ShowWifiQr(J.S(data, "name"), await WifiPassword(data, passwordUrl)); })
            );
        }
        private void NicknamePage()
        {
            Sub("Nickname & location");
            ObjectCards(J.Obj("nickname", J.First(network, "nickname_label", "nickname"), "timezone", J.Get(network, "timezone")));
            ActionRow(
                LocalButton("Change nickname", delegate { Edit("Network nickname", new[] { new Field("label", "Nickname", J.First(network, "nickname_label", "nickname")) }, async v => { ValidateName(V(v, "label")); await Change("PUT", FeatureUrl("label?labelType=specialMarket"), J.Obj("label", V(v, "label").Trim()), true, "Network nickname updated."); }); }),
                LocalButton("Change timezone", delegate { Edit("Network timezone", new[] { new Field("timezone", "IANA timezone, e.g. America/Chicago", J.S(network, "timezone", "value").NullIfEmpty() ?? J.S(network, "timezone")) }, async v => { string zone = V(v, "timezone").Trim(); if (!System.Text.RegularExpressions.Regex.IsMatch(zone, @"^[A-Za-z_+-]+(?:/[A-Za-z0-9_+-]+){1,2}$|^UTC$")) throw new ArgumentException("Enter an IANA timezone such as America/Chicago."); if (!Confirm("Change the network timezone? Scheduled pauses will follow the new timezone.")) return; await Change("PUT", NetResource("settings"), J.Obj("timezone", J.Obj("value", zone)), false, "Timezone updated."); }); })
            );
        }
        private void WanPage()
        {
            Sub("Internet connection");
            ObjectCards(J.Obj("connection", J.Get(network, "connection"), "ip_settings", J.Get(network, "ip_settings"), "lease", J.Get(network, "lease"), "ipv6_lease", J.Get(network, "ipv6_lease"), "pppoe_enabled", J.Get(network, "pppoe_enabled"), "pppoe_username", J.Get(network, "pppoe_username")));
            page.Children.Add(LocalButton("Configure WAN addressing", delegate { EditWanAddress(); }));
            page.Children.Add(Section("PPPoE & ISP VLAN settings", "Settings/Advanced networking/Internet connection/ISP settings"));
        }
        private void DnsPage()
        {
            Sub("DNS and local DNS caching"); object dns = J.Get(network, "dns"); ObjectCards(dns);
            page.Children.Add(Section("DNS performance", "Settings/Advanced networking/DNS/DNS performance"));
            page.Children.Add(LocalButton("Edit DNS settings", delegate
            {
                string servers = String.Join(Environment.NewLine, J.List(J.Get(dns, "custom", "ips")).Select(x => J.S(x)));
                Edit("DNS", new[] { new Field("mode", "DNS servers", J.S(dns, "mode") == "custom" ? "Custom" : "ISP default", "choice", "ISP default", "Custom"), new Field("servers", "Custom DNS server IP addresses (one per line)", servers, "multiline"), new Field("caching", "Local DNS caching", J.B(dns, "caching"), "bool") }, async v =>
                {
                    object body = FeatureContracts.Dns(V(v, "mode") == "Custom", V(v, "servers"), Flag(v, "caching"));
                    if (!Confirm("Apply DNS settings? eero may restart the network. Advanced Security can prevent custom DNS.")) return;
                    await Change("PUT", NetResource("settings"), body, false, "DNS settings updated.");
                });
            }));
        }
        private void DhcpPage()
        {
            Sub("DHCP & NAT"); ObjectCards(J.Obj("connection", J.Get(network, "connection"), "dhcp", J.Get(network, "dhcp"), "nat_port_randomization", J.Get(network, "nat_port_randomization")));
            page.Children.Add(LocalButton("Edit DHCP & NAT", delegate
            {
                string mode = J.S(network, "connection", "mode") == "bridged" ? "Bridge" : J.S(network, "dhcp", "mode") == "custom" ? "Manual" : "Automatic";
                Edit("DHCP & NAT", new[] { new Field("mode", "Mode", mode, "choice", "Automatic", "Manual", "Bridge"), new Field("subnet", "Manual subnet network address", J.S(network, "dhcp", "custom", "subnet_ip")), new Field("mask", "Manual subnet mask", J.S(network, "dhcp", "custom", "subnet_mask")), new Field("first", "First DHCP address", J.S(network, "dhcp", "custom", "start_ip")), new Field("last", "Last DHCP address", J.S(network, "dhcp", "custom", "end_ip")), new Field("randomization", "NAT port randomization", J.B(network, "nat_port_randomization"), "bool") }, async v =>
                {
                    string selected = V(v, "mode") == "Bridge" ? "bridge" : V(v, "mode") == "Manual" ? "custom" : "automatic";
                    object body = FeatureContracts.Dhcp(selected, V(v, "subnet"), V(v, "mask"), V(v, "first"), V(v, "last"), Flag(v, "randomization"));
                    if (!Confirm("Apply DHCP & NAT settings? The network may restart. Bridge mode disables routing, profiles and some security features.")) return;
                    await Change("PUT", NetResource("settings"), body, false, "DHCP & NAT settings updated.");
                });
            }));
        }
        private void NetworkBooleanPage(string key, string title, string description)
        {
            Sub(description);
            if (!(J.Get(network, key) is bool)) { page.Children.Add(Card(Text("eero did not report this setting for the selected network."))); return; }
            page.Children.Add(Card(Text(title + ": " + (J.B(network, key) ? "On" : "Off"), 20, "#17263C", true)));
            page.Children.Add(LocalButton("Change " + title, delegate { Edit(title, new[] { new Field(key, "Enable " + title, J.B(network, key), "bool") }, async v => { if (!Confirm("Apply " + title + " settings? Some devices may reconnect.")) return; await Change("PUT", NetResource("settings"), J.Obj(key, Flag(v, key)), false, title + " updated."); }); }));
        }
        private void WirelessPage()
        {
            Sub("Wireless settings");
            page.Children.Add(Section("Fast roaming", "Settings/Advanced networking/Wireless settings/Fast roaming"));
            var fields = new List<Field>();
            foreach (string key in new[] { "wpa3", "band_steering", "sqm", "ffs" }) if (J.Get(network, key) is bool) fields.Add(new Field(key, key == "band_steering" ? "Client steering" : key == "wpa3" ? "WPA3 security" : key == "ffs" ? "Amazon Frustration-Free Setup" : Human(key), J.B(network, key), "bool"));
            ObjectCards(J.Obj("wpa3", J.Get(network, "wpa3"), "client_steering", J.Get(network, "band_steering"), "optimization", J.Get(network, "sqm"), "multi_link_operation", J.Get(network, "mlo_mode")));
            if (fields.Count > 0) page.Children.Add(LocalButton("Edit wireless options", delegate { Edit("Wireless options", fields, async v => { var body = new Dictionary<string, object>(); foreach (Field f in fields) if (Flag(v, f.Key) != J.B(network, f.Key)) body[f.Key] = Flag(v, f.Key); if (body.Count == 0) return; if (!Confirm("Apply these wireless options? Devices may briefly reconnect.")) return; await Change("PUT", NetResource("settings"), body, false, "Wireless options updated."); }); }));
            page.Children.Add(Button("Legacy compatibility mode", async delegate
            {
                object value = await Read(FeatureUrl("ac_compat"));
                Edit("Legacy compatibility", new[] { new Field("enabled", "Enable legacy compatibility", J.B(value, "enabled"), "bool") }, async v => { await Change("PUT", FeatureUrl("ac_compat"), J.Obj("enabled", Flag(v, "enabled")), true, "Compatibility setting updated."); });
            }));
            if (J.Get(network, "mlo_mode") != null) page.Children.Add(LocalButton("Multi-link operation", delegate { Edit("Multi-link operation", new[] { new Field("mlo_mode", "Mode", J.S(network, "mlo_mode"), "choice", "disabled", "single", "multi") }, async v => { if (!Confirm("Change multi-link operation? Connected devices may reconnect.")) return; await Change("PUT", FeatureUrl("mlo_mode"), J.Obj("mlo_mode", V(v, "mlo_mode")), false, "Multi-link operation updated."); }); }));
            ActionRow(Button("Security by radio band", async delegate { await BandSecurity(); }), Button("Radio settings", async delegate { Info("Radio settings", await Read(FeatureUrl("radio_settings"))); }), Button("Channel utilization", async delegate { Info("Channel utilization", await Read(FeatureUrl("channel_utilization?") + StatsRange(1))); }));
        }
        private void ThreadPage(object data)
        {
            Sub("Thread smart-home network"); ObjectCards(data);
            ActionRow(LocalButton("View Thread credentials", delegate { ShowCredentials("Thread credentials", preview ? new Dictionary<string, string> { { "Thread dataset", "Sample-only-dataset" } } : EverydayContracts.ThreadCredentials(data)); }), Button("Regenerate Thread credentials", async delegate
            {
                if (!Confirm("Regenerate this Thread network's credentials? Thread devices may need to be paired again.")) return;
                await Change("POST", FeatureUrl("thread"), null, false, "Thread credential regeneration requested.");
            }));
            page.Children.Add(LocalButton("Edit Thread settings", delegate { Edit("Thread", new[] { new Field("thread_enable", "Enable Thread network", J.B(network, "thread"), "bool"), new Field("enable_credential_syncing", "Sync Thread credentials", J.B(data, "enable_credential_syncing"), "bool") }, async v => { await Change("PUT", FeatureUrl("thread"), J.Obj("thread_enable", Flag(v, "thread_enable"), "enable_credential_syncing", Flag(v, "enable_credential_syncing")), false, "Thread settings updated."); }); }));
        }
        private void DdnsPage()
        {
            Sub("Dynamic DNS requires an eligible eero subscription."); ObjectCards(J.Get(network, "dynamic_dns"));
            ActionRow(Button("Enable Dynamic DNS", async delegate { await Change("PUT", FeatureUrl("ddns/enable"), null, false, "Dynamic DNS enabled."); }), Button("Disable Dynamic DNS", async delegate { if (!Confirm("Disable Dynamic DNS for this network?")) return; await Change("PUT", FeatureUrl("ddns/disable"), null, false, "Dynamic DNS disabled."); }));
        }
        private static List<object> Entries(object data, params string[] names)
        {
            var direct = J.List(data); if (direct.Count > 0) return direct;
            foreach (string name in names) { var a = J.List(J.Get(data, name)); if (a.Count > 0) return a; }
            return new List<object>();
        }
        private void RoutingPage(object data)
        {
            Sub("IPv4 reservations & port forwards / IPv6 firewall rules");
            if (J.S(network, "connection", "mode") == "bridged") { page.Children.Add(Card(Text("These routing features require eero to run in Automatic or Manual NAT mode."))); return; }
            if (data == null) { page.Children.Add(Text("Loading network rules…", 14, "#64738A")); return; }
            foreach (string type in new[] { "reservations", "forwards", "pinholes" })
            {
                string kind = type; var list = J.List(J.Get(data, kind));
                page.Children.Add(Text(kind == "reservations" ? "IPv4 reservations" : kind == "forwards" ? "Port forwards" : "IPv6 firewall rules", 20, "#17263C", true));
                foreach (object raw in list)
                {
                    object item = raw; var p = new StackPanel(); Details(p, item, 0); var row = new WrapPanel();
                    row.Children.Add(LocalButton("Edit", delegate { RoutingEdit(kind, item, data); }));
                    row.Children.Add(Button("Delete", async delegate
                    {
                        if (!Confirm(kind == "reservations" ? "Delete this reservation and its associated port forwards?" : "Delete this rule?")) return;
                        string url = ObjectUrl(item); if (String.IsNullOrEmpty(url)) throw new InvalidOperationException("eero did not provide an edit URL for this entry.");
                        await Change("DELETE", url + (kind == "reservations" ? "?delete_forwards=true" : ""), null, false, "Rule deleted.");
                    })); p.Children.Add(row); page.Children.Add(Card(p, 18));
                }
                if (list.Count == 0) page.Children.Add(Text("No entries returned.", 13, "#64738A"));
                page.Children.Add(LocalButton(kind == "reservations" ? "Add reservation" : kind == "forwards" ? "Open a port" : "Add IPv6 firewall rule", delegate { RoutingEdit(kind, null, data); }));
            }
        }
        private void RoutingEdit(string kind, object item, object rules)
        {
            bool edit = item != null;
            string container = J.S(rules, kind, "url").NullIfEmpty() ?? (kind == "pinholes" ? FeatureUrl("ipv6/pinholes") : NetResource(kind));
            string url = edit ? ObjectUrl(item) : container;
            if (kind == "reservations")
            {
                Edit(edit ? "Edit reservation" : "Add reservation", new[] { new Field("name", "Nickname", J.S(item, "description")), new Field("mac", "MAC address", J.S(item, "mac")), new Field("ip", "Reserved IPv4 address", J.S(item, "ip")) }, async v => { await Change(edit ? "PUT" : "POST", url, FeatureContracts.Reservation(V(v, "name"), V(v, "mac"), V(v, "ip")), false, "Reservation saved."); });
            }
            else if (kind == "forwards")
            {
                Edit(edit ? "Edit port forward" : "Open a port", new[] { new Field("description", "Nickname", J.S(item, "description")), new Field("ip", "Device IPv4 address", J.S(item, "ip")), new Field("gateway_port", "External port or range", J.S(item, "gateway_port")), new Field("client_port", "Internal port or range", J.S(item, "client_port")), new Field("protocol", "Protocol", J.S(item, "protocol").NullIfEmpty() ?? "tcp", "choice", "tcp", "udp", "both"), new Field("enabled", "Enabled", !edit || J.B(item, "enabled"), "bool") }, async v => { object body = FeatureContracts.Forward(V(v, "ip"), V(v, "gateway_port"), V(v, "client_port"), V(v, "protocol"), V(v, "description"), Flag(v, "enabled")); if (!Confirm("Save this port forward? It allows matching inbound internet traffic to the selected device.")) return; await Change(edit ? "PUT" : "POST", url, body, false, "Port forward saved."); });
            }
            else
            {
                var choices = devices.Where(d => J.S(d, "url").Length > 0).ToDictionary(d => Device(d).Name + " · " + J.S(d, "mac"), d => J.S(d, "url"));
                if (choices.Count == 0) throw new InvalidOperationException("No device URLs were returned for an IPv6 rule.");
                string chosen = choices.FirstOrDefault(p => p.Value == J.S(item, "device")).Key ?? choices.Keys.First();
                Edit(edit ? "Edit IPv6 firewall rule" : "IPv6 firewall rule", new[] { new Field("device", "Device", chosen, "choice", choices.Keys.ToArray()), new Field("protocol", "Protocol", J.S(item, "protocol").NullIfEmpty() ?? "tcp", "choice", "tcp", "udp", "both"), new Field("port", "Port or range", J.S(item, "port")) }, async v => { var body = J.Obj("device", choices[V(v, "device")], "protocol", V(v, "protocol"), "port", FeatureContracts.PortRange(V(v, "port"))); if (!Confirm("Allow this inbound IPv6 traffic to the selected device?")) return; await Change(edit ? "PUT" : "POST", url, body, false, "IPv6 firewall rule saved."); });
            }
        }
        private void PowerPage(object data)
        {
            Sub("Power optimization and scheduled power saving.");
            ActionRow(LocalButton("Power saving options", delegate { Edit("Power saving", new[] { new Field("enable", "Enable power saving", J.B(network, "power_saving"), "bool"), new Field("scheduled", "Enable scheduled power saving", J.B(network, "power_saving_schedule_enabled"), "bool") }, async v => { await Change("PUT", FeatureUrl("power_saving"), J.Obj("enable", Flag(v, "enable"), "power_saving_schedule_enabled", Flag(v, "scheduled")), false, "Power saving updated."); }); }), LocalButton("Add power schedule", delegate { ScheduleEdit(null, FeatureUrl("power_saving/schedules"), true); }));
            foreach (object raw in Entries(data, "schedules"))
            {
                object item = raw; var p = new StackPanel(); Details(p, item, 0); var row = new WrapPanel();
                string url = ObjectUrl(item, FeatureUrl("power_saving/schedules/" + J.First(item, "id", "schedule_id")));
                row.Children.Add(LocalButton("Edit schedule", delegate { ScheduleEdit(item, url, true); })); row.Children.Add(Button("Delete schedule", async delegate { if (!Confirm("Delete this power saving schedule?")) return; await Change("DELETE", url, null, false, "Schedule deleted."); })); p.Children.Add(row); page.Children.Add(Card(p));
            }
        }
        private void ScheduleEdit(object item, string url, bool power = false)
        {
            var days = new HashSet<string>(J.List(J.Get(item, "days")).Select(x => J.S(x)));
            var fields = new List<Field> { new Field("name", "Schedule name", J.S(item, "name")), new Field("start", "Start time (24-hour HH:mm)", J.S(item, power ? "start_time" : "start").NullIfEmpty() ?? "22:00"), new Field("end", "End time (24-hour HH:mm)", J.S(item, power ? "end_time" : "end").NullIfEmpty() ?? "07:00"), new Field("enabled", "Enabled", item == null || J.B(item, "enabled"), "bool") };
            foreach (string day in new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" }) fields.Add(new Field(day, Human(day.ToLowerInvariant()), days.Contains(day), "bool"));
            Edit(item == null ? "Add schedule" : "Edit schedule", fields, async v => { var selected = new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" }.Where(d => Flag(v, d)); object body = FeatureContracts.Schedule(V(v, "name"), selected, V(v, "start"), V(v, "end"), Flag(v, "enabled"), power); await Change(item == null ? "POST" : "PUT", url, body, false, "Schedule saved."); }, "Save", "Times follow the network timezone. End times earlier than start times continue into the next day.");
        }
        private void BlockedPage(object data)
        {
            Sub("Blocked devices");
            foreach (object raw in Entries(data, "devices"))
            { object item = raw; var p = new StackPanel(); p.Children.Add(Text(Device(item).Name, 19, "#17263C", true)); Detail(p, "MAC address", J.S(item, "mac")); p.Children.Add(Button("Unblock device", async delegate { if (!Confirm("Allow this device to access your network?")) return; await Change("DELETE", FeatureUrl("blacklist/" + Uri.EscapeDataString(J.S(item, "mac"))), null, false, "Device unblocked."); })); page.Children.Add(Card(p)); }
            if (data != null && Entries(data, "devices").Count == 0) page.Children.Add(Text("No blocked devices returned.", 14, "#64738A"));
        }
        private void EditWanAddress()
        {
            object lease = J.Get(network, "lease"); object stat = J.Get(lease, "static");
            Edit("Internet addressing", new[] { new Field("mode", "WAN address assignment", J.S(lease, "mode") == "static" ? "Static IP" : "DHCP", "choice", "DHCP", "Static IP"), new Field("ip", "Static IPv4 address", J.S(stat, "ip")), new Field("mask", "Subnet mask", J.S(stat, "mask")), new Field("router", "Router / gateway", J.S(stat, "router")) }, async v =>
            {
                bool isStatic = V(v, "mode") == "Static IP"; var body = J.Obj("mode", isStatic ? "static" : "dhcp");
                if (isStatic) body["static"] = J.Obj("ip", FeatureContracts.Address(V(v, "ip"), AddressFamily.InterNetwork), "mask", FeatureContracts.Address(V(v, "mask"), AddressFamily.InterNetwork), "router", FeatureContracts.Address(V(v, "router"), AddressFamily.InterNetwork));
                if (!Confirm("Apply the internet addressing settings supplied by your ISP? Incorrect values can disconnect the network.")) return;
                await Change("PUT", NetResource("settings"), J.Obj("lease", body), false, "Internet addressing updated.");
            });
        }
    }
}
