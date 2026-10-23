using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace EeroDesktop
{
    public static class RuntimeContracts
    {
        public static string Serial(string value)
        {
            string serial = (value ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(serial, "^[A-Za-z0-9-]{8,40}$")) throw new ArgumentException("Enter the serial number printed on the eero or accessory.");
            return serial;
        }
        public static object AdminInvite() { return J.Obj("invite_role", "admin"); }
        public static object InvitationResponse(string inviteId, bool accept) { return J.Obj("invite_id", ExtendedContracts.Identifier(inviteId), "accept", accept); }
        public static object AutoSetup(string mode)
        {
            if (!new[] { "OFF", "ZTS", "LTS", "DEFAULT" }.Contains(mode)) throw new ArgumentException("Choose a supported automatic setup mode.");
            return J.Obj("mode", mode);
        }
        public static object CreateNetwork(string name, string password, string timezone, IDictionary<string, object> consents)
        {
            MainWindow.ValidateSsid(name); MainWindow.ValidatePassword(password);
            if (!System.Text.RegularExpressions.Regex.IsMatch(timezone ?? "", "^[A-Za-z_+-]+(?:/[A-Za-z_0-9+-]+){0,3}$")) throw new ArgumentException("Enter an IANA timezone such as America/Chicago or Europe/London.");
            return J.Obj("name", name.Trim(), "password", password, "timezone", J.Obj("value", timezone), "settings", J.Obj("consents", consents, "enabled_simple_setup_vendors", new int[0]));
        }
    }

    public sealed partial class MainWindow
    {
        private void IncomingAccessPage(object data)
        {
            Sub("Incoming access invitations"); var invites = Entries(data, "promotions");
            if (invites.Count == 0) page.Children.Add(Card(Text("No pending access invitations.")));
            foreach (object raw in invites)
            {
                object invite = raw; string id = J.S(invite, "invite_id"), net = J.S(invite, "network_id");
                var p = new StackPanel(); p.Children.Add(Text(J.S(invite, "network_name"), 20, "#17263C", true)); p.Children.Add(Text("Requested access: " + J.S(invite, "invite_role"), 14, "#64738A"));
                if (id.Length > 0 && net.Length > 0)
                    foreach (bool decision in new[] { true, false })
                    {
                        bool accept = decision;
                        p.Children.Add(Button(accept ? "Accept invitation" : "Decline invitation", async delegate
                        {
                            if (!Confirm((accept ? "Accept" : "Decline") + " this access invitation for " + J.S(invite, "network_name") + "?")) return;
                            await Change("POST", "/2.2/networks/" + ExtendedContracts.Identifier(net) + "/invites/response", RuntimeContracts.InvitationResponse(id, accept), false, accept ? "Access invitation accepted." : "Access invitation declined.");
                        }));
                    }
                page.Children.Add(Card(p));
            }
        }
        private void AutomaticSetupPage(object data)
        {
            Sub("Automatic eero setup"); string current = J.S(network, "auto_setup_mode");
            page.Children.Add(Card(Text("Current mode: " + (current.NullIfEmpty() ?? "Not reported"))));
            page.Children.Add(LocalButton("Automatic setup preference", delegate
            {
                Edit("Automatic eero setup", new[] { new Field("mode", "Mode", new[] { "OFF", "ZTS", "LTS", "DEFAULT" }.Contains(current) ? current : "DEFAULT", "choice", "OFF", "ZTS", "LTS", "DEFAULT") }, async v =>
                {
                    if (!Confirm("Change automatic setup for this network? OFF disables automatic setup; ZTS and LTS use eero's automatic setup policies; DEFAULT follows the provider default.")) return;
                    await Change("PUT", FeatureUrl("auto_setup_mode"), RuntimeContracts.AutoSetup(V(v, "mode")), false, "Automatic setup preference updated.");
                });
            }));
            page.Children.Add(Text("Discovered eeros", 20, "#17263C", true)); var found = Entries(data, "eeros");
            if (found.Count == 0) page.Children.Add(Text("No cloud-discovered eeros were returned. Bluetooth discovery is also available under Add or replace eero.", 14, "#64738A"));
            foreach (object raw in found)
            {
                object eero = raw; var p = new StackPanel(); Details(p, eero, 0); string serial = J.S(eero, "serial");
                if (serial.Length > 0) p.Children.Add(LocalButton("Add this eero", delegate { AddDiscoveredEero(serial); }));
                page.Children.Add(Card(p));
            }
        }
        private void AddDiscoveredEero(string serial)
        {
            Edit("Add discovered eero", new[] { new Field("serial", "Serial number", serial), new Field("location", "Location") }, async v =>
            {
                string validated = RuntimeContracts.Serial(V(v, "serial")); ValidateName(V(v, "location"));
                if (!Confirm("Add eero " + validated + " to the selected network?")) return;
                await Change("POST", "/2.2/eeros?requireSession=true", J.Obj("serial", validated, "location", V(v, "location").Trim(), "network", networkUrl), true, "eero addition requested.");
            }, "Add eero");
        }
        private void AccessoriesPage(object status)
        {
            Sub("Accessories & cellular");
            ObjectCards(status, "Accessory setup status");
            object cellular = J.Get(network, "cellular_backup"); if (cellular != null) ObjectCards(cellular, "Cellular backup");
            var accessories = J.List(J.Get(network, "accessories"));
            if (accessories.Count == 0) page.Children.Add(Text("No registered accessories were reported for this network.", 14, "#64738A"));
            foreach (object raw in accessories)
            {
                object accessory = raw; var p = new StackPanel(); var photo = HardwareImage(accessory, 72); photo.HorizontalAlignment = System.Windows.HorizontalAlignment.Left; p.Children.Add(photo); p.Children.Add(Text(J.First(accessory, "name", "model"), 20, "#17263C", true)); Details(p, accessory, 0);
                string serial = J.First(accessory, "dsn", "serial");
                if (serial.Length > 0)
                {
                    p.Children.Add(Button("Refresh accessory details", async delegate { Info("Accessory", await Read(FeatureUrl("accessories/" + Uri.EscapeDataString(RuntimeContracts.Serial(serial))))); }));
                    p.Children.Add(LocalButton("Remove accessory", delegate { Edit("Remove accessory", new[] { new Field("serial", "Type the accessory serial number to confirm") }, async v =>
                    {
                        if (V(v, "serial").Trim() != serial) throw new ArgumentException("The accessory serial number does not match.");
                        if (!Confirm("Remove this accessory? Its cellular backup or phone service may stop working.")) return;
                        await Change("DELETE", FeatureUrl("accessories/" + Uri.EscapeDataString(RuntimeContracts.Serial(serial))), null, false, "Accessory removed.");
                    }, "Remove accessory"); }));
                }
                string eeroId = J.S(accessory, "eero_id"); if (eeroId.Length > 0) p.Children.Add(Button("Phone line associations", async delegate { Info("Phone lines", await Read(FeatureUrl("sip_config_associations/" + ExtendedContracts.Identifier(eeroId)))); }));
                page.Children.Add(Card(p));
            }
            page.Children.Add(Text("New cellular service activation and physical accessory pairing require the provider's setup flow. Existing registration, connection and phone-line details are shown when your network supports them.", 13, "#64738A"));
        }
        private void PlacementPage()
        {
            Sub("Placement tests"); page.Children.Add(Text("Run a cloud placement test for a configured eero, then retrieve its signal and placement rating. Results can take a little time to appear.", 14, "#64738A"));
            foreach (object raw in nodes)
            {
                object node = raw; string serial = J.S(node, "serial"); if (serial.Length == 0) continue;
                var p = new StackPanel(); p.Children.Add(Text(J.First(node, "location", "model"), 20, "#17263C", true));
                string url = "/2.3/networks/" + NetworkId + "/placement_test/" + Uri.EscapeDataString(RuntimeContracts.Serial(serial));
                p.Children.Add(Button("Run placement test", async delegate
                {
                    if (!Confirm("Run a placement test for " + J.First(node, "location", "model") + "?")) return;
                    await Change("POST", url, null, false, "Placement test requested. Use View results after the test finishes.");
                }));
                p.Children.Add(Button("View results", async delegate { Info("Placement results", await Read(url)); })); page.Children.Add(Card(p));
            }
        }
        private void VpnPage()
        {
            Sub("VPN powered by Guardian");
            object availability; featureData.TryGetValue("guardian_availability", out availability);
            page.Children.Add(Card(Text(VpnContracts.EligibilityMessage(availability), 15)));
            page.Children.Add(Text("Guardian must be included with your eero service. Some internet providers do not offer this benefit. Check eligibility before installing or registering a device.", 13, "#64738A"));
            page.Children.Add(Button("Check VPN eligibility", async delegate { object result = await LoadGuardianAvailability(); RenderPage(); SetNotice(VpnContracts.EligibilityMessage(result), result is ApiException); }));
            VpnControls();
            page.Children.Add(Button("View Guardian sign-in details", async delegate { await GuardianCredentials(); }));
            ActionRow(LocalButton("Activate or manage Guardian", delegate { OpenServiceWindow("Guardian VPN", "https://guardianapp.com/login?connect-api-env=wifi-company-prod"); }), LocalButton("Guardian Windows download", delegate { Official("https://guardianapp.com/windows/latest/"); }), LocalButton("Windows VPN setup instructions", delegate { OpenServiceWindow("Guardian setup", "https://eero.com/support/articles/how-do-i-set-up-vpn-powered-by-guardian"); }));
            page.Children.Add(Text("Guardian's own Windows app is also available through the download above.", 13, "#64738A"));
        }
        private void SubscriptionWebPage()
        {
            Sub("Manage subscriptions & purchases");
            page.Children.Add(Card(Text("Open eero's subscription service with a temporary account sign-in. Review prices, terms and payment details in the service window before submitting anything.", 15)));
            ActionRow(Button("Manage subscription", async delegate { await OpenPaymentService("MANAGE_SUBSCRIPTION"); }), Button("Manage purchases", async delegate { await OpenPaymentService("MANAGE_PURCHASES"); }), Button("Available add-ons", async delegate { await OpenPaymentService("MOBILE_ADDON_PAYMENT"); }));
            page.Children.Add(LocalButton("Manage a Google Play subscription", delegate
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://play.google.com/store/account/subscriptions") { UseShellExecute = true });
            }));
            page.Children.Add(Text("For a subscription purchased through Google Play, sign in with the Google account that made the purchase. Google Play management opens in your browser.", 13, "#64738A"));
        }
        private void BusinessInsightsPage()
        {
            Sub("eero Insight"); page.Children.Add(Text("Open the official network management portal. Access depends on your business account and permissions.", 15));
            page.Children.Add(Button("Open eero Insight", async delegate
            {
                if (preview) throw new InvalidOperationException("Sign in to open eero Insight.");
                object temporary = await api.Send("GET", "/2.2/login/temporary");
                OpenServiceWindow("eero Insight", "https://insight.eero.com/login/mobile?next=/networks/" + NetworkId, ServiceLinks.TokenHeader(J.S(temporary, "temporary_token")));
            }));
        }
    }
}
