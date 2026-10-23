using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void ProfilesPageV2() { NativeProfiles(); }
        private void ProfileDetailPage(object profile) { NativeProfileDetail(profile); }
        private string ProfilePolicyUrl(object profile) { return FeatureUrl("dns_policies/profiles/" + J.S(profile, "url").TrimEnd('/').Split('/').Last()); }
        private void ProfileDevices(object profile)
        {
            var p = new StackPanel(); p.Children.Add(Text("Assign profile devices", 24, "#17263C", true)); p.Children.Add(Text("Choose the devices that belong to " + J.S(profile, "name") + ".", 13, "#64738A"));
            var current = new HashSet<string>(J.List(J.Get(profile, "devices")).Select(d => J.S(d, "url")));
            var all = devices.Concat(J.List(J.Get(profile, "devices"))).Where(d => J.S(d, "url").Length > 0).GroupBy(d => J.S(d, "url")).Select(g => g.First()).ToList();
            var checks = new List<CheckBox>();
            foreach (object device in all) { var check = new CheckBox { Content = Device(device).Name + " · " + J.S(device, "mac"), Tag = J.S(device, "url"), IsChecked = current.Contains(J.S(device, "url")) }; checks.Add(check); p.Children.Add(check); }
            var status = Text("", 13, "#B5333D"); p.Children.Add(status); var save = new Button { Content = "Save device assignments" }; p.Children.Add(save); var w = Dialog("Profile devices", p); bool working = false;
            save.Click += async delegate
            {
                if (working) return; working = true; save.IsEnabled = false;
                try { var selected = checks.Where(c => c.IsChecked == true).Select(c => J.Obj("url", (string)c.Tag)).ToArray(); await Change("PUT", J.S(profile, "url"), J.Obj("devices", selected), false, "Profile devices updated."); working = false; w.Close(); }
                catch (Exception e) { status.Text = Message(e); }
                finally { working = false; save.IsEnabled = true; }
            };
            w.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (working) e.Cancel = true; }; w.ShowDialog();
        }
        private void SecurityPage(object data)
        {
            Sub("Security & privacy");
            page.Children.Add(Text("Features depend on your eero subscription and network permissions.", 13, "#64738A"));
            ObjectCards(data, "Advanced Security and content filtering");
            ActionRow(LocalButton("Edit network protection", delegate { EditPolicies(data, FeatureUrl("dns_policies/network")); }), Button("Block & allow websites", async delegate { DomainDialog(await Read(FeatureUrl("dns_policies/advanced_content_filter")), null); }), LocalButton("Ad blocking", delegate
            {
                object ad = J.Get(data, "ad_block_settings");
                Edit("Ad blocking", new[] { new Field("enable", "Enable ad blocking", J.B(ad, "enabled"), "bool") }, async v => { await Change("POST", FeatureUrl("dns_policies/adblock"), J.Obj("enable", Flag(v, "enable"), "profiles", J.List(J.Get(ad, "profiles")).ToArray()), false, "Ad blocking updated."); });
            }), Section("Security activity", "Activity/Security activity"), Section("Family content filters", "Home/Profiles"));
            page.Children.Add(Section("Subnet content filters", "Home/Security & privacy/Content filters"));
            page.Children.Add(Section("Identity protection & digital footprint", "Home/Security & privacy/Identity protection"));
            page.Children.Add(Section("VPN powered by Guardian", "Home/Security & privacy/VPN"));
        }
        private void EditPolicies(object data, string url)
        {
            var map = J.Get(data, "dns_policies") as IDictionary<string, object>;
            var unified = J.Get(data, "unified_content_filters", "dns_policies") as IDictionary<string, object>;
            if (unified != null) { if (map == null) map = new Dictionary<string, object>(); else map = new Dictionary<string, object>(map); foreach (var entry in unified) map[entry.Key] = entry.Value; }
            if (map == null) map = data as IDictionary<string, object>;
            if (map == null) throw new InvalidOperationException("eero did not return editable content-filter settings.");
            string[] allowed = { "ad_block", "block_illegal_content", "block_malware", "block_pornographic_content", "block_violent_content", "safe_search_enabled", "youtube_restricted", "block_messaging_content", "block_social_content", "block_gaming_content", "block_streaming_content", "block_shopping_content" };
            var fields = map.Where(p => p.Value is bool && (allowed.Contains(p.Key) || unified != null)).Select(p => new Field(p.Key, Human(p.Key), p.Value, "bool")).ToArray();
            if (fields.Length == 0) throw new InvalidOperationException("No editable content-filter settings were returned for this account.");
            Edit("Security & content filters", fields, async v => { var changed = new Dictionary<string, object>(); foreach (Field f in fields) if (Flag(v, f.Key) != (bool)f.Value) changed[f.Key] = Flag(v, f.Key); if (changed.Count == 0) return; await Change("POST", url, changed, true, "Security settings updated."); });
        }
        private void DomainDialog(object data, object profile)
        {
            string profileId = profile == null ? null : J.S(profile, "url").TrimEnd('/').Split('/').Last();
            var p = new StackPanel(); p.Children.Add(Text("Block & allow websites", 24, "#17263C", true)); p.Children.Add(Text(profile == null ? "Applies to the whole network." : "Applies to " + J.S(profile, "name") + ".", 13, "#64738A")); Details(p, data, 0);
            var w = Dialog("Website controls", p);
            var add = new Button { Content = "Add or remove a website rule" }; p.Children.Insert(2, add);
            add.Click += delegate
            {
                w.Close();
                Edit("Website rule", new[] { new Field("domain", "Domain, e.g. example.com"), new Field("list", "Rule", "Block", "choice", "Block", "Allow"), new Field("remove", "Remove this rule instead of adding it", false, "bool") }, async v =>
                {
                    var body = J.Obj("domain", FeatureContracts.Domain(V(v, "domain")), "is_delete", Flag(v, "remove"));
                    if (profileId != null) body["profiles"] = new[] { profileId };
                    string path = FeatureUrl("dns_policies/" + (profileId == null ? "network/" : "profiles/") + (V(v, "list") == "Allow" ? "allowed" : "blocked"));
                    await Change("PUT", path, body, false, "Website rule updated.");
                });
            }; w.ShowDialog();
        }
        private async Task BlockApps(object profile)
        {
            string baseUrl = ProfilePolicyUrl(profile) + "/applications"; object available = await Read(baseUrl);
            var list = Entries(available, "applications", "data");
            if (list.Count == 0) { Info("Blocked applications", available); return; }
            var fields = new List<Field>(); var current = new HashSet<string>(J.List(J.Get(profile, "premium_dns", "blocked_applications")).Select(x => J.S(x)));
            foreach (object app in list)
            {
                string id = J.First(app, "id", "slug", "name"); if (id.Length > 0) fields.Add(new Field(id, J.First(app, "display_name", "name", "slug"), current.Contains(id) || J.B(app, "is_blocked"), "bool"));
            }
            if (fields.Count == 0) { Info("Available app controls", available); return; }
            Edit("Blocked applications", fields, async v => { var selected = fields.Where(f => Flag(v, f.Key)).Select(f => f.Key).ToArray(); await Change("PUT", baseUrl + "/blocked", J.Obj("applications", selected), false, "Blocked applications updated."); });
        }
        private void NodeDetailsPage(object node)
        {
            if (node == null) { page.Children.Add(Text("Select an eero from Home.")); return; }
            string url = J.S(node, "url"); Sub("Connection details and settings for this eero.");
            var identity = new Grid(); identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) }); identity.ColumnDefinitions.Add(new ColumnDefinition()); identity.Children.Add(HardwareImage(node, 72));
            var model = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; model.Children.Add(Text(J.S(node, "model"), 22, "#17263C", true)); model.Children.Add(Text(J.B(node, "gateway") ? "Gateway eero" : "Mesh eero", 13, "#64738A")); model.Children.Add(StatusBadge(IsOnline(node) ? "Online" : "Offline", IsOnline(node))); Grid.SetColumn(model, 1); identity.Children.Add(model); page.Children.Add(Card(identity, 20));
            var columns = Columns(); var information = Column(columns, 0); var settings = Column(columns, 1); page.Children.Add(columns);
            var info = new StackPanel(); info.Children.Add(Text("Device information", 20, "#17263C", true));
            Detail(info, "IP address", J.First(node, "ip_address", "ip")); Detail(info, "Connected devices", J.S(node, "connected_clients_count"));
            Detail(info, "eeroOS version", J.First(node, "os_version", "firmware_version")); Detail(info, "Serial number", J.S(node, "serial"));
            Detail(info, "MAC address", J.First(node, "mac_address", "mac"));
            var technical = LocalButton("All device details", delegate { Info("eero details", node); }); technical.Style = (Style)FindResource("QuietButton"); technical.HorizontalAlignment = HorizontalAlignment.Left; technical.Margin = new Thickness(-8, 8, 0, 0); info.Children.Add(technical); information.Children.Add(Card(info));
            var options = new StackPanel(); options.Children.Add(Text("Device settings", 20, "#17263C", true));
            string ledUrl = Resource(node, "led_action", null);
            if (ledUrl != null) {
                options.Children.Add(LocalButton("Status light", delegate { Edit("Status light", new[] { new Field("led_on", "Enable status light", J.B(node, "led_on"), "bool") }, async v => { await Change("PUT", ledUrl, J.Obj("led_on", Flag(v, "led_on")), true, "Status light updated."); }); }));
                options.Children.Add(LocalButton("Light brightness", delegate { Edit("Status light brightness", new[] { new Field("brightness", "Brightness (0–100)", J.S(node, "led_brightness")) }, async v => { int n; if (!Int32.TryParse(V(v, "brightness"), out n) || n < 0 || n > 100) throw new ArgumentException("Enter a brightness from 0 to 100."); await Change("PUT", ledUrl, J.Obj("led_brightness", n), true, "Brightness updated."); }); }));
            }
            if (J.Get(node, "nightlight") != null) options.Children.Add(LocalButton("Nightlight", delegate { NightlightEdit(node); }));
            string connections = Resource(node, "connections", null);
            if (connections != null) options.Children.Add(Button("Ethernet connections & ports", async delegate { PortDialog(node, await Read(connections)); }));
            options.Children.Add(Button("Firmware update history", async delegate { Info("Firmware history", await Read("/2.2/eeros/" + url.TrimEnd('/').Split('/').Last() + "/firmware_update/history?limit=20&offset=0")); })); settings.Children.Add(Card(options));
            var management = new StackPanel(); management.Children.Add(Text("Manage eero", 20, "#17263C", true));
            management.Children.Add(Button("Restart this eero", async delegate { if (!Confirm("Restart this eero? Connected devices may lose internet access temporarily.")) return; await Change("POST", Resource(node, "reboot", url + "/reboot"), null, false, "Restart requested."); }));
            var remove = LocalButton("Remove this eero", delegate { Edit("Remove eero", new[] { new Field("confirm", "Type the eero location name to confirm") }, async v => { if (V(v, "confirm") != J.S(node, "location")) throw new ArgumentException("The location name does not match."); if (!Confirm("Remove this eero from the network? Removing the gateway can take the entire network offline.")) return; currentPage = "Home/eeros"; selectedNode = null; await Change("DELETE", url, null, false, "eero removed."); }, "Remove eero"); }); remove.Foreground = Brush("#B5333D"); management.Children.Add(remove); settings.Children.Add(Card(management));
        }
        private void NightlightEdit(object node)
        {
            object light = J.Get(node, "nightlight");
            Edit("Nightlight", new[] { new Field("enabled", "Enable nightlight", J.B(light, "enabled"), "bool"), new Field("brightness", "Brightness (0–100)", J.S(light, "brightness_percentage")), new Field("scheduled", "Use a schedule", J.B(light, "schedule", "enabled"), "bool"), new Field("on", "On (HH:mm)", J.S(light, "schedule", "on").NullIfEmpty() ?? "20:00"), new Field("off", "Off (HH:mm)", J.S(light, "schedule", "off").NullIfEmpty() ?? "07:00") }, async v =>
            {
                int brightness; if (!Int32.TryParse(V(v, "brightness"), out brightness) || brightness < 0 || brightness > 100) throw new ArgumentException("Enter a brightness from 0 to 100.");
                FeatureContracts.Schedule("Nightlight", new[] { "MON" }, V(v, "on"), V(v, "off"), true);
                await Change("PUT", J.S(node, "url") + "/nightlight/settings", J.Obj("enabled", Flag(v, "enabled"), "brightness_percentage", brightness, "schedule", J.Obj("enabled", Flag(v, "scheduled"), "on", V(v, "on"), "off", V(v, "off"))), false, "Nightlight updated.");
            });
        }
        private void PortDialog(object node, object connections)
        {
            var p = new StackPanel(); p.Children.Add(Text("Ethernet connections", 24, "#17263C", true)); var w = Dialog("Ethernet ports", p); bool working = false; var status = Text("", 13, "#B5333D");
            foreach (object raw in J.List(J.Get(connections, "ports", "interfaces")))
            {
                object port = raw; var section = new StackPanel(); section.Children.Add(Text(J.S(port, "name").NullIfEmpty() ?? "Port " + J.S(port, "interface_number"), 19, "#17263C", true)); Details(section, port, 0);
                foreach (object action in J.List(J.Get(port, "actions")))
                {
                    string type = J.S(action, "type"); if (!new[] { "ENABLE_DATA", "DISABLE_DATA", "ENABLE_POE", "DISABLE_POE", "ENABLE_PORT", "DISABLE_PORT", "RESTART_POWER", "ENABLE_PORT_SECURITY", "DISABLE_PORT_SECURITY" }.Contains(type)) continue;
                    var b = new Button { Content = Human(type.ToLowerInvariant()) }; section.Children.Add(b);
                    b.Click += async delegate
                    {
                        if (working || !Confirm("Apply " + Human(type.ToLowerInvariant()) + " to " + J.S(port, "name") + "? It may interrupt data or PoE power.")) return;
                        working = true; b.IsEnabled = false;
                        try { await Change("POST", J.S(node, "url") + "/ports/" + J.S(port, "interface_number") + "/action", J.Obj("action", type), false, "Port action requested."); working = false; w.Close(); }
                        catch (Exception e) { status.Text = Message(e); }
                        finally { working = false; b.IsEnabled = true; }
                    };
                }
                p.Children.Add(Card(section, 16));
            }
            foreach (object action in J.List(J.Get(connections, "node_actions")))
            {
                string type = J.S(action, "type"); if (type != "POWER_CYCLE_ALL_PORTS" && type != "POWER_CYCLE_ALL_PORTS_AND_REBOOT") continue;
                var b = new Button { Content = Human(type.ToLowerInvariant()) }; p.Children.Add(b);
                b.Click += async delegate
                {
                    if (working || !Confirm("Apply this action to every Ethernet port on this eero? Devices may lose power and internet access.")) return;
                    working = true; b.IsEnabled = false;
                    try { await Change("POST", J.S(node, "url") + "/action", J.Obj("action", type), false, "eero port action requested."); working = false; w.Close(); }
                    catch (Exception e) { status.Text = Message(e); }
                    finally { working = false; b.IsEnabled = true; }
                };
            }
            p.Children.Add(status); w.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (working) e.Cancel = true; }; w.ShowDialog();
        }
        private void DeviceExtraActions(object device, Window parent, StackPanel body)
        {
            var profileChoices = profiles.Where(p => J.S(p, "url").Length > 0).ToDictionary(p => J.S(p, "name") + " · " + J.S(p, "url").Split('/').Last(), p => J.S(p, "url"));
            if (profileChoices.Count > 0) body.Children.Add(LocalButton("Assign to a profile", delegate
            {
                parent.Close(); Edit("Assign device to profile", new[] { new Field("profile", "Profile", profileChoices.Keys.First(), "choice", profileChoices.Keys.ToArray()) }, async v => { await Change("PUT", J.S(device, "url"), J.Obj("profile", J.Obj("url", profileChoices[V(v, "profile")])), false, "Device profile updated."); });
            }));
            body.Children.Add(LocalButton("Full connection details", delegate { parent.Close(); Info("Device details", device); }));
            body.Children.Add(LocalButton("Device type", delegate
            {
                parent.Close(); var names = FeatureContracts.DeviceTypes.ToDictionary(t => Human(t), t => t);
                string current = FeatureContracts.DeviceTypes.Contains(J.S(device, "device_type")) ? Human(J.S(device, "device_type")) : Human("generic");
                Edit("Device type", new[] { new Field("type", "Type", current, "choice", names.Keys.OrderBy(n => n).ToArray()) }, async v => { await Change("PUT", FeatureUrl("devices/" + Uri.EscapeDataString(J.S(device, "mac"))), J.Obj("device_type", names[V(v, "type")]), false, "Device type updated."); });
            }));
            body.Children.Add(Button("Data usage", async delegate { parent.Close(); Info("Device data usage", await Read(FeatureUrl("data_usage/devices/" + Uri.EscapeDataString(J.S(device, "mac")) + "?") + StatsRange() + "&cadence=daily")); }));
            if (J.Get(device, "secondary_wan_deny_access") is bool) body.Children.Add(LocalButton("Internet backup access", delegate
            {
                parent.Close(); Edit("Device internet backup", new[] { new Field("allow", "Allow this device to use secondary WAN / backup internet", !J.B(device, "secondary_wan_deny_access"), "bool") }, async v => { string id = networkUrl.TrimEnd('/').Split('/').Last(); await Change("PUT", "/2.3/networks/" + id + "/devices/" + Uri.EscapeDataString(J.S(device, "mac")), J.Obj("secondary_wan_deny_access", !Flag(v, "allow")), false, "Device backup access updated."); });
            }));
        }
        private void AddEeroPage()
        {
            Sub("Add or replace eero");
            FeatureMenu(currentPage, false);
            page.Children.Add(Card(Text("Add an eligible eero to the selected network by serial number. Nearby discovery, automatic setup and new wired networks are grouped above. Full Bluetooth provisioning and gateway replacement are not available in this client.", 14, "#64738A")));
            page.Children.Add(LocalButton("Add eero by serial number", delegate
            {
                Edit("Add eero", new[] { new Field("serial", "eero serial number"), new Field("location", "Location") }, async v =>
                {
                    string serial = V(v, "serial").Trim(); if (!System.Text.RegularExpressions.Regex.IsMatch(serial, "^[A-Za-z0-9-]{8,40}$")) throw new ArgumentException("Enter the serial number printed on the eero."); ValidateName(V(v, "location"));
                    if (!Confirm("Add the eero with this serial number to the selected network?")) return;
                    await Change("POST", "/2.2/eeros?requireSession=true", J.Obj("serial", serial, "location", V(v, "location").Trim(), "network", networkUrl), true, "eero addition requested.");
                }, "Add eero");
            }));
        }
    }
}
