using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public sealed class WiredNetworkSetup
    {
        public bool CreationUncertain { get; private set; }
        public string CreatedUrl { get; private set; }
        private string serial, location;
        public async Task Create(EeroApi api, object body, string serialNumber, string nodeLocation)
        {
            if (CreatedUrl != null || CreationUncertain) throw new InvalidOperationException("A creation request was already sent. Review the network in your account before starting another setup.");
            serial = RuntimeContracts.Serial(serialNumber); MainWindow.ValidateName(nodeLocation); location = nodeLocation.Trim();
            CreationUncertain = true;
            object created = await api.Send("POST", "/2.3/networks", body);
            Uri resource = EeroApi.Resource(J.S(created, "url"));
            if (!System.Text.RegularExpressions.Regex.IsMatch(resource.AbsolutePath, @"^/2\.[23]/networks/[A-Za-z0-9_-]+$") || resource.Query.Length != 0) throw new InvalidOperationException("The server did not return a valid created network. Refresh your account before continuing.");
            CreatedUrl = resource.AbsolutePath; CreationUncertain = false;
        }
        public async Task<string> Claim(EeroApi api)
        {
            if (CreatedUrl == null) throw new InvalidOperationException("Create a network first.");
            string destination = CreatedUrl;
            await api.Send("POST", "/2.2/eeros?requireSession=true", J.Obj("serial", serial, "location", location, "network", destination), true);
            CreatedUrl = serial = location = null;
            return destination;
        }
    }
    public sealed partial class MainWindow
    {
        private WiredNetworkSetup wiredSetup = new WiredNetworkSetup();
        private void CreateNetworkPage()
        {
            Sub("Create a wired network");
            page.Children.Add(Card(Text("Use a separate, unconfigured eero connected by Ethernet to a working DHCP internet connection. Review eero's agreements, create the network, then add the gateway by serial number. This flow has not been validated with an unconfigured eero. Keep your active network's gateway in place.", 15)));
            if (wiredSetup.CreatedUrl != null)
            {
                page.Children.Add(Text("A network was created. Continue adding its gateway; this will reuse the created network.", 15, "#64738A"));
                page.Children.Add(Button("Continue adding gateway", async delegate { await ClaimCreatedGateway(); }));
            }
            else if (wiredSetup.CreationUncertain)
                page.Children.Add(Text("The creation response was interrupted. Use Refresh to inspect your account before starting another creation. This window will not repeat the creation request.", 15, "#B5333D"));
            else
                page.Children.Add(LocalButton("Review setup & agreements", delegate
                {
                    Edit("Gateway for new network", new[] { new Field("serial", "Serial number of the unconfigured gateway") }, async v =>
                    {
                        string serial = RuntimeContracts.Serial(V(v, "serial"));
                        if (nodes.Any(n => J.S(n, "serial").Equals(serial, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("This eero belongs to the active network. Use a separate eero ready for setup.");
                        if (preview) throw new InvalidOperationException("Sign in to create a network.");
                        object agreements = await api.Send("GET", "/2.2/consents/network?serial=" + Uri.EscapeDataString(serial));
                        AfterEditor(delegate { NetworkCreationEditor(serial, J.List(agreements)); });
                    }, "Continue");
                }));
            page.Children.Add(Section("Find nearby eeros", "Home/Add or replace eero/Find nearby eeros"));
            page.Children.Add(Text("PPPoE/static WAN bootstrap, cellular activation and gateway replacement still require the official setup wizard. Existing-network ISP settings remain available under Advanced networking.", 13, "#64738A"));
        }
        private void NetworkCreationEditor(string serial, List<object> agreements)
        {
            var fields = new List<Field> { new Field("name", "New wifi name"), new Field("password", "Wifi password", null, "password"), new Field("timezone", "IANA timezone (for example America/Chicago)", J.S(network, "timezone", "value")), new Field("location", "Gateway location") };
            for (int i = 0; i < agreements.Count; i++)
            {
                object agreement = agreements[i];
                if (J.S(agreement, "name").Length == 0 || J.S(agreement, "localized_consent").Length == 0) throw new InvalidOperationException("eero returned an incomplete agreement. Setup cannot continue.");
                fields.Add(new Field("consent_" + i, J.S(agreement, "localized_consent") + (J.B(agreement, "requires_consent") ? " (required)" : " (optional)"), false, "bool"));
                foreach (object link in J.List(J.Get(agreement, "links"))) { string url = J.S(link, "link"); ServiceLinks.Eero(url); fields.Add(new Field("", "Read agreement on eero.com", url, "link")); }
            }
            Edit("Create new network", fields, async v =>
            {
                if (wiredSetup.CreatedUrl != null || wiredSetup.CreationUncertain) throw new InvalidOperationException("A creation request was already sent. Close this dialog and review its result.");
                ValidateName(V(v, "location")); var consents = new Dictionary<string, object>();
                for (int i = 0; i < agreements.Count; i++) { bool accepted = Flag(v, "consent_" + i); if (J.B(agreements[i], "requires_consent") && !accepted) throw new ArgumentException("Review and accept each required agreement to continue."); consents.Add(J.S(agreements[i], "name"), accepted); }
                object body = RuntimeContracts.CreateNetwork(V(v, "name"), V(v, "password"), V(v, "timezone").Trim(), consents);
                if (!Confirm("Create a new network named " + V(v, "name") + " and add the gateway with serial " + serial + "? Your current network is separate.")) return;
                try { await wiredSetup.Create(api, body, serial, V(v, "location")); }
                finally { RenderPage(); }
                AfterEditor(delegate { RunClaimCreatedGateway(); });
            }, "Create network", "The gateway must be separate from your active network and already connected to internet by Ethernet.");
        }
        private async void RunClaimCreatedGateway() { await Run(async delegate { await ClaimCreatedGateway(); }, "Adding new gateway…"); }
        private async Task ClaimCreatedGateway()
        {
            if (preview || wiredSetup.CreatedUrl == null) throw new InvalidOperationException("Create a network first.");
            // The created resource survives a failed claim; retrying never creates another network.
            string destination = await wiredSetup.Claim(api);
            try { await LoadAccount(); networkUrl = destination; await LoadNetwork(); currentPage = "Home"; ShowShell(); SetNotice("Gateway addition accepted. Check the network status while setup finishes."); }
            catch (Exception e) { SetNotice("Gateway addition was accepted. Refresh did not finish: " + Message(e), true); }
        }
    }
}
