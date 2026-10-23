using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void AfterEditor(Action action) { Dispatcher.BeginInvoke(action); }
        private string NetworkId { get { return ExtendedContracts.Identifier(networkUrl.TrimEnd('/').Split('/').Last()); } }
        private static bool AccountOnlyPage(string route)
        {
            return route == "Settings/Notifications/Windows notifications" || route == "Help" || route.StartsWith("Help/") || route == "Settings" || route == "Settings/Appearance" || route == "Settings/About" || route.StartsWith("Settings/Account settings") || route == "Settings/Network management" || route == "Settings/Network management/Pending transfers" || route == "Settings/User permissions/Join a network" || route == "Settings/User permissions/Incoming access invitations" || route == "Home/Add or replace eero" || route == "Home/Add or replace eero/Find nearby eeros" || route == "Home/Add or replace eero/Create a network";
        }
        private void JoinNetworkPage()
        {
            Sub("Join a network");
            page.Children.Add(Card(Text("Paste an eero administrator invitation link or code. Review the network and requested role before accepting.", 15)));
            page.Children.Add(LocalButton("Review invitation", delegate
            {
                Edit("Review invitation", new[] { new Field("code", "Invitation link or code", null, "long") }, async v =>
                {
                    string code = ExtendedContracts.InviteCode(V(v, "code")); object data = await ReadInvite(code);
                    AfterEditor(delegate { InvitationReview(code, data); });
                }, "Look up invitation");
            }));
        }
        private Task<object> ReadInvite(string code) { if (preview) throw new InvalidOperationException("Sign in to review an invitation."); return api.Send("POST", "/2.2/inviteQuery", J.Obj("invite_code", code)); }
        private void InvitationReview(string code, object data)
        {
            string id = ExtendedContracts.Identifier(J.S(data, "network_id")); string state = J.S(data, "invite_status");
            var p = new StackPanel(); p.Children.Add(Text("Network invitation", 23, "#17263C", true)); Details(p, data, 0);
            var w = Dialog("Network invitation", p); var status = Text("", 13, "#B5333D"); p.Children.Add(status); var actions = new WrapPanel(); p.Children.Add(actions); bool working = false;
            foreach (bool decision in new[] { true, false })
            {
                bool accept = decision; var button = new Button { Content = accept ? "Accept invitation" : "Decline invitation", IsEnabled = state.Equals("pending", StringComparison.OrdinalIgnoreCase) };
                button.Click += async delegate
                {
                    if (working || !Confirm((accept ? "Accept" : "Decline") + " the invitation to " + J.S(data, "network_name") + "?")) return;
                    working = true; actions.IsEnabled = false;
                    try { await api.Send("POST", "/2.2/networks/" + id + "/invites/response", ExtendedContracts.InviteResponse(code, accept)); working = false; w.Close(); await ReloadMembership(accept ? "Network invitation accepted." : "Network invitation declined."); }
                    catch (Exception e) { status.Text = Message(e); }
                    finally { working = false; actions.IsEnabled = true; }
                }; actions.Children.Add(button);
            }
            if (!state.Equals("pending", StringComparison.OrdinalIgnoreCase)) p.Children.Add(Text("Only pending invitations can be accepted or declined.", 13, "#64738A"));
            w.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = working; }; w.ShowDialog();
        }
        private async Task ReloadMembership(string message)
        {
            selectedProfile = selectedNode = network = null; networkUrl = null; devices.Clear(); nodes.Clear(); profiles.Clear(); featureData.Clear();
            notificationEpoch++; currentPage = "Home";
            try { await LoadAccount(); ShowShell(); SetNotice(message); }
            catch (Exception e) { ShowShell(); SetNotice(message + " Refresh did not finish: " + Message(e), true); }
        }
        private void PendingTransfersPage(object data)
        {
            Sub("Pending network transfers"); var transfers = Entries(data, "transfers");
            if (transfers.Count == 0) page.Children.Add(Card(Text("No pending transfers were returned.")));
            foreach (object raw in transfers)
            {
                object transfer = raw; string id = ExtendedContracts.Identifier(J.S(transfer, "id")); var p = new StackPanel(); Details(p, transfer, 0); var row = new WrapPanel();
                foreach (bool value in new[] { true, false })
                {
                    bool accept = value; row.Children.Add(Button(accept ? "Accept transfer" : "Decline transfer", async delegate
                    {
                        if (preview) throw new InvalidOperationException("Preview mode is read only.");
                        if (!Confirm((accept ? "Accept ownership of" : "Decline") + " this network transfer from " + J.S(transfer, "organization_name") + "?")) return;
                        await api.Send("POST", "/2.2/network_transfers/" + id + (accept ? "/accept" : "/deny"));
                        await ReloadMembership(accept ? "Network transfer accepted." : "Network transfer declined.");
                    }));
                }
                p.Children.Add(row); page.Children.Add(Card(p));
            }
            string next = J.S(data, "next_token"); int token;
            if (Int32.TryParse(next, out token)) page.Children.Add(Button("Next page of transfers", async delegate { featureData[currentPage] = await Read("/2.2/network_transfers/pending?token=" + token); RenderPage(); }));
        }
        private void LicensePage()
        {
            Sub("Redeem a software subscription key");
            if (network == null) { page.Children.Add(Text("Select a network before redeeming a key.")); return; }
            page.Children.Add(Card(Text("Use a software key supplied by eero or your provider. First validate the key, then review the activation options. Automatic renewal starts unchecked.", 15)));
            page.Children.Add(LocalButton("Validate a license key", delegate
            {
                Edit("License key", new[] { new Field("key", "Software key", null, "password") }, async v =>
                {
                    if (preview) throw new InvalidOperationException("Sign in to validate a key."); string key = V(v, "key").Trim();
                    object result = await api.Send("POST", "/2.2/license_keys", ExtendedContracts.License(key));
                    AfterEditor(delegate { LicenseActivation(key, result); });
                }, "Validate key");
            }));
        }
        private void OutgoingTransferPage(object data)
        {
            Sub("Outgoing ownership transfer"); ObjectCards(data);
            if (String.IsNullOrEmpty(J.S(data, "recipient_email_address")) && String.IsNullOrEmpty(J.S(data, "transfer_date"))) { page.Children.Add(Text("No outgoing ownership invitation was returned.", 14, "#64738A")); return; }
            ActionRow(Button("Resend invitation", async delegate { if (!Confirm("Resend the pending ownership invitation to the current recipient?")) return; await Change("POST", NetResource("transfer"), null, false, "Ownership invitation resent."); }), Button("Cancel ownership invitation", async delegate { if (!Confirm("Cancel the outgoing ownership invitation?")) return; await Change("DELETE", NetResource("transfer"), null, false, "Ownership invitation canceled."); }));
        }
        private void LicenseActivation(string key, object result)
        {
            var p = new StackPanel(); p.Children.Add(Text("Review license activation", 23, "#17263C", true)); Details(p, result, 0);
            p.Children.Add(Text("Network: " + J.S(network, "name"), 16, "#17263C", true));
            var renew = new CheckBox { Content = new TextBlock { Text = "Enable automatic renewal under the provider's subscription terms", TextWrapping = TextWrapping.Wrap, MaxWidth = 410 }, IsChecked = false }; p.Children.Add(renew);
            var w = Dialog("Activate license", p); var status = Text("", 13, "#B5333D"); p.Children.Add(status); var activate = new Button { Content = "Activate for this network" }; p.Children.Add(activate); bool working = false;
            activate.Click += async delegate
            {
                if (working || !Confirm("Activate this subscription key for " + J.S(network, "name") + "? Automatic renewal: " + (renew.IsChecked == true ? "enabled" : "disabled") + ".")) return;
                working = true; activate.IsEnabled = false;
                try { await Change("POST", "/2.2/license_keys", ExtendedContracts.License(key, NetworkId, renew.IsChecked == true), false, "Subscription key activated."); working = false; w.Close(); }
                catch (Exception e) { status.Text = Message(e); } finally { working = false; activate.IsEnabled = true; }
            };
            w.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = working; }; w.ShowDialog();
        }
        private void PartnerBenefitsPage()
        {
            Sub("eero Plus partner benefits"); page.Children.Add(Text("Benefits require the corresponding subscription. Redemption codes are shown only when you request them.", 14, "#64738A"));
            ActionRow(Button("1Password status", async delegate { Info("1Password", await Read("/2.2/account/1password_status")); }), Button("1Password redemption code", async delegate { ShowPartnerCode("1Password", await Read("/2.2/partners/onepassword/coupon?test=false")); }), Button("Malwarebytes redemption code", async delegate { ShowPartnerCode("Malwarebytes", await Read("/2.2/partners/malwarebytes/coupon?test=false")); }), Section("Identity protection & digital footprint", "Home/Security & privacy/Identity protection"));
            if (network != null) page.Children.Add(Button("VPN seat availability", async delegate { Info("VPN seats", await Read("/2.2/partners/guardian/networks/" + NetworkId + "/availability")); }));
            page.Children.Add(Text("A native Windows VPN tunnel and the partner applications themselves remain separate integrations. These controls retrieve the eero benefit status and redemption information.", 13, "#64738A"));
        }
        private void ShowPartnerCode(string title, object result)
        {
            string code = J.S(result, "coupon"); if (code.Length == 0) throw new InvalidOperationException("No redemption code was returned for this account.");
            var p = new StackPanel(); p.Children.Add(Text(title + " redemption code", 23, "#17263C", true)); var input = new TextBox { Text = code, IsReadOnly = true }; p.Children.Add(input);
            var copy = new Button { Content = "Copy redemption code" }; copy.Click += delegate { Clipboard.SetText(code); }; p.Children.Add(copy); var w = Dialog(title, p); w.Closed += delegate { input.Clear(); }; w.ShowDialog();
        }
        private void IdentityProtectionPage(object data)
        {
            Sub("Identity protection & digital footprint"); ObjectCards(data, "Protection status");
            ActionRow(Button("Supported countries", async delegate { Info("Supported countries", await Read(FeatureUrl("identity_protection/countries"))); }), LocalButton("Activate identity protection", delegate
            {
                Edit("Activate identity protection", new[] { new Field("country", "Two-letter country code", "US") }, async v =>
                {
                    string country = V(v, "country").Trim().ToUpperInvariant(); if (!Regex.IsMatch(country, "^[A-Z]{2}$")) throw new ArgumentException("Enter a two-letter country code.");
                    if (!Confirm("Activate the identity-protection benefit for this account and country?")) return;
                    await Change("POST", "/2.2/account/identity_protection/signup", J.Obj("country_code", country), false, "Identity protection activation requested.");
                }, "Activate");
            }), LocalButton("Scan an email address", delegate
            {
                Edit("Digital footprint scan", new[] { new Field("email", "Email address to scan") }, async v =>
                {
                    string email = ExtendedContracts.Email(V(v, "email")); if (preview) throw new InvalidOperationException("Sign in to run a scan.");
                    if (!Confirm("Send " + email + " to eero's identity-protection service for a digital-footprint scan?")) return;
                    object result = await api.Send("POST", "/2.2/account/identity_protection/scan", J.Obj("email", email)); featureData["identity_scan"] = result;
                    AfterEditor(delegate { IdentityScanResult(result); });
                }, "Run scan");
            }), LocalButton("Send email verification", delegate
            {
                Edit("Email verification", new[] { new Field("email", "Email address") }, async v => { if (preview) throw new InvalidOperationException("Sign in to request verification."); await api.Send("POST", "/2.2/account/identity_protection/scan/email", J.Obj("email", ExtendedContracts.Email(V(v, "email")))); SetNotice("Identity-protection verification email requested."); }, "Send verification email");
            }), LocalButton("Verify scan email", delegate
            {
                Edit("Verify digital-footprint email", new[] { new Field("code", "Verification code") }, async v =>
                {
                    string code = V(v, "code").Trim(); if (!Regex.IsMatch(code, "^[A-Za-z0-9-]{4,32}$")) throw new ArgumentException("Enter the code from the verification email."); if (preview) throw new InvalidOperationException("Sign in to verify an email.");
                    object result = await api.Send("POST", "/2.2/account/identity_protection/verify_email", J.Obj("code", code)); featureData["identity_scan"] = result; AfterEditor(delegate { IdentityScanResult(result); });
                }, "Verify");
            }));
            object previous; if (featureData.TryGetValue("identity_scan", out previous)) page.Children.Add(LocalButton("Latest scan result", delegate { IdentityScanResult(previous); }));
        }
        private void IdentityScanResult(object result)
        {
            var p = new StackPanel(); p.Children.Add(Text("Digital footprint", 23, "#17263C", true)); Details(p, result, 0);
            string report = J.S(result, "mwb_full_report_url"); if (report.Length > 0) { var b = new Button { Content = "Open detailed report at Malwarebytes" }; b.Click += delegate { OpenPartnerReport(report); }; p.Children.Add(b); }
            if (J.B(result, "email_unverified")) p.Children.Add(Text("Use Send email verification, then Verify scan email to unlock verified results.", 14, "#64738A")); Dialog("Digital footprint", p).ShowDialog();
        }
        private void OpenPartnerReport(string url)
        {
            Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != "https" || u.Port != 443 || u.UserInfo.Length > 0 || !(u.Host == "malwarebytes.com" || u.Host.EndsWith(".malwarebytes.com", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("The service did not return a supported secure Malwarebytes report link.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        private void SetupVendorsPage(object data)
        {
            Sub("Connected device setup"); var vendors = Entries(data, "vendors", "configurations");
            if (vendors.Count == 0) ObjectCards(data);
            foreach (object raw in vendors)
            {
                object vendor = raw; string id = J.S(vendor, "vendor_id"); var p = new StackPanel(); Details(p, vendor, 0);
                if (id.Length > 0) p.Children.Add(LocalButton("Edit setup permission", delegate { Edit("Connected setup", new[] { new Field("enabled", "Allow setup for this vendor", J.B(vendor, "enabled"), "bool") }, async v => { if (!Confirm("Change connected-device setup for this vendor?")) return; await Change("PUT", FeatureUrl("simple_setup/vendors/" + Uri.EscapeDataString(id)), J.Obj("enabled", Flag(v, "enabled")), true, "Setup permission updated."); }); })); page.Children.Add(Card(p));
            }
        }
        private void IspSettingsPage()
        {
            Sub("PPPoE & ISP VLAN settings"); ObjectCards(J.Obj("pppoe_enabled", J.Get(network, "pppoe_enabled"), "pppoe_username", J.Get(network, "pppoe_username"), "vlan", J.Get(network, "vlan")));
            page.Children.Add(LocalButton("Edit ISP settings", delegate
            {
                Edit("ISP settings", new[] { new Field("enabled", "Use PPPoE", J.B(network, "pppoe_enabled"), "bool"), new Field("replace", "Replace the saved PPPoE credentials", false, "bool"), new Field("username", "PPPoE username", J.S(network, "pppoe_username")), new Field("password", "New PPPoE password", null, "password"), new Field("vlan", "ISP VLAN ID (blank disables VLAN tagging)", J.S(network, "vlan")) }, async v =>
                {
                    bool enabled = Flag(v, "enabled"), replace = Flag(v, "replace"); if (enabled && !J.B(network, "pppoe_enabled") && !replace) throw new ArgumentException("Enter new credentials when enabling PPPoE for the first time.");
                    object body = ExtendedContracts.Pppoe(enabled, V(v, "username"), V(v, "password"), replace, V(v, "vlan")); if (!Confirm("Apply ISP settings? Incorrect PPPoE credentials or VLAN settings can disconnect the network.")) return;
                    await Change("PUT", NetResource("settings"), body, false, "ISP settings updated.");
                }, "Apply ISP settings", "Use the settings supplied by your ISP. This changes an existing network; physical setup of a new gateway still needs provisioning.");
            }));
        }
        private async Task UploadPortalAsset(object config, bool logo)
        {
            if (preview) throw new InvalidOperationException("Sign in to upload a portal asset.");
            string configId = ExtendedContracts.Identifier(J.First(config, "configuration_id", "id"));
            var picker = new Microsoft.Win32.OpenFileDialog { Title = logo ? "Select guest portal logo" : "Select guest terms PDF", Filter = logo ? "PNG or JPEG image|*.png;*.jpg;*.jpeg" : "PDF document|*.pdf", Multiselect = false };
            if (picker.ShowDialog(this) != true) return;
            long size = new FileInfo(picker.FileName).Length; if (size > (logo ? 5 : 10) * 1024 * 1024) throw new ArgumentException("This file is too large. Logos support up to 5 MB; PDFs up to 10 MB.");
            byte[] bytes = File.ReadAllBytes(picker.FileName); string mime = ExtendedContracts.PortalMime(picker.FileName, bytes, logo);
            if (!Confirm("Upload " + Path.GetFileName(picker.FileName) + " (" + ((bytes.Length + 1023) / 1024) + " KB) as the guest portal " + (logo ? "logo" : "terms document") + "? Guests will be able to access the uploaded asset.")) return;
            string type = logo ? "CaptivePortalLogo" : "CaptivePortalToS", ext = Path.GetExtension(picker.FileName).TrimStart('.').ToLowerInvariant();
            object upload = await api.Send("GET", FeatureUrl("file/import/upload_link?importJobType=" + type + "&fileExtension=" + ext));
            await PortalUpload.Send(upload, bytes, mime);
            await Change("POST", FeatureUrl("file/import/job"), ExtendedContracts.ImportJob(J.S(upload, "import_file_path"), logo, configId), false, "Asset uploaded and import requested. Refresh the portal after processing finishes.");
        }
        private void DnsPerformancePage(object result)
        {
            Sub("Measure DNS resolver performance from the eero gateway. A test does not change your DNS configuration.");
            page.Children.Add(LocalButton("Start DNS performance test", delegate
            {
                string existing = String.Join(Environment.NewLine, J.List(J.Get(network, "dns", "custom", "ips")).Select(s => J.S(s)));
                Edit("DNS performance test", new[] { new Field("servers", "DNS server IP addresses (one per line)", existing, "multiline"), new Field("port", "DNS server port", "53") }, async v =>
                {
                    int port; if (!Int32.TryParse(V(v, "port"), out port)) throw new ArgumentException("Enter a numeric DNS port."); object body = ExtendedContracts.DnsPerformance(V(v, "servers"), port);
                    if (preview) throw new InvalidOperationException("Sign in to start a gateway test."); object kickoff = await api.Send("POST", FeatureUrl("dns_perf_test"), body);
                    featureData["dns_perf_action"] = ExtendedContracts.Identifier(J.S(kickoff, "action_uuid")); await LoadFeature(currentPage); RenderPage(); SetNotice("DNS test requested. Use Refresh results until it finishes.");
                }, "Start test");
            }));
            if (result != null) ObjectCards(result, "Resolver test results"); else page.Children.Add(Card(Text("No resolver test has been started during this session.")));
            if (featureData.ContainsKey("dns_perf_action")) page.Children.Add(Button("Refresh results", async delegate { await LoadFeature(currentPage); RenderPage(); }));
        }
    }
    public static class PortalUpload
    {
        public static async Task Send(object upload, byte[] data, string mime, HttpMessageHandler handler = null)
        {
            Uri url = ExtendedContracts.UploadUrl(J.S(upload, "signed_upload_url")); string key = J.S(upload, "kms_key_id");
            if (key.Length == 0 || key.Length > 2048 || key.Any(Char.IsControl)) throw new InvalidDataException("The upload encryption key is missing or invalid.");
            // A separate client never forwards the eero session cookie to storage.
            using (var http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }))
            using (var request = new HttpRequestMessage(HttpMethod.Put, url))
            {
                http.Timeout = TimeSpan.FromSeconds(120); request.Headers.Add("x-amz-server-side-encryption", "aws:kms"); request.Headers.Add("x-amz-server-side-encryption-aws-kms-key-id", key);
                request.Content = new ByteArrayContent(data); request.Content.Headers.ContentType = new MediaTypeHeaderValue(mime);
                using (var response = await http.SendAsync(request)) if (!response.IsSuccessStatusCode) throw new InvalidOperationException("The storage service rejected the upload (" + (int)response.StatusCode + "). No import was requested.");
            }
        }
    }
}
