using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private readonly Dictionary<string, object> featureData = new Dictionary<string, object>();
        private object selectedProfile, selectedNode;
        private string FeatureUrl(string suffix) { if (String.IsNullOrEmpty(networkUrl)) throw new InvalidOperationException("Select a network first."); return networkUrl.TrimEnd('/') + "/" + suffix.TrimStart('/'); }
        private string NetResource(string name) { return Resource(network, name, FeatureUrl(name)); }
        private static string ObjectUrl(object data, string fallback = null) { return J.S(data, "url").NullIfEmpty() ?? fallback; }
        private async void Navigate(string route)
        {
            if (busy) return;
            currentPage = route; RenderPage();
            if (route.Contains("/")) await Run(async delegate { await LoadFeature(route); if (currentPage == route) RenderPage(); }, "Loading " + route.Split('/').Last().ToLowerInvariant() + "…");
        }
        private void FeatureMenu(string parent, bool explanation = true) { NativeFeatureMenu(parent, explanation); }
        private async Task LoadFeature(string route)
        {
            if (preview) { featureData[route] = PreviewFeature(route); return; }
            if (!api.SignedIn) return;
            object data = null;
            try
            {
                switch (route)
                {
                    case "Help/Contact support": data = await api.Send("GET", "/2.2/account/eero_support"); break;
                    case "Settings/Account settings/Subscriptions/Billing & status": data = await api.Send("GET", "/2.2/premium/customer"); featureData["billing_customer"] = data; break;
                    case "Settings/Troubleshooting/Guided diagnostics": data = await api.Send("GET", NetResource("diagnostics")); break;
                    case "Settings/Advanced networking/Wireless settings/Fast roaming": data = await api.Send("GET", FeatureUrl("fast_transition")); break;
                    case "Settings/Advanced networking/Internet backup/Find backup wifi": data = await api.Send("GET", FeatureUrl("backup_access_points/ssid_discovery")); break;
                    case "Settings/Account settings/Subscriptions/Offers & referrals": if (!String.IsNullOrEmpty(networkUrl)) data = await api.Send("GET", "/2.4/premium/plans/" + NetworkId); break;
                    case "Settings/User permissions/Incoming access invitations": data = await api.Send("GET", "/2.2/incoming_member_promotions"); break;
                    case "Home/Add or replace eero/Automatic setup": data = await api.Send("GET", FeatureUrl("auto_discovered_eeros")); break;
                    case "Settings/Connected services/Accessories & cellular": data = await api.Send("GET", FeatureUrl("accessories/setup/status")); break;
                    case "Home/Profiles/Profile": if (selectedProfile != null) selectedProfile = data = await api.Send("GET", J.S(selectedProfile, "url")); break;
                    case "Home/eeros/Details": if (selectedNode != null) selectedNode = data = await api.Send("GET", J.S(selectedNode, "url")); break;
                    case "Settings/Network management/Pending transfers": data = await api.Send("GET", "/2.2/network_transfers/pending"); break;
                    case "Settings/Network management/Outgoing transfer": data = await api.Send("GET", NetResource("transfer")); break;
                    case "Settings/Connected services/Connected device setup": data = await api.Send("GET", FeatureUrl("simple_setup/vendors")); break;
                    case "Home/Security & privacy/Identity protection": data = await api.Send("GET", "/2.2/account/identity_protection"); break;
                    case "Settings/Advanced networking/DNS/DNS performance": object action; if (featureData.TryGetValue("dns_perf_action", out action)) data = await api.Send("GET", FeatureUrl("dns_perf_test/" + ExtendedContracts.Identifier(J.S(action)))); break;
                    case "Devices/Blocked devices": data = await api.Send("GET", FeatureUrl("blacklist")); break;
                    case "Home/Internet": data = await api.Send("GET", NetResource("speedtest")); break;
                    case "Home/Security & privacy": data = await api.Send("GET", FeatureUrl("dns_policies/network")); break;
                    case "Settings/Guest wifi network": data = await api.Send("GET", NetResource("guestnetwork")); break;
                    case "Settings/Advanced networking/Reservations & port forwarding": data = await api.Send("GET", NetResource("routing")); break;
                    case "Settings/Advanced networking/Thread": data = await api.Send("GET", NetResource("thread")); break;
                    case "Settings/User permissions": data = J.Obj("members", await api.Send("GET", FeatureUrl("members")), "invites", await api.Send("GET", FeatureUrl("invites"))); break;
                    case "Settings/Software updates": data = await api.Send("GET", NetResource("updates")); break;
                    case "Settings/Notifications": data = await api.Send("GET", FeatureUrl("notifications")); break;
                    case "Settings/Account settings": account = await api.Send("GET", "account"); data = account; break;
                    case "Settings/Advanced networking/Power saving": data = await api.Send("GET", FeatureUrl("power_saving/schedules")); break;
                    case "Settings/Advanced networking/Internet backup": data = J.Obj("status", await api.Send("GET", FeatureUrl("backupinternet")), "access_points", await api.Send("GET", FeatureUrl("backup_access_points"))); break;
                    case "Settings/Advanced networking/Multi-WAN": data = await api.Send("GET", FeatureUrl("wan/links")); break;
                    case "Activity/Data usage": data = await api.Send("GET", FeatureUrl("data_usage?") + StatsRange(usageDays) + "&cadence=daily"); break;
                    case "Activity/Security activity": data = await api.Send("GET", FeatureUrl("insights?") + StatsRange() + "&cadence=daily"); break;
                    case "Activity/Network events": data = await api.Send("GET", FeatureUrl("app_events?page_size=50")); break;
                    case "Settings/Connected services": data = await api.Send("GET", FeatureUrl("proxied_nodes")); break;
                    case "Settings/Additional wifi networks": case "Home/Security & privacy/Content filters": data = await api.Send("GET", FeatureUrl("subnets_config")); break;
                    case "Settings/Business guest wifi": data = await api.Send("GET", FeatureUrl("captive_portal/configurations")); break;
                    case "Settings/Advanced networking/Multiple static IPs": data = await api.Send("GET", "/2.3/networks/" + networkUrl.TrimEnd('/').Split('/').Last() + "/multistaticip"); break;
                }
                featureData[route] = data;
            }
            catch (ApiException e) { if (e.SessionExpired) throw; featureData[route] = e; }
        }
        private void RenderFeature()
        {
            object data; featureData.TryGetValue(currentPage, out data);
            var error = data as ApiException;
            if (error != null)
            {
                page.Children.Add(Card(Text(error.Message, 15, "#B5333D")));
                page.Children.Add(Text("Availability depends on your account permissions, subscription, hardware and firmware.", 13, "#64738A"));
                page.Children.Add(Button("Try again", async delegate { await LoadFeature(currentPage); RenderPage(); }));
                FeatureMenu(currentPage, false);
                return;
            }
            switch (currentPage)
            {
                case "Help/Contact support": SupportContactsPage(data); break;
                case "Settings/Account settings/Subscriptions/Billing & status": BillingOverview(); break;
                case "Settings/Account settings/Subscriptions/Business billing": OrganizationBilling(); break;
                case "Home/Chat": ChatPage(false); break;
                case "Help/Support history": ChatPage(true); break;
                case "Settings/Troubleshooting/Guided diagnostics": GuidedDiagnosticsPage(data); break;
                case "Settings/Troubleshooting/Connect a device": DeviceConnectionPage(); break;
                case "Settings/Advanced networking/Wireless settings/Fast roaming": FastRoamingPage(data); break;
                case "Settings/Advanced networking/Internet backup/Find backup wifi": BackupDiscoveryPage(data); break;
                case "Settings/Account settings/Subscriptions/Offers & referrals": SubscriptionOffersPage(data); break;
                case "Settings/Connected services/Amazon connected home": AmazonHomePage(); break;
                case "Settings/Notifications/Windows notifications": WindowsNotificationsPage(); break;
                case "Settings/User permissions/Incoming access invitations": IncomingAccessPage(data); break;
                case "Home/Add or replace eero/Find nearby eeros": BluetoothSetupPage(); break;
                case "Home/Add or replace eero/Automatic setup": AutomaticSetupPage(data); break;
                case "Home/Add or replace eero/Create a network": CreateNetworkPage(); break;
                case "Home/eeros/Placement tests": PlacementPage(); break;
                case "Home/Security & privacy/VPN": VpnPage(); break;
                case "Settings/Connected services/Accessories & cellular": AccessoriesPage(data); break;
                case "Settings/Business guest wifi/eero Insight": BusinessInsightsPage(); break;
                case "Settings/Account settings/Subscriptions/Manage subscriptions & purchases": SubscriptionWebPage(); break;
                case "Home/Internet": InternetPage(data); break;
                case "Home/eeros": NodePage(); break;
                case "Home/Profiles": ProfilesPageV2(); break;
                case "Home/Security & privacy": SecurityPage(data); break;
                case "Home/Add or replace eero": AddEeroPage(); break;
                case "Devices/Blocked devices": BlockedPage(data); break;
                case "Activity/Data usage": DataUsagePage(data); break;
                case "Activity/Security activity": InsightsPage(data); break;
                case "Activity/Network events": ObjectCards(data, "Network events"); break;
                case "Settings/Wifi name & password": WifiPage(); break;
                case "Settings/Guest wifi network": GuestPage(data); break;
                case "Settings/Additional wifi networks": AdditionalWifiPage(data); break;
                case "Settings/Business guest wifi": BusinessPortalPage(data); break;
                case "Home/Security & privacy/Content filters": SubnetFiltersPage(data); break;
                case "Settings/Nickname & location": NicknamePage(); break;
                case "Settings/Advanced networking": FeatureMenu(currentPage); break;
                case "Settings/Advanced networking/Internet connection": WanPage(); break;
                case "Settings/Advanced networking/DHCP & NAT": DhcpPage(); break;
                case "Settings/Advanced networking/DNS": DnsPage(); break;
                case "Settings/Advanced networking/Reservations & port forwarding": RoutingPage(data); break;
                case "Settings/Advanced networking/IPv6": NetworkBooleanPage("ipv6_upstream", "IPv6", "IPv6 connectivity from your internet provider."); break;
                case "Settings/Advanced networking/UPnP": NetworkBooleanPage("upnp", "UPnP", "Allow devices to request port mappings automatically."); break;
                case "Settings/Advanced networking/Wireless settings": WirelessPage(); break;
                case "Settings/Advanced networking/Thread": ThreadPage(data); break;
                case "Settings/Advanced networking/Dynamic DNS": DdnsPage(); break;
                case "Settings/Advanced networking/Power saving": PowerPage(data); break;
                case "Settings/Advanced networking/Internet backup": BackupPage(data); break;
                case "Settings/Advanced networking/Multi-WAN": MultiWanPage(data); break;
                case "Settings/Advanced networking/Multiple static IPs": MultiStaticPage(data); break;
                case "Settings/User permissions": PermissionsPage(data); break;
                case "Settings/Software updates": UpdatesPage(data); break;
                case "Settings/Appearance": AppearancePage(); break;
                case "Settings/Notifications": NotificationsPage(data); break;
                case "Settings/Troubleshooting": TroubleshootingPage(); break;
                case "Settings/Account settings": AccountPage(); break;
                case "Settings/Account settings/Subscriptions": SubscriptionPage(); break;
                case "Settings/Connected services": ConnectedPage(data); break;
                case "Settings/Network management": NetworkManagementPage(); break;
                case "Settings/About": AboutPage(); break;
                case "Help/Service availability": PlatformPage(); break;
                case "Home/Profiles/Profile": ProfileDetailPage(selectedProfile); break;
                case "Home/eeros/Details": NodeDetailsPage(selectedNode); break;
                case "Settings/User permissions/Join a network": JoinNetworkPage(); break;
                case "Settings/Network management/Pending transfers": PendingTransfersPage(data); break;
                case "Settings/Network management/Outgoing transfer": OutgoingTransferPage(data); break;
                case "Settings/Account settings/Subscriptions/Redeem a license key": LicensePage(); break;
                case "Settings/Account settings/Subscriptions/Partner benefits": PartnerBenefitsPage(); break;
                case "Home/Security & privacy/Identity protection": IdentityProtectionPage(data); break;
                case "Settings/Connected services/Connected device setup": SetupVendorsPage(data); break;
                case "Settings/Advanced networking/Internet connection/ISP settings": IspSettingsPage(); break;
                case "Theme preview": ThemePreviewPage(); break;
                case "Product gallery": ProductGalleryPage(); break;
                case "Settings/Advanced networking/DNS/DNS performance": DnsPerformancePage(data); break;
                default: FeatureMenu(currentPage); break;
            }
        }
        private static string Human(string key)
        {
            var special = new Dictionary<string, string> { { "ip", "IP address" }, { "mac", "MAC address" }, { "ssid", "Wifi name" }, { "up_mbps", "Upload (Mbps)" }, { "down_mbps", "Download (Mbps)" }, { "ipv6_upstream", "IPv6" }, { "led_on", "Status light" }, { "sqm", "Optimize for conferencing and gaming" } };
            if (special.ContainsKey(key)) return special[key];
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Regex.Replace(key.Replace('_', ' ').Replace('.', ' '), "([a-z])([A-Z])", "$1 $2"));
        }
        private void ObjectCards(object value, string title = null)
        {
            if (value == null) { page.Children.Add(Text("Use Refresh to load this section.", 14, "#64738A")); return; }
            var p = new StackPanel(); if (title != null) p.Children.Add(Text(title, 20, "#17263C", true));
            Details(p, value, 0); page.Children.Add(Card(p));
        }
        private void Details(StackPanel p, object value, int depth)
        {
            if (depth > 4) return;
            var map = value as IDictionary<string, object>;
            if (map != null)
            {
                foreach (var item in map)
                {
                    if (Regex.IsMatch(item.Key, "(^resources$|^url$|token|password|secret|credential|certificate|master_key|private_key|psk|operational_dataset|software_key|invite_code|signed_upload_url|kms_key_id|mwb_full_report_url|^coupon$)", RegexOptions.IgnoreCase)) continue;
                    var a = item.Value as IEnumerable;
                    if (item.Value is IDictionary<string, object> || (a != null && !(item.Value is string)))
                    {
                        p.Children.Add(Text(Human(item.Key), 14, "#64738A", true));
                        var child = new StackPanel { Margin = new Thickness(12, 0, 0, 12) }; Details(child, item.Value, depth + 1); p.Children.Add(child);
                    }
                    else
                    {
                        string display = item.Value is bool ? ((bool)item.Value ? "On" : "Off") : J.S(item.Value);
                        if (display.Length > 0 && !display.StartsWith("/2.")) p.Children.Add(Text(Human(item.Key) + ": " + display, 13));
                    }
                }
                return;
            }
            var list = J.List(value);
            if (list.Count > 0 && list.All(x => x == null || x is string || x is ValueType)) p.Children.Add(Text(String.Join(" · ", list.Take(60).Select(x => J.S(x)).Where(s => !s.StartsWith("/2."))), 13));
            else if (list.Count > 0) foreach (object item in list.Take(60)) { Details(p, item, depth + 1); p.Children.Add(new Border { Height = 8 }); }
            else if (value != null && !(value is IEnumerable)) p.Children.Add(Text(J.S(value), 13));
            else if (value is string) p.Children.Add(Text(J.S(value), 13));
            else p.Children.Add(Text("No entries returned.", 13, "#64738A"));
        }
        private async Task<object> Read(string path)
        { if (preview) throw new InvalidOperationException("Sign in to load this information."); return await api.Send("GET", path); }
        private async Task Change(string method, string path, object body, bool form, string message)
        {
            if (preview) throw new InvalidOperationException("Preview mode is read only. Sign in to manage your network.");
            await api.Send(method, path, body, form);
            try
            {
                if (!String.IsNullOrEmpty(networkUrl)) await LoadNetwork();
                if (currentPage == "Home/Profiles/Profile" && selectedProfile != null) selectedProfile = await api.Send("GET", J.S(selectedProfile, "url"));
                else if (currentPage == "Home/eeros/Details" && selectedNode != null) selectedNode = await api.Send("GET", J.S(selectedNode, "url"));
                else await LoadFeature(currentPage);
                RenderPage(); SetNotice(message);
            }
            catch (Exception e) { SetNotice(message + " Refresh did not finish: " + Message(e), true); }
        }
        private sealed class Field
        {
            public string Key, Label, Type; public object Value; public string[] Choices;
            public Field(string key, string label, object value = null, string type = "text", params string[] choices) { Key = key; Label = label; Value = value; Type = type; Choices = choices; }
        }
        private void Edit(string title, IEnumerable<Field> fields, Func<Dictionary<string, object>, Task> save, string submit = "Save", string description = null)
        {
            var body = new StackPanel(); body.Children.Add(Text(title, 24, "#17263C", true));
            if (description != null) body.Children.Add(Text(description, 13, "#64738A"));
            var readers = new Dictionary<string, Func<object>>(); var secrets = new List<PasswordBox>();
            foreach (Field field in fields)
            {
                Field item = field;
                if (item.Type == "link") { body.Children.Add(LocalButton(item.Label, delegate { Official(J.S(item.Value)); })); continue; }
                if (item.Type == "bool") { var c = new CheckBox { Content = new TextBlock { Text = item.Label, TextWrapping = TextWrapping.Wrap, MaxWidth = 410 }, IsChecked = item.Value is bool && (bool)item.Value }; body.Children.Add(c); readers[item.Key] = () => c.IsChecked == true; continue; }
                body.Children.Add(Text(item.Label, 13, "#17263C", true));
                if (item.Type == "choice")
                {
                    var c = new ComboBox { ItemsSource = item.Choices, SelectedItem = J.S(item.Value) }; if (c.SelectedIndex < 0) c.SelectedIndex = 0; body.Children.Add(c); readers[item.Key] = () => J.S(c.SelectedItem);
                }
                else if (item.Type == "password")
                { var c = new PasswordBox { MaxLength = 512 }; body.Children.Add(c); secrets.Add(c); readers[item.Key] = () => c.Password; }
                else
                {
                    var c = new TextBox { Text = J.S(item.Value), MaxLength = item.Type == "multiline" || item.Type == "long" ? 4096 : 254, AcceptsReturn = item.Type == "multiline", MinHeight = item.Type == "multiline" ? 80 : 0 }; body.Children.Add(c); readers[item.Key] = () => c.Text;
                }
            }
            var status = Text("", 13, "#B5333D"); body.Children.Add(status);
            var actions = new WrapPanel(); body.Children.Add(actions);
            var window = Dialog(title, body); bool working = false;
            var commit = new Button { Content = submit, Background = Brush("#345BEB"), Foreground = Brushes.White }; actions.Children.Add(commit);
            var cancel = new Button { Content = "Cancel", IsCancel = true }; actions.Children.Add(cancel);
            commit.Click += async delegate
            {
                if (working) return;
                working = true; commit.IsEnabled = cancel.IsEnabled = false; status.Text = "Saving…";
                try { var values = readers.ToDictionary(p => p.Key, p => p.Value()); await save(values); working = false; window.Close(); }
                catch (Exception e)
                {
                    status.Text = Message(e);
                    var auth = e as ApiException;
                    if (auth != null && auth.SessionExpired) { working = false; window.Close(); api.Forget(); ClearData(); ShowLogin(false); SetNotice("Your session expired. Sign in again.", true); }
                }
                finally { working = false; commit.IsEnabled = cancel.IsEnabled = true; }
            };
            window.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { if (working) e.Cancel = true; };
            // Commit closes after the request has completed, so allow its own close.
            window.Closed += delegate { foreach (PasswordBox secret in secrets) secret.Clear(); };
            cancel.Click += delegate { if (!working) window.Close(); };
            window.ShowDialog();
        }
        private static string V(IDictionary<string, object> values, string key) { object v; return values.TryGetValue(key, out v) ? J.S(v) : ""; }
        private static bool Flag(IDictionary<string, object> values, string key) { return V(values, key).Equals("True", StringComparison.OrdinalIgnoreCase); }
        private void Info(string title, object data)
        { var p = new StackPanel(); p.Children.Add(Text(title, 23, "#17263C", true)); Details(p, data, 0); var w = Dialog(title, p); w.ShowDialog(); }
        private Button Section(string title, string route) { return LocalButton(title + "  ›", delegate { Navigate(route); }); }
        private void ActionRow(params UIElement[] buttons) { var row = new WrapPanel(); foreach (UIElement b in buttons) row.Children.Add(b); page.Children.Add(row); }
        private void Official(string url)
        {
            if (url == "about:blank" || !ServiceLinks.Allowed(url))
                throw new InvalidOperationException("The service returned a link outside the supported official domains.");
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        private void ShowWifiQr(string name, string password)
        {
            using (var generator = new QRCodeGenerator()) using (var code = generator.CreateQrCode(FeatureContracts.WifiQr(name, password), QRCodeGenerator.ECCLevel.Q)) using (var png = new PngByteQRCode(code))
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = new MemoryStream(png.GetGraphic(8)); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze();
                var p = new StackPanel(); p.Children.Add(Text("Join " + name, 23, "#17263C", true)); p.Children.Add(new Image { Source = bitmap, Width = 320, Height = 320, Margin = new Thickness(0, 12, 0, 12) }); p.Children.Add(Text("This QR code contains your wifi credentials. Anyone who scans it can join the network.", 12, "#64738A")); Dialog("Wifi QR code", p).ShowDialog();
            }
        }
        private object PreviewFeature(string path)
        {
            if (path == "Help/Contact support") return J.Obj("contacts", new[] { J.Obj("type", "CHAT", "title", "Sample support chat", "content", "https://www.eero.com/support"), J.Obj("type", "MAIL", "title", "Sample support email", "content", "support@example.com") });
            if (path.EndsWith("Offers & referrals")) return J.Obj("plans", new[] { J.Obj("id", "sample-product", "name", "Sample eero Plus plan", "currency", "USD", "price", 999) });
            if (path.EndsWith("Guided diagnostics")) return J.Obj("status", "completed", "issue_area", "none", "issues", new object[0]);
            if (path.EndsWith("Fast roaming")) return J.Obj("fast_transition", true);
            if (path.EndsWith("Find backup wifi")) return J.Obj("status", "SUCCESS", "discovered_ssids", new[] { J.Obj("ssid", "Sample phone hotspot", "rssi", -53) });
            if (path.EndsWith("Incoming access invitations")) return J.Obj("promotions", new[] { J.Obj("invite_id", "sample-invite", "network_id", "sample", "network_name", "Sample family network", "invite_role", "admin") });
            if (path.EndsWith("Automatic setup")) return J.Obj("eeros", new[] { J.Obj("serial", "SAMPLE-SERIAL", "model", "Sample eero", "ineligible_for_zts", J.Obj("reason", "Preview example")) });
            if (path.EndsWith("Accessories & cellular")) return new[] { J.Obj("model", "Sample accessory", "status", "connected") };
            if (path.EndsWith("Guest wifi network")) return J.Obj("name", "Maple Guest", "enabled", true);
            if (path.EndsWith("Reservations & port forwarding")) return J.Obj("reservations", J.Obj("data", new[] { J.Obj("description", "Office PC", "ip", "192.168.4.20", "mac", "02:00:00:00:00:01") }), "forwards", J.Obj("data", new object[0]), "pinholes", J.Obj("data", new object[0]));
            if (path.EndsWith("Security & privacy")) return J.Obj("dns_policies", J.Obj("block_malware", true, "ad_block", true));
            if (path.EndsWith("Software updates")) return J.Obj("target_firmware", "Preview", "update_status", "up_to_date", "can_update_now", false);
            if (path.EndsWith("Notifications")) return J.Obj("device.new", true, "network.updated", true);
            if (path.EndsWith("Pending transfers")) return J.Obj("transfers", new[] { J.Obj("id", 17, "organization_name", "Sample provider", "subset_name", "Maple community", "created_at", "2026-10-22") });
            if (path.EndsWith("Outgoing transfer")) return J.Obj("recipient_email_address", "recipient@example.com", "transfer_date", "2026-10-22");
            if (path.EndsWith("Identity protection")) return J.Obj("enabled", false, "status", "Sample benefit status");
            if (path.EndsWith("Connected device setup")) return J.Obj("vendors", new[] { J.Obj("vendor_id", "sample", "name", "Sample connected-device vendor", "enabled", true) });
            if (path.EndsWith("Business guest wifi")) return J.Obj("configurations", new[] { J.Obj("configuration_id", 5, "header", "Welcome to Maple House", "sub_header", "Guest wifi", "color_theme", "Dark", "capture_email", false, "capture_name", false, "access_expiration_in_minutes", 240) });
            if (path == "Activity/Data usage" || path == "Activity/Security activity") return J.Obj("series", new[] { J.Obj("type", "download", "sum", 13400000000L, "values", new[] { J.Obj("time", "2026-10-16", "value", 1200000000L), J.Obj("time", "2026-10-17", "value", 2100000000L), J.Obj("time", "2026-10-18", "value", 1600000000L), J.Obj("time", "2026-10-19", "value", 2800000000L), J.Obj("time", "2026-10-20", "value", 1800000000L), J.Obj("time", "2026-10-21", "value", 2200000000L), J.Obj("time", "2026-10-22", "value", 1700000000L) }) });
            return null;
        }
    }
}
