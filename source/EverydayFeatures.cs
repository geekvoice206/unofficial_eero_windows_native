using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EeroDesktop
{
    public static class EverydayContracts
    {
        public static object BackupOrder(IEnumerable<string> ids)
        {
            string[] all = ids.Select(ExtendedContracts.Identifier).ToArray();
            if (all.Length == 0 || all.Distinct().Count() != all.Length) throw new ArgumentException("Each saved backup network must appear exactly once.");
            return J.Obj("rearranged_ids", all);
        }
        public static object Symptom(string value)
        {
            if (!new[] { "connection_drops", "eeros_red", "internet_offline", "internet_slow" }.Contains(value)) throw new ArgumentException("Choose a supported connection problem.");
            return J.Obj("symptom", value);
        }
        public static string ProductId(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(c => Char.IsControl(c))) throw new ArgumentException("eero did not return a valid product identifier.");
            return value;
        }
        public static object Offers(IEnumerable<string> products)
        {
            string[] all = products.Select(ProductId).Distinct().ToArray();
            if (all.Length == 0 || all.Length > 50) throw new ArgumentException("Select between one and fifty available products.");
            return J.Obj("product_ids", all);
        }
        public static object Referral(string networkId, string productId) { return J.Obj("product_id", ProductId(productId), "network_id", ExtendedContracts.Identifier(networkId)); }
        public static Dictionary<string, string> GuardianLogin(object data)
        {
            string id = J.S(data, "guardian_id"), password = J.S(data, "guardian_secret");
            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(password) || id.Length > 4096 || password.Length > 8192) throw new InvalidOperationException("eero did not return complete Guardian sign-in details for this account.");
            return new Dictionary<string, string> { { "Guardian username", id }, { "Guardian password", password } };
        }
        public static Dictionary<string, string> ThreadCredentials(object data)
        {
            var values = new Dictionary<string, string>();
            foreach (string key in new[] { "active_operational_dataset", "commissioning_credential", "master_key" })
            {
                string value = J.S(data, key); if (value.Length > 0 && value.Length <= 8192) values[key.Replace('_', ' ')] = value;
            }
            if (values.Count == 0) throw new InvalidOperationException("This network did not return Thread credentials for your account.");
            return values;
        }
    }

    public sealed partial class MainWindow
    {
        private void ShowCredentials(string title, Dictionary<string, string> values)
        {
            var p = new StackPanel(); p.Children.Add(Text(title, 24, "#17263C", true));
            p.Children.Add(Text("Reveal or copy only when you need these details. Copied values remain in the Windows clipboard.", 13, "#64738A"));
            var fields = new List<TextBox>(); var secrets = new List<PasswordBox>();
            foreach (var entry in values)
            {
                string secret = entry.Value; p.Children.Add(Text(entry.Key, 14, "#17263C", true));
                var masked = new PasswordBox { Password = secret, IsHitTestVisible = false, Focusable = false }; secrets.Add(masked); p.Children.Add(masked);
                var shown = new TextBox { IsReadOnly = true, Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap }; fields.Add(shown); p.Children.Add(shown);
                var actions = new WrapPanel(); var reveal = new Button { Content = "Reveal" }; actions.Children.Add(reveal);
                reveal.Click += delegate { bool show = shown.Visibility != Visibility.Visible; shown.Text = show ? secret : ""; shown.Visibility = show ? Visibility.Visible : Visibility.Collapsed; masked.Visibility = show ? Visibility.Collapsed : Visibility.Visible; reveal.Content = show ? "Hide" : "Reveal"; };
                actions.Children.Add(LocalButton("Copy", delegate { Clipboard.SetText(secret); })); p.Children.Add(actions);
            }
            var w = Dialog(title, p); w.Closed += delegate { foreach (var field in fields) field.Clear(); foreach (var secret in secrets) secret.Clear(); values.Clear(); }; w.ShowDialog();
        }
        private async Task GuardianCredentials()
        {
            if (preview) { ShowCredentials("Sample Guardian sign-in", new Dictionary<string, string> { { "Guardian username", "Sample account" }, { "Guardian password", "sample-only-password" } }); return; }
            if (!Confirm("Retrieve your Guardian sign-in details from eero? Use them only in Guardian's official Windows app or website.")) return;
            object data = await api.Send("POST", "/2.2/partners/guardian");
            ShowCredentials("Guardian sign-in", EverydayContracts.GuardianLogin(data));
        }
        private void SubscriptionOffersPage(object data)
        {
            Sub("Offers & referrals");
            if (network == null) { page.Children.Add(Card(Text("Select a network to load its eligible plans and referrals."))); return; }
            page.Children.Add(Text("Plans returned for this network. Offers and referrals depend on eligibility and purchase channel. Review final prices and terms in eero's payment service.", 14, "#64738A"));
            var plans = Entries(data, "plans"); if (plans.Count == 0) page.Children.Add(Card(Text("No plans were returned for this network.")));
            foreach (object raw in plans)
            {
                object plan = raw; string id = J.S(plan, "id"); if (id.Length == 0) continue;
                var p = new StackPanel(); p.Children.Add(Text(J.S(plan, "name").NullIfEmpty() ?? "eero plan", 20, "#17263C", true));
                if (J.S(plan, "interval").Length > 0) p.Children.Add(Text("Billing interval: " + J.S(plan, "interval"), 14, "#64738A"));
                p.Children.Add(Button("Check offers", async delegate
                {
                    if (preview) throw new InvalidOperationException("Sign in to retrieve current offers.");
                    object offers = await api.Send("POST", "/2.2/premium/in_app_payments/offers/" + NetworkId, EverydayContracts.Offers(new[] { id }));
                    Info("Available offers", J.Obj("offers", Entries(offers, "offers").Select(o => J.Obj("title", J.S(o, "title"), "ends_at", J.S(o, "ends_at"))).ToArray()));
                }));
                p.Children.Add(Button("Create referral link", async delegate
                {
                    if (preview) throw new InvalidOperationException("Sign in to create a referral link.");
                    object referral = await api.Send("PUT", "/2.2/subscription_referrals", EverydayContracts.Referral(NetworkId, id));
                    ShowReferralLink(J.S(referral, "referral_url"));
                }));
                p.Children.Add(Button("Email this plan to me", async delegate
                {
                    if (preview) throw new InvalidOperationException("Sign in to email a referral.");
                    if (!Confirm("Ask eero to email this subscription referral to your account email address?")) return;
                    await api.Send("POST", "/2.2/subscription_referral/email", EverydayContracts.Referral(NetworkId, id)); SetNotice("Referral email requested from eero.");
                })); page.Children.Add(Card(p));
            }
            ActionRow(Button("Referral status", async delegate { Info("Subscription referrals", await Read(FeatureUrl("managed/subscription_referrals"))); }), Button("Manage purchases", async delegate { await OpenPaymentService("MANAGE_PURCHASES"); }));
        }
        private void ShowReferralLink(string value)
        {
            string url = ServiceLinks.Eero(value).AbsoluteUri;
            var p = new StackPanel(); p.Children.Add(Text("Subscription referral", 24, "#17263C", true));
            p.Children.Add(new TextBox { Text = url, IsReadOnly = true, TextWrapping = TextWrapping.Wrap });
            p.Children.Add(LocalButton("Copy link", delegate { Clipboard.SetText(url); })); p.Children.Add(LocalButton("Open referral", delegate { OpenServiceWindow("Subscription referral", url); })); Dialog("Subscription referral", p).ShowDialog();
        }
        private void GuidedDiagnosticsPage(object data)
        {
            Sub("Diagnose a connection problem");
            page.Children.Add(Text("Choose the symptom to run eero's network health checks, then refresh the results. Review the findings before changing network settings.", 14, "#64738A"));
            string[] labels = { "My internet is slow", "My internet is offline", "My connection drops", "My eeros are red" };
            string[] symptoms = { "internet_slow", "internet_offline", "connection_drops", "eeros_red" };
            for (int i = 0; i < labels.Length; i++)
            {
                string symptom = symptoms[i]; page.Children.Add(Button(labels[i], async delegate
                {
                    if (preview) throw new InvalidOperationException("Sign in to run network health checks.");
                    await api.Send("POST", NetResource("diagnostics"), EverydayContracts.Symptom(symptom));
                    SetNotice("Health check requested. Refresh results to see progress; the request will not be automatically repeated.");
                }));
            }
            page.Children.Add(Button("Refresh results", async delegate { featureData[currentPage] = await Read(NetResource("diagnostics")); RenderPage(); }));
            if (data != null) ObjectCards(data, "Health check results");
        }
        private void DeviceConnectionPage()
        {
            Sub("Connect a device");
            page.Children.Add(Card(Text("Some devices need a 2.4 GHz connection during setup. Temporarily hide the 5 GHz network while you connect the device. eero controls when the temporary setting expires; you can also restore it here.", 15)));
            object flag = J.Get(network, "temporary_flags", "hide_5g"); if (flag != null) ObjectCards(flag, "Temporary 5 GHz setting");
            ActionRow(Button("Temporarily hide 5 GHz", async delegate { if (!Confirm("Temporarily hide 5 GHz on this network? Wireless clients may reconnect while you set up the device.")) return; await Change("PUT", FeatureUrl("temporary_flags/hide_5g"), J.Obj("value", true), true, "Temporary 5 GHz pause requested."); }), Button("Restore 5 GHz", async delegate { await Change("DELETE", FeatureUrl("temporary_flags/hide_5g"), null, false, "Temporary 5 GHz pause removed."); }));
            page.Children.Add(Section("Legacy compatibility & wireless settings", "Settings/Advanced networking/Wireless settings"));
        }
        private void FastRoamingPage(object data)
        {
            Sub("Fast roaming"); ObjectCards(data);
            if (!(J.Get(data, "fast_transition") is bool)) { page.Children.Add(Text("This network did not return a fast roaming setting.", 14, "#64738A")); return; }
            page.Children.Add(LocalButton("Configure fast roaming", delegate { Edit("Fast roaming", new[] { new Field("enabled", "Enable fast transition", J.B(data, "fast_transition"), "bool") }, async v =>
            {
                if (!Confirm("Change fast roaming? Some wireless devices may reconnect or need compatibility settings.")) return;
                await Change("PUT", FeatureUrl("fast_transition"), J.Obj("fast_transition", Flag(v, "enabled")), false, "Fast roaming preference updated.");
            }); }));
        }
        private void BackupDiscoveryPage(object data)
        {
            Sub("Find backup wifi networks");
            ActionRow(Button("Scan for backup wifi", async delegate { if (preview) throw new InvalidOperationException("Sign in to scan for backup networks."); await api.Send("POST", FeatureUrl("backup_access_points/ssid_discovery")); SetNotice("Discovery requested. Refresh results shortly."); }), Button("Refresh results", async delegate { featureData[currentPage] = await Read(FeatureUrl("backup_access_points/ssid_discovery")); RenderPage(); }));
            if (data != null) page.Children.Add(Text("Discovery status: " + J.S(data, "status"), 16, "#17263C", true));
            foreach (object found in Entries(J.Get(data, "discovered_ssids")))
            {
                string ssid = J.S(found, "ssid"); var p = new StackPanel(); p.Children.Add(Text(ssid, 19, "#17263C", true)); p.Children.Add(Text("Signal: " + J.S(found, "rssi") + " dBm", 13, "#64738A"));
                p.Children.Add(LocalButton("Use as backup", delegate { BackupEdit(null, ssid); })); page.Children.Add(Card(p));
            }
        }
        private void BackupPriority(List<object> networks)
        {
            if (networks.Count < 2) throw new InvalidOperationException("Save at least two backup wifi networks to choose their priority.");
            var p = new StackPanel(); p.Children.Add(Text("Backup wifi priority", 24, "#17263C", true)); p.Children.Add(Text("Move the preferred backup to the top. All saved backups stay in the order.", 14, "#64738A"));
            var ordered = networks.ToList(); var list = new DataGrid { Height = 240, ItemsSource = ordered, SelectedIndex = 0, AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, CanUserSortColumns = false };
            list.Columns.Add(new DataGridTextColumn { Header = "Backup wifi (highest priority first)", Binding = new System.Windows.Data.Binding("[ssid]"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); p.Children.Add(list);
            Action<int> move = offset => { int from = list.SelectedIndex, to = from + offset; if (from < 0 || to < 0 || to >= ordered.Count) return; object item = ordered[from]; ordered.RemoveAt(from); ordered.Insert(to, item); list.ItemsSource = null; list.ItemsSource = ordered; list.SelectedIndex = to; };
            var row = new WrapPanel(); row.Children.Add(LocalButton("Move up", delegate { move(-1); })); row.Children.Add(LocalButton("Move down", delegate { move(1); })); p.Children.Add(row);
            var status = Text("", 13, "#B5333D"); p.Children.Add(status); var w = Dialog("Backup priority", p); bool working = false; var save = new Button { Content = "Save priority" }; p.Children.Add(save);
            save.Click += async delegate { if (working) return; working = true; save.IsEnabled = false; try { if (preview) throw new InvalidOperationException("Preview mode is read only."); await Change("POST", FeatureUrl("backup_access_points/rearrange"), EverydayContracts.BackupOrder(ordered.Select(n => J.S(n, "uuid"))), false, "Backup priority saved."); working = false; w.Close(); } catch (Exception e) { status.Text = Message(e); } finally { working = false; save.IsEnabled = true; } };
            w.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (working) e.Cancel = true; }; w.ShowDialog();
        }
        private void AmazonHomePage()
        {
            Sub("Amazon connected home"); bool linked = J.B(network, "amazon_account_linked");
            ObjectCards(J.Obj("amazon_account_linked", J.Get(network, "amazon_account_linked"), "alexa_skill", J.Get(network, "alexa_skill")));
            if (linked)
            {
                page.Children.Add(Button("Find Zigbee devices", async delegate { if (!Confirm("Start discovering nearby Zigbee devices on a compatible eero hub? Put the device you want to add in pairing mode first.")) return; await Change("POST", "/2.3/networks/" + NetworkId + "/shh/start_zigbee_discovery", null, false, "Zigbee discovery requested. Check your Amazon smart-home app for discovered devices."); }));
                page.Children.Add(Button("Unlink Amazon account", async delegate { if (!Confirm("Unlink the Amazon account from this network? Connected-home integrations may stop working.")) return; await Change("POST", "/2.2/amazon/networks/" + NetworkId + "/account_unlink", null, false, "Amazon account unlinked."); }));
            }
            page.Children.Add(Section("Connected device setup permissions", "Settings/Connected services/Connected device setup"));
            page.Children.Add(Text("New Amazon account links cannot be created here. Discovery requires a linked Amazon account, a supported hub, and an eligible Zigbee device.", 13, "#64738A"));
        }
    }
}
