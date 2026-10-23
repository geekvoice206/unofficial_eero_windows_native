using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private int usageDays = 7;
        private void ActivityMenu() { NativeActivity(); }
        private void InternetPage(object history)
        {
            Sub("Internet");
            ObjectCards(J.Obj("health", J.Get(network, "health"), "gateway_ip", J.Get(network, "gateway_ip"), "internet_provider", J.Get(network, "isp"), "last_speed_measurement", J.Get(network, "speed")));
            ActionRow(Button("Run speed test", StartSpeedTest), Section("Internet connection", "Settings/Advanced networking/Internet connection"), Section("Internet backup", "Settings/Advanced networking/Internet backup"), Section("Multi-WAN", "Settings/Advanced networking/Multi-WAN"));
            ObjectCards(history, "Speed test history");
        }
        private string StatsRange(int days = 7)
        {
            DateTime end = DateTime.UtcNow; DateTime start = end.AddDays(-days);
            return "start=" + Uri.EscapeDataString(start.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) + "&end=" + Uri.EscapeDataString(end.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        }
        private void DataUsagePage(object data)
        {
            Sub("Data usage over the last " + usageDays + " days. History availability depends on your plan.");
            ActionRow(Button("Devices & profiles breakdown", async delegate { Info("Usage breakdown", await Read(FeatureUrl("data_usage/breakdown?") + StatsRange(usageDays))); }), LocalButton("Choose date range", delegate
            {
                Edit("Data usage range", new[] { new Field("days", "Period", usageDays == 1 ? "1 day" : usageDays + " days", "choice", "1 day", "7 days", "30 days") }, async v => { usageDays = Int32.Parse(V(v, "days").Split(' ')[0]); featureData[currentPage] = await Read(FeatureUrl("data_usage?") + StatsRange(usageDays) + "&cadence=daily"); RenderPage(); }, "Load");
            }), Button("Data usage notifications", async delegate { object settings = await Read(FeatureUrl("data_usage/report_settings")); Edit("Data usage reports", new[] { new Field("cadence", "Report frequency", J.S(settings, "cadence") == "monthly" ? "Monthly" : "Weekly", "choice", "Weekly", "Monthly") }, async v => { bool weekly = V(v, "cadence") == "Weekly"; await Change("PUT", FeatureUrl("data_usage/report_settings"), J.Obj("cadence", weekly ? "weekly" : "monthly", "notification_day", weekly ? 1 : 31), false, "Data usage report frequency updated."); }); }));
            AddSeriesCharts(page, data, true);
            page.Children.Add(Button("Measurement details", async delegate { Info("Network usage", data); await Task.FromResult(0); }));
        }
        private void InsightsPage(object data)
        {
            Sub("Security activity over the last seven days. Requires the corresponding eero subscription.");
            ActionRow(Button("By device", async delegate { Info("Security by device", await Read(FeatureUrl("insights/devices?") + StatsRange())); }), Button("By profile", async delegate { Info("Security by profile", await Read(FeatureUrl("insights/profiles?") + StatsRange())); }));
            AddSeriesCharts(page, data, false);
            page.Children.Add(Button("Activity details", async delegate { Info("Security activity", data); await Task.FromResult(0); }));
        }
        private void PermissionsPage(object data)
        {
            Sub("User permissions");
            page.Children.Add(Section("Join a network", "Settings/User permissions/Join a network"));
            page.Children.Add(Section("Incoming access invitations", "Settings/User permissions/Incoming access invitations"));
            page.Children.Add(Button("Create admin invitation", async delegate
            {
                if (preview) throw new InvalidOperationException("Preview mode is read only.");
                if (!Confirm("Create a link that grants administrator access to the person who accepts it?")) return;
                object invite = await api.Send("POST", FeatureUrl("invites"), RuntimeContracts.AdminInvite());
                string link = J.S(invite, "invite_url"); if (String.IsNullOrEmpty(link)) throw new InvalidOperationException("eero created a response without an invitation link. Refresh invitations before trying again.");
                var p = new StackPanel(); p.Children.Add(Text("Admin invitation", 23, "#17263C", true)); p.Children.Add(Text("Share this link only with the person you want to administer your network.", 13, "#64738A")); p.Children.Add(new TextBox { Text = link, IsReadOnly = true }); var copy = new Button { Content = "Copy invitation link" }; copy.Click += delegate { Clipboard.SetText(link); }; p.Children.Add(copy); Dialog("Admin invitation", p).ShowDialog(); await LoadFeature(currentPage); RenderPage();
            }));
            page.Children.Add(Text("Members", 20, "#17263C", true));
            foreach (object raw in Entries(J.Get(data, "members"), "members"))
            {
                object member = raw; var p = new StackPanel(); Details(p, member, 0);
                string id = J.First(member, "user_id", "id");
                if (id.Length > 0 && J.S(member, "user_role") != "owner" && J.S(member, "user_role") != "admin") p.Children.Add(Button("Invite to administrator access", async delegate { if (!Confirm("Send an administrator access invitation to " + J.First(member, "user_name", "name") + "?")) return; await Change("POST", FeatureUrl("member_promotion"), J.Obj("member_id", ExtendedContracts.Identifier(id)), false, "Administrator access invitation sent."); }));
                if (J.S(member, "user_role") != "owner" && id.Length > 0) p.Children.Add(Button("Remove administrator", async delegate { if (!Confirm("Remove administrator access for " + J.First(member, "user_name", "name") + "?")) return; await Change("DELETE", FeatureUrl("admins/" + Uri.EscapeDataString(id)), null, false, "Administrator access removed."); }));
                page.Children.Add(Card(p));
            }
            page.Children.Add(Text("Invitations", 20, "#17263C", true));
            foreach (object raw in Entries(J.Get(data, "invites"), "invites"))
            {
                object invite = raw; var p = new StackPanel(); Details(p, invite, 0); string id = J.First(invite, "invite_id", "id");
                if (id.Length > 0)
                {
                    var row = new WrapPanel(); row.Children.Add(LocalButton("Rename invitation", delegate { Edit("Invitation nickname", new[] { new Field("name", "Nickname", J.S(invite, "invite_nickname")) }, async v => { ValidateName(V(v, "name")); await Change("PUT", FeatureUrl("invites/" + Uri.EscapeDataString(id)), J.Obj("invite_nickname", V(v, "name").Trim()), false, "Invitation nickname updated."); }); }));
                    row.Children.Add(Button("Cancel invitation", async delegate { if (!Confirm("Cancel this invitation? Its link will stop granting access.")) return; await Change("DELETE", FeatureUrl("invites/" + Uri.EscapeDataString(id)), null, false, "Invitation canceled."); })); p.Children.Add(row);
                }
                page.Children.Add(Card(p));
            }
        }
        private void UpdatesPage(object data)
        {
            Sub("Software updates"); ObjectCards(data);
            var install = Button("Install available update", async delegate
            {
                if (!J.B(data, "can_update_now")) throw new InvalidOperationException("eero has not reported an update that can be installed now.");
                if (!Confirm("Install the available eero software update? Your network will restart during installation.")) return;
                await Change("POST", NetResource("updates"), "", false, "Software update requested. Installation can take several minutes.");
            }); install.IsEnabled = J.B(data, "can_update_now"); page.Children.Add(install);
            page.Children.Add(LocalButton("Preferred update hour", delegate { Edit("Preferred update hour", new[] { new Field("hour", "Hour in network timezone (0–23)", J.S(data, "preferred_update_hour")) }, async v => { int n; if (!Int32.TryParse(V(v, "hour"), out n) || n < 0 || n > 23) throw new ArgumentException("Enter an hour between 0 and 23."); await Change("POST", NetResource("updates") + "/preferred_update_hour", J.Obj("preferred_update_hour", n), false, "Preferred update hour updated."); }); }));
            page.Children.Add(Text("The app requests firmware installation from eero. It does not bypass rollout eligibility or supply its own firmware.", 13, "#64738A"));
        }
        private void NotificationsPage(object data)
        {
            Sub("Notifications"); ObjectCards(data, "Network preferences");
            ActionRow(LocalButton("Edit network notifications", delegate { EditReturnedBooleans("Network notifications", data, FeatureUrl("notifications")); }), Button("Notification history", async delegate { Info("Notification history", await Read(FeatureUrl("notifications_history?limit=50"))); }), Button("Mark notifications read", async delegate { await Change("POST", FeatureUrl("notifications/mark_read"), null, false, "Notifications marked read."); }), LocalButton("Account offline notifications", delegate { EditReturnedBooleans("Offline notifications", J.Get(account, "push_settings"), "/2.2/account/push_settings"); }));
            page.Children.Add(Section("Windows notifications & startup", "Settings/Notifications/Windows notifications"));
            page.Children.Add(Text("Windows alerts check eero notification history while the app is running. Enable background mode to keep receiving alerts after closing the main window.", 13, "#64738A"));
        }
        private void EditReturnedBooleans(string title, object data, string url)
        {
            var map = data as IDictionary<string, object>;
            if (map == null) throw new InvalidOperationException("eero did not return editable settings.");
            var fields = map.Where(p => p.Value is bool).Select(p => new Field(p.Key, Human(p.Key), p.Value, "bool")).ToArray();
            if (fields.Length == 0) throw new InvalidOperationException("No editable notification flags were returned.");
            Edit(title, fields, async v => { var body = new Dictionary<string, object>(); foreach (Field f in fields) body[f.Key] = Flag(v, f.Key); await Change("PUT", url, body, false, "Notification preferences updated."); });
        }
        private void TroubleshootingPage()
        {
            Sub("Troubleshooting");
            FeatureMenu(currentPage, false);
            ActionRow(Button("Check cloud connection", async delegate { await api.CheckConnection(); SetNotice("Secure connection to eero verified."); }), Button("Network diagnostics", async delegate { Info("Network diagnostics", await Read(NetResource("diagnostics"))); }), Button("Run network diagnostics", async delegate { if (preview) throw new InvalidOperationException("Sign in to run diagnostics."); await api.Send("POST", NetResource("diagnostics")); SetNotice("Diagnostics requested. Open Network diagnostics shortly to review the results."); }), Button("Network scan", async delegate { Info("Network scan", await Read(FeatureUrl("network_scan"))); }));
            string networkId = networkUrl.TrimEnd('/').Split('/').Last();
            ActionRow(Button("Coverage scan", async delegate { Info("Coverage scan", await Read("/2.4/networks/" + Uri.EscapeDataString(networkId) + "/placement/wifi_coverage_scan?allDevices=true")); }), Button("eero upgrade report", async delegate { Info("Upgrade report", await Read("/2.4/networks/" + Uri.EscapeDataString(networkId) + "/placement/wifi_generation_scan?allDevices=true")); }), Section("Legacy device compatibility", "Settings/Advanced networking/Wireless settings"));
            page.Children.Add(Button("Official troubleshooting articles", async delegate { Official("https://www.eero.com/support"); await Task.FromResult(0); }));
        }
        private static string Contact(object value) { return value is string ? (string)value : J.First(value, "value", "email", "phone", "number"); }
        private void AccountPage()
        {
            Sub("Account settings"); var p = new StackPanel(); Detail(p, "Name", J.S(account, "name")); Detail(p, "Email", Contact(J.Get(account, "email"))); Detail(p, "Phone", Contact(J.Get(account, "phone"))); Detail(p, "Role", J.S(account, "role"));
            string auth = J.S(account, "auth", "type"); Detail(p, "Sign-in method", auth == "eero" ? "Email or text verification code" : auth == "amazon" ? "Amazon" : auth == "sso" ? "Internet provider" : "Not reported by eero"); page.Children.Add(Card(p));
            ActionRow(LocalButton("Change name", delegate { Edit("Account name", new[] { new Field("name", "Name", J.S(account, "name")) }, async v => { ValidateName(V(v, "name")); await Change("PUT", "/2.2/account/name", J.Obj("name", V(v, "name").Trim()), true, "Account name updated."); }); }), LocalButton("Change email", delegate { Edit("Account email", new[] { new Field("email", "New email address") }, async v => { string email = V(v, "email").Trim(); if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) throw new ArgumentException("Enter a valid email address."); await Change("PUT", "/2.2/account/email", J.Obj("email", email), true, "Email change requested. Use Verify new email to enter its code."); }); }), LocalButton("Verify new email", delegate { VerifyContact("email"); }), LocalButton("Change phone", delegate { Edit("Account phone", new[] { new Field("phone", "New phone number (include country code)") }, async v => { await Change("PUT", "/2.2/account/phone", J.Obj("phone", PhoneInput.Normalize(V(v, "phone"))), true, "Phone change requested. Use Verify new phone to enter its code."); }); }), LocalButton("Verify new phone", delegate { VerifyContact("phone"); }));
            page.Children.Add(LocalButton("Email preferences", delegate { Edit("Email preferences", new[] { new Field("marketing", "Receive eero marketing emails", J.B(account, "consents", "marketing_emails"), "bool") }, async v => { await Change("PUT", "/2.2/account/consents", J.Obj("marketing_emails", Flag(v, "marketing")), true, "Email preferences updated."); }); }));
            page.Children.Add(Section("Subscriptions & partner services", "Settings/Account settings/Subscriptions"));
            page.Children.Add(LocalButton("Delete eero account", delegate { Edit("Delete account", new[] { new Field("confirm", "Type DELETE MY EERO ACCOUNT to confirm") }, async v => { if (V(v, "confirm") != "DELETE MY EERO ACCOUNT") throw new ArgumentException("The confirmation text does not match."); if (!Confirm("Permanently delete your eero account and associated account data? This can remove your ability to manage networks.")) return; if (preview) throw new InvalidOperationException("Preview mode is read only."); await api.Send("DELETE", "/2.2/account"); api.Forget(); timer.Stop(); ClearData(); ShowLogin(false); SetNotice("Account deletion request accepted."); }, "Delete account"); }));
        }
        private void VerifyContact(string type)
        { Edit("Verify new " + type, new[] { new Field("code", "Verification code") }, async v => { string code = V(v, "code").Trim(); if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[0-9]{4,10}$")) throw new ArgumentException("Enter the numeric verification code."); await Change("POST", "/2.2/account/" + type + "/verify", J.Obj("code", code), true, "Account " + type + " verified."); }, "Verify"); }
        private void SubscriptionPage()
        {
            Sub("Subscriptions & partner services");
            object status = J.Get(network, "premium_status") ?? J.Get(account, "premium_status"), details = J.Get(network, "premium_details") ?? J.Get(account, "premium_details");
            if (status != null || details != null) ObjectCards(J.Obj("premium_status", status, "premium_details", details));
            else page.Children.Add(Text(preview ? "Sample account. Sign in to load your plan details." : "No subscription summary was returned. Open Subscription details to check your account.", 14, "#64738A"));
            page.Children.Add(Button("Subscription details", async delegate { Info("Subscription details", await Read("/2.2/premium/customer")); }));
            if (network != null) page.Children.Add(Button("Available plans", async delegate { Info("Available plans", await Read("/2.4/premium/plans/" + NetworkId)); }));
            page.Children.Add(Button("eero Plus plans and information", async delegate { Official("https://www.eero.com/eero-plus"); await Task.FromResult(0); }));
            FeatureMenu("Settings/Account settings/Subscriptions", false);
            page.Children.Add(Text("Software-key activation and partner benefit codes are available below. Store purchases, Amazon account authorization and the VPN tunnel use their provider integrations.", 14, "#64738A"));
        }
        private void ConnectedPage(object data)
        {
            Sub("Connected services"); ObjectCards(J.Obj("amazon_account_linked", J.Get(network, "amazon_account_linked"), "alexa_skill", J.Get(network, "alexa_skill"), "frustration_free_setup", J.Get(network, "ffs"), "eero_built_in", data));
            page.Children.Add(Section("Connected device setup", "Settings/Connected services/Connected device setup"));
            page.Children.Add(Section("Amazon connected home", "Settings/Connected services/Amazon connected home"));
            page.Children.Add(Section("Accessories & cellular", "Settings/Connected services/Accessories & cellular"));
            ActionRow(Button("eero Built-in settings", async delegate { object settings = await Read(FeatureUrl("proxied_nodes")); Edit("eero Built-in", new[] { new Field("enabled", "Enable eero Built-in", J.B(settings, "enabled"), "bool") }, async v => { await Change("PUT", FeatureUrl("proxied_nodes"), J.Obj("enabled", Flag(v, "enabled")), false, "eero Built-in setting updated."); }); }), Button("Connected setup vendors", async delegate { Info("Connected setup vendors", await Read(FeatureUrl("simple_setup/vendors"))); }));
            page.Children.Add(Text("Amazon and Alexa account linking is managed by eero. New account links cannot be created here.", 13, "#64738A"));
        }
        private void NetworkManagementPage()
        {
            Sub("Network management"); page.Children.Add(Text("Use the network selector at the top to switch networks.", 14, "#64738A"));
            page.Children.Add(Section("Create a wired network", "Home/Add or replace eero/Create a network"));
            if (network == null) { ActionRow(Section("Join a network", "Settings/User permissions/Join a network"), Section("Pending transfers", "Settings/Network management/Pending transfers")); return; }
            page.Children.Add(LocalButton("Transfer network ownership", delegate { Edit("Transfer network", new[] { new Field("name", "Recipient name"), new Field("email", "Recipient email (or leave blank for phone)"), new Field("phone", "Recipient phone with country code (or leave blank for email)") }, async v =>
            {
                ValidateName(V(v, "name")); string email = V(v, "email").Trim(), phone = V(v, "phone").Trim(); if ((email.Length == 0) == (phone.Length == 0)) throw new ArgumentException("Enter either an email address or a phone number."); if (phone.Length > 0) phone = PhoneInput.Normalize(phone); else if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) throw new ArgumentException("Enter a valid email address.");
                if (!Confirm("Send an ownership-transfer invitation to this recipient? Ownership changes when the recipient accepts.")) return;
                await Change("POST", NetResource("transfer"), J.Obj("name", V(v, "name").Trim(), "email", email, "phone", phone), true, "Ownership transfer requested.");
            }, "Send transfer invitation"); }));
            ActionRow(Section("Pending network transfers", "Settings/Network management/Pending transfers"), Section("Outgoing ownership transfer", "Settings/Network management/Outgoing transfer"));
            page.Children.Add(LocalButton("Delete this network", delegate { Edit("Delete network", new[] { new Field("confirm", "Type the current wifi name to confirm") }, async v => { if (V(v, "confirm") != J.S(network, "name")) throw new ArgumentException("The wifi name does not match."); if (!Confirm("Delete this network from your account? This will disrupt its management and may require setting it up again.")) return; if (preview) throw new InvalidOperationException("Preview mode is read only."); await api.Send("DELETE", networkUrl); featureData.Clear(); network = null; networkUrl = null; await LoadAccount(); currentPage = "Home"; ShowShell(); SetNotice("Network deleted."); }, "Delete network"); }));
            page.Children.Add(Text("Full Bluetooth provisioning and physical gateway replacement require the official setup wizard.", 13, "#64738A"));
        }
        private void BackupPage(object data)
        {
            Sub("Internet backup"); ObjectCards(J.Get(data, "status"), "Backup status");
            page.Children.Add(Section("Find backup wifi", "Settings/Advanced networking/Internet backup/Find backup wifi"));
            page.Children.Add(LocalButton("Backup wifi priority", delegate { BackupPriority(Entries(J.Get(data, "access_points"), "access_points")); }));
            ActionRow(LocalButton("Backup options", delegate { Edit("Internet backup", new[] { new Field("enabled", "Enable backup internet", J.B(data, "status", "backup_internet_enabled"), "bool") }, async v => { await Change("PUT", FeatureUrl("backupinternet"), J.Obj("backup_internet_enabled", Flag(v, "enabled")), false, "Backup internet updated."); }); }), LocalButton("Add backup wifi", delegate { BackupEdit(null); }), Button("Test backup connectivity", async delegate { if (preview) throw new InvalidOperationException("Sign in to test backup connectivity."); await api.Send("POST", FeatureUrl("backup_access_points/connectivity_check")); SetNotice("Backup connectivity check requested. Refresh this page for results."); }));
            foreach (object raw in Entries(J.Get(data, "access_points"), "access_points"))
            {
                object item = raw; var p = new StackPanel(); Details(p, item, 0); var row = new WrapPanel(); row.Children.Add(LocalButton("Edit", delegate { BackupEdit(item); }));
                row.Children.Add(Button("Remove backup", async delegate { if (!Confirm("Remove this backup wifi network?")) return; await Change("DELETE", FeatureUrl("backup_access_points/" + Uri.EscapeDataString(J.S(item, "uuid"))), null, false, "Backup wifi removed."); })); p.Children.Add(row); page.Children.Add(Card(p));
            }
            ActionRow(Button("Discover backup wifi networks", async delegate { if (preview) throw new InvalidOperationException("Sign in to discover backup networks."); await api.Send("POST", FeatureUrl("backup_access_points/ssid_discovery")); SetNotice("Discovery requested. Use Discovery results shortly."); }), Button("Discovery results", async delegate { Info("Backup wifi discovery", await Read(FeatureUrl("backup_access_points/ssid_discovery"))); }));
        }
        private void BackupEdit(object data, string discoveredSsid = null)
        {
            Edit(data == null ? "Add backup wifi" : "Edit backup wifi", new[] { new Field("ssid", "Backup wifi name", discoveredSsid ?? J.S(data, "ssid")), new Field("password", "Backup wifi password", null, "password"), new Field("enabled", "Enabled", data == null || J.B(data, "enabled"), "bool") }, async v =>
            {
                ValidateSsid(V(v, "ssid")); if (data == null || V(v, "password").Length > 0) ValidatePassword(V(v, "password"));
                string id = data == null ? Guid.NewGuid().ToString() : J.S(data, "uuid");
                var body = J.Obj("ssid", V(v, "ssid").Trim(), "uuid", id); if (data != null) body["enabled"] = Flag(v, "enabled");
                if (V(v, "password").Length > 0) body["password"] = V(v, "password");
                await Change(data == null ? "POST" : "PUT", FeatureUrl("backup_access_points" + (data == null ? "" : "/" + Uri.EscapeDataString(id))), body, false, "Backup wifi saved.");
            }, "Save", data == null ? "Use a backup hotspot that your eero can reach." : "Leave the password blank to keep the existing password.");
        }
        private void MultiWanPage(object data)
        {
            Sub("Multi-WAN is available only on supported gateways and firmware."); ObjectCards(data, "WAN links");
            ActionRow(Button("Discover WAN links", async delegate { Info("Discovered WAN links", await Read(FeatureUrl("wan/discover"))); }), Button("WAN limits", async delegate { Info("WAN limits", await Read(FeatureUrl("wan/limits"))); }), LocalButton("WAN mode", delegate { Edit("WAN mode", new[] { new Field("mode", "Routing mode", "Automatic failover", "choice", "Automatic failover", "Strict", "Legacy") }, async v => { string mode = V(v, "mode") == "Strict" ? "MULTI_WAN_STRICT" : V(v, "mode") == "Legacy" ? "MULTI_WAN_LEGACY" : "MULTI_WAN_AUTOMATIC"; if (!Confirm("Change WAN routing mode? Internet connectivity may be interrupted.")) return; await Change("PUT", FeatureUrl("wan/enable_multi_wan"), J.Obj("wan_mode", mode), false, "WAN mode updated."); }); }));
            foreach (object raw in Entries(data, "links"))
            {
                object link = raw; string id = J.S(link, "link_id"); if (id.Length == 0) continue;
                var p = new StackPanel(); p.Children.Add(Text(J.First(link, "name", "link_id"), 19, "#17263C", true)); Details(p, link, 0);
                p.Children.Add(Button("Connection details", async delegate { Info("WAN link", await Read(FeatureUrl("wan/links/" + Uri.EscapeDataString(id)))); }));
                p.Children.Add(LocalButton("Name & priority", delegate { EditWanPriority(link, Entries(data, "links")); }));
                p.Children.Add(Button("Remove link", async delegate { if (!Confirm("Remove this WAN link? It may be your active internet connection.")) return; await Change("DELETE", FeatureUrl("wan/links/" + Uri.EscapeDataString(id)), null, false, "WAN link removed."); })); page.Children.Add(Card(p));
            }
            page.Children.Add(LocalButton("Add WAN link", delegate
            {
                var nodeChoices = nodes.Where(n => J.S(n, "url").Length > 0).ToDictionary(n => J.First(n, "location", "model") + " · " + J.S(n, "url").Split('/').Last(), n => J.S(n, "url").Split('/').Last()); if (nodeChoices.Count == 0) throw new InvalidOperationException("No eero node identifiers were returned.");
                Edit("Add WAN link", new[] { new Field("name", "Connection name"), new Field("node", "Gateway eero", nodeChoices.Keys.First(), "choice", nodeChoices.Keys.ToArray()), new Field("port", "Ethernet port number"), new Field("priority", "Priority", "1"), new Field("type", "Internet connection type", "DHCP", "choice", "DHCP", "Static IP", "PPPoE"), new Field("vlan", "VLAN ID (optional)"), new Field("ip", "Static IPv4 address (Static IP only)"), new Field("mask", "Static subnet mask (Static IP only)"), new Field("gateway", "Static gateway IPv4 (Static IP only)"), new Field("username", "PPPoE username (PPPoE only)"), new Field("password", "PPPoE password (PPPoE only)", null, "password") }, async v =>
                {
                    ValidateName(V(v, "name")); int node, port, priority; if (!Int32.TryParse(nodeChoices[V(v, "node")], out node) || !Int32.TryParse(V(v, "port"), out port) || port < 0 || port > 48 || !Int32.TryParse(V(v, "priority"), out priority) || priority < 0 || priority > 100) throw new ArgumentException("Enter valid port and priority values.");
                    string connectionType = V(v, "type") == "Static IP" ? "STATIC_IP" : V(v, "type") == "PPPoE" ? "PPPOE" : "DHCP";
                    var body = J.Obj("name", V(v, "name").Trim(), "eero_id", node, "port_number", port, "priority", priority, "connection_type", connectionType);
                    if (connectionType == "STATIC_IP") body["static_ip"] = J.Obj("static_ip", FeatureContracts.Address(V(v, "ip"), System.Net.Sockets.AddressFamily.InterNetwork), "subnet_mask", FeatureContracts.Address(V(v, "mask"), System.Net.Sockets.AddressFamily.InterNetwork), "gateway_ip", FeatureContracts.Address(V(v, "gateway"), System.Net.Sockets.AddressFamily.InterNetwork));
                    if (connectionType == "PPPOE") { if (V(v, "username").Length == 0 || V(v, "password").Length == 0) throw new ArgumentException("Enter the PPPoE username and password supplied by your ISP."); body["pppoe"] = J.Obj("username", V(v, "username"), "password", V(v, "password")); }
                    if (V(v, "vlan").Length > 0) { int vlan; if (!Int32.TryParse(V(v, "vlan"), out vlan) || vlan < 1 || vlan > 4094) throw new ArgumentException("VLAN IDs must be between 1 and 4094."); body["vlan_id"] = vlan.ToString(); }
                    if (!Confirm("Add this DHCP WAN link?")) return; await Change("POST", FeatureUrl("wan/links"), body, false, "WAN link added.");
                }, "Add link");
            }));
        }
        private void EditWanPriority(object link, List<object> links)
        {
            var ordered = links.OrderBy(x => { int p; return Int32.TryParse(J.S(x, "priority"), out p) ? p : 0; }).ToList();
            int position = ordered.FindIndex(x => J.S(x, "link_id") == J.S(link, "link_id")) + 1;
            Edit("WAN name & priority", new[] { new Field("name", "Connection name", J.S(link, "name")), new Field("position", "Priority position (1 is highest)", position.ToString()) }, async v =>
            {
                ValidateName(V(v, "name")); int index; if (!Int32.TryParse(V(v, "position"), out index) || index < 1 || index > ordered.Count) throw new ArgumentException("Enter a priority position between 1 and " + ordered.Count + ".");
                var others = ordered.Where(x => J.S(x, "link_id") != J.S(link, "link_id")).ToList(); others.Insert(index - 1, link);
                int baseline; if (!Int32.TryParse(J.S(ordered.First(), "priority"), out baseline)) baseline = 0;
                var updates = new List<object>();
                for (int i = 0; i < others.Count; i++) { object item = others[i]; var entry = J.Obj("link_id", J.S(item, "link_id"), "name", J.S(item, "link_id") == J.S(link, "link_id") ? V(v, "name").Trim() : J.S(item, "name"), "priority", baseline + i); object metadata = J.Get(item, "metadata"); if (metadata != null) entry["metadata"] = metadata; updates.Add(entry); }
                if (!Confirm("Apply the WAN priorities? This can switch your active internet connection.")) return;
                await Change("PUT", FeatureUrl("wan/links"), J.Obj("links", updates), false, "WAN names and priorities updated.");
            });
        }
        private void HelpPage()
        {
            Sub("Help & support");
            ActionRow(Button("Official eero support", async delegate { OpenServiceWindow("eero support", "https://www.eero.com/support"); await Task.FromResult(0); }), Section("Troubleshooting", "Settings/Troubleshooting"), Section("Feature availability", "Settings/About"), Section("Service availability", "Help/Service availability"));
            if (api.SignedIn) page.Children.Add(Button("Account support details", async delegate { Info("eero support", await Read("/2.2/account/eero_support")); }));
            ActionRow(LocalButton("Chat with eero", delegate { OpenChat(); }), LocalButton("Support history", delegate { OpenChat(true); }));
            page.Children.Add(Section("Contact support", "Help/Contact support"));
            page.Children.Add(Text("Chat availability depends on your eero account. eero's assistant and a human support conversation are separate services.", 13, "#64738A"));
        }
        private void AboutPage()
        {
            Sub("eero Desktop v1.0 · Independent Windows client");
            page.Children.Add(Text("Made by: Carlos B. (Los33)", 16, "#17263C", true));
            page.Children.Add(Card(Text("A native Windows application for managing your eero network. Availability depends on your network's hardware, firmware, account role and subscription.", 14)));
            page.Children.Add(Section("Service availability", "Help/Service availability"));
            foreach (var group in FeatureCatalog.Entries.Where(e => e.Parent.Length > 0).GroupBy(e => FeatureCatalog.Root(e.Path)))
            {
                page.Children.Add(Text(group.Key, 20, "#17263C", true));
                foreach (FeatureEntry entry in group) page.Children.Add(Text(entry.Path.Replace("/", " › ") + " — " + entry.Description, 13, "#64738A"));
            }
            page.Children.Add(Text("QR code generation uses QRCoder under the MIT license. Product-image sources and software licenses are listed in THIRD-PARTY-NOTICES.txt.", 12, "#64738A"));
        }
        private void PlatformPage()
        {
            Sub("Availability depends on your account, hardware, and service provider.");
            foreach (var item in new[] {
                new[] { "Bluetooth setup and gateway replacement", "Discover nearby eeros, run placement checks, or create a wired DHCP network from Add or replace eero. Full Bluetooth provisioning and the gateway replacement wizard are not available in this client." },
                new[] { "Billing and sign-in", "Subscription management, payment methods, purchase history and business billing open their official service pages. Benefits are refreshed from eero. Purchases made through another billing provider remain managed by that provider. Amazon sign-in opens Amazon’s secure page and requires an existing eero account using that Amazon login. It does not require a developer account or Business subscription." },
                new[] { "Guardian VPN", "Windows-device registration, locations, device removal and connect/disconnect controls are grouped under Security & privacy > VPN. The official Windows WireGuard component and administrator approval are required. Connection is reported verified only after a fresh Guardian handshake; failed startup attempts remove the tunnel. Live subscription eligibility and tunnel operation still require account testing." },
                new[] { "Chat and background delivery", "Chat, history, recommendations and human support depend on your eero account. Review requested network changes before approving them. Alexa opens as a separate web service. Windows alerts check notification history while the app is running, including in background mode." },
                new[] { "Business, ISP, cellular and accessory provisioning", "Portal settings/uploads, eero Insight handoff, license activation, existing-network PPPoE/VLAN, accessory details/removal, cellular status and phone-line associations are implemented. Initial PPPoE/static bootstrap, cellular activation and physical accessory pairing still require the provider setup flow." }
            }) { var p = new StackPanel(); p.Children.Add(Text(item[0], 19, "#17263C", true)); p.Children.Add(Text(item[1], 14, "#64738A")); page.Children.Add(Card(p)); }
        }
    }
}
