using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void AdditionalWifiPage(object data)
        {
            Sub("Additional wifi networks");
            foreach (object raw in Entries(data, "subnets", "configurations"))
            {
                object subnet = raw; var p = new StackPanel(); Details(p, subnet, 0); var row = new WrapPanel();
                row.Children.Add(LocalButton("Edit wifi network", delegate { SubnetEdit(subnet); }));
                row.Children.Add(Button("Content filters", async delegate { await EditSubnetFilters(subnet); }));
                string type = J.S(subnet, "subnet_type");
                if (type == "A" || type == "B") row.Children.Add(Button("Delete additional network", async delegate { if (!Confirm("Delete this additional wifi network? Connected devices will disconnect.")) return; await Change("DELETE", FeatureUrl("subnets_config/" + type), null, false, "Additional wifi network deleted."); }));
                p.Children.Add(row); page.Children.Add(Card(p));
            }
            page.Children.Add(LocalButton("Add additional wifi network", delegate { SubnetEdit(null); }));
            page.Children.Add(Text("The service enforces the number of SSIDs, available subnet types and subscription requirements for your hardware.", 13, "#64738A"));
        }
        private void SubnetEdit(object subnet)
        {
            var fields = new List<Field> { new Field("name", "Wifi name", J.S(subnet, "name")), new Field("password", "New password (blank keeps current when editing)", null, "password"), new Field("enabled", "Enabled", subnet == null || J.B(subnet, "enabled"), "bool"), new Field("open_network", "Open network without a password", J.B(subnet, "open_network"), "bool"), new Field("wan_access", "Allow internet access", subnet == null || J.B(subnet, "wan_access"), "bool"), new Field("rate", "Bandwidth limit percentage (1–100)", J.S(subnet, "rate_limit_pct").NullIfEmpty() ?? "100") };
            if (subnet == null) { fields.Add(new Field("type", "Additional wifi slot", "A", "choice", "A", "B")); fields.Add(new Field("kind", "Purpose", "IoT", "choice", "IoT", "Business", "Kids", "Guest")); }
            Edit(subnet == null ? "Add additional wifi" : "Edit additional wifi", fields, async v =>
            {
                ValidateSsid(V(v, "name")); int rate; if (!Int32.TryParse(V(v, "rate"), out rate) || rate < 1 || rate > 100) throw new ArgumentException("Enter a bandwidth percentage between 1 and 100.");
                string password = V(v, "password").NullIfEmpty() ?? J.S(subnet, "password");
                if (!Flag(v, "open_network")) ValidatePassword(password);
                var body = J.Obj("name", V(v, "name").Trim(), "enabled", Flag(v, "enabled"), "open_network", Flag(v, "open_network"), "wan_access", Flag(v, "wan_access"), "rate_limit_pct", rate);
                if (subnet != null) foreach (string key in new[] { "network_id", "subnet_id", "subnet_type", "subnet_kind", "dedicated_subnet" }) { object existing = J.Get(subnet, key); if (existing != null) body[key] = existing; }
                else { body["network_id"] = networkUrl.TrimEnd('/').Split('/').Last(); body["subnet_type"] = V(v, "type"); body["subnet_kind"] = V(v, "kind"); }
                if (!Flag(v, "open_network")) body["password"] = password;
                if (!Confirm("Save this wifi network? Devices using it may disconnect.")) return;
                await Change("PUT", FeatureUrl("subnets_config"), body, false, "Wifi subnet saved.");
            });
        }
        private void SubnetFiltersPage(object data)
        {
            Sub("Unified content filters by wifi network");
            foreach (object raw in Entries(data, "subnets"))
            {
                object subnet = raw; var p = new StackPanel(); p.Children.Add(Text(J.First(subnet, "name", "subnet_type"), 20, "#17263C", true)); p.Children.Add(Button("Edit content filters", async delegate { await EditSubnetFilters(subnet); })); page.Children.Add(Card(p));
            }
            if (data != null && Entries(data, "subnets").Count == 0) page.Children.Add(Text("No configurable subnets were returned.", 14, "#64738A"));
        }
        private async Task EditSubnetFilters(object subnet)
        {
            string id = J.S(subnet, "subnet_id").NullIfEmpty() ?? J.S(subnet, "subnet_type"); if (id.Length == 0) throw new InvalidOperationException("eero did not provide a subnet identifier.");
            object data = await Read(FeatureUrl("subnets_config/" + Uri.EscapeDataString(id) + "/dns_policies/content_filters"));
            var map = new Dictionary<string, object>();
            foreach (object candidate in new[] { J.Get(data, "dns_policies"), J.Get(data, "unified_content_filters", "dns_policies"), J.Get(data, "premium_dns", "dns_policies"), J.Get(data, "premium_dns", "unified_content_filters", "dns_policies") })
            { var values = candidate as IDictionary<string, object>; if (values != null) foreach (var p in values) if (p.Value is bool) map[p.Key] = p.Value; }
            if (map.Count == 0) throw new InvalidOperationException("No editable content filters were returned for this subnet.");
            Edit("Wifi content filters", map.Select(p => new Field(p.Key, Human(p.Key), p.Value, "bool")), async v => { var filters = map.ToDictionary(p => p.Key, p => (object)Flag(v, p.Key)); await Change("PUT", FeatureUrl("subnets_config/dns_policies/content_filters"), J.Obj("subnets", new[] { id }, "content_filters", filters), false, "Subnet content filters updated."); });
        }
        private async Task BandSecurity()
        {
            string path = FeatureUrl("wpa3_per_band"); object data = await Read(path);
            Edit("Security by radio band", new[] { new Field("band_2_4_ghz", "2.4 GHz security", J.S(data, "band_2_4_ghz"), "choice", "WPA2", "WPA2_WPA3", "WPA3"), new Field("band_5_ghz", "5 GHz security", J.S(data, "band_5_ghz"), "choice", "WPA2", "WPA2_WPA3", "WPA3") }, async v => { if (!Confirm("Change wifi authentication modes? Some devices may need to reconnect or may not support the new mode.")) return; await Change("PUT", path, J.Obj("band_2_4_ghz", V(v, "band_2_4_ghz"), "band_5_ghz", V(v, "band_5_ghz")), false, "Per-band wifi security updated."); }, "Save", "6 GHz uses WPA3 as required by that band; this endpoint edits the 2.4 and 5 GHz modes.");
        }
        private void MultiStaticPage(object data)
        {
            Sub("Multiple static IPs · requires ISP support"); ObjectCards(data);
            page.Children.Add(LocalButton("Configure static public IPs", delegate
            {
                object lan = J.Get(data, "multistaticip_settings"), nat = J.Get(data, "multistaticip_settings_nat_portfwd");
                Edit("Multiple static IPs", new[] { new Field("enabled", "Enable multiple static IPs", J.B(data, "enabled"), "bool"), new Field("type", "Mode", J.S(data, "type").NullIfEmpty() ?? "PublicLAN", "choice", "PublicLAN", "NATWithPortFwd"), new Field("subnet", "Public LAN subnet address", J.S(lan, "subnet_ip")), new Field("mask", "Public LAN subnet mask", J.S(lan, "subnet_mask")), new Field("router", "Public LAN router address", J.S(lan, "router_ip")), new Field("start", "NAT public IP range start", J.S(nat, "subnet_ip_start")), new Field("end", "NAT public IP range end", J.S(nat, "subnet_ip_end")) }, async v =>
                {
                    var body = J.Obj("enabled", Flag(v, "enabled"), "type", V(v, "type"));
                    if (Flag(v, "enabled"))
                    {
                        if (V(v, "type") == "PublicLAN") body["multistaticip_settings"] = J.Obj("subnet_ip", FeatureContracts.Address(V(v, "subnet"), AddressFamily.InterNetwork), "subnet_mask", FeatureContracts.Address(V(v, "mask"), AddressFamily.InterNetwork), "router_ip", FeatureContracts.Address(V(v, "router"), AddressFamily.InterNetwork));
                        else body["multistaticip_settings_nat_portfwd"] = J.Obj("subnet_ip_start", FeatureContracts.Address(V(v, "start"), AddressFamily.InterNetwork), "subnet_ip_end", FeatureContracts.Address(V(v, "end"), AddressFamily.InterNetwork));
                    }
                    if (!Confirm("Apply multiple-static-IP settings supplied by your ISP? Incorrect values can disrupt internet access.")) return;
                    await Change("PUT", "/2.3/networks/" + networkUrl.TrimEnd('/').Split('/').Last() + "/multistaticip", body, false, "Multiple static IP settings updated.");
                });
            }));
        }
        private void BusinessPortalPage(object data)
        {
            Sub("Business guest wifi · existing portal configurations");
            page.Children.Add(Section("eero Insight", "Settings/Business guest wifi/eero Insight"));
            foreach (object raw in Entries(data, "configurations"))
            {
                object config = raw; var p = new StackPanel(); Details(p, config, 0);
                var row = new WrapPanel(); row.Children.Add(LocalButton("Edit portal branding", delegate { PortalEdit(config); }));
                row.Children.Add(Button("Upload logo", async delegate { await UploadPortalAsset(config, true); }));
                row.Children.Add(Button("Upload terms PDF", async delegate { await UploadPortalAsset(config, false); }));
                row.Children.Add(LocalButton("Visitor information", delegate { Edit("Visitor information", new[] { new Field("email", "Capture visitor email", J.B(config, "capture_email"), "bool"), new Field("name", "Capture visitor name", J.B(config, "capture_name"), "bool") }, async v => { int id; if (!Int32.TryParse(J.First(config, "configuration_id", "id"), out id)) throw new InvalidOperationException("Missing portal configuration ID."); await Change("PUT", FeatureUrl("captive_portal/configuration"), J.Obj("id", id, "capture_email", Flag(v, "email"), "capture_name", Flag(v, "name")), false, "Visitor information settings updated."); }); }));
                row.Children.Add(Button("Use for guest wifi", async delegate { int id; if (!Int32.TryParse(J.First(config, "configuration_id", "id"), out id)) throw new InvalidOperationException("Missing portal configuration ID."); if (!Confirm("Use this sign-in portal for guest wifi?")) return; await Change("PUT", FeatureUrl("captive_portal/subnet_id/GUEST"), J.Obj("captive_portal_configuration_id", id), false, "Guest portal configuration selected."); })); p.Children.Add(row); page.Children.Add(Card(p));
            }
            page.Children.Add(Text("Edit existing portal text, theme, visitor fields, session duration and hosted links, or upload a PNG/JPEG logo and terms PDF. New business provisioning and cellular/accessory activation still depend on their service integrations.", 13, "#64738A"));
        }
        private void PortalEdit(object config)
        {
            Edit("Guest portal branding", new[] { new Field("header", "Heading", J.S(config, "header")), new Field("sub_header", "Subheading", J.S(config, "sub_header")), new Field("color_theme", "Theme", J.S(config, "color_theme").NullIfEmpty() ?? "Light", "choice", "Light", "Dark"), new Field("minutes", "Session duration in minutes", J.S(config, "access_expiration_in_minutes")), new Field("logo", "Hosted business logo URL (optional)", J.S(config, "business_logo_url")), new Field("terms", "Hosted business terms URL (optional)", J.S(config, "business_terms_of_service_url")) }, async v =>
            {
                int id, minutes; if (!Int32.TryParse(J.First(config, "configuration_id", "id"), out id) || !Int32.TryParse(V(v, "minutes"), out minutes) || minutes < 1 || minutes > 525600) throw new ArgumentException("Enter a session duration between 1 and 525600 minutes.");
                foreach (string key in new[] { "logo", "terms" }) if (V(v, key).Length > 0) { Uri uri; if (!Uri.TryCreate(V(v, key), UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new ArgumentException("Use HTTPS URLs for hosted assets and terms."); }
                var body = J.Obj("id", id, "header", V(v, "header"), "sub_header", V(v, "sub_header"), "color_theme", V(v, "color_theme"), "access_expiration_in_minutes", minutes, "business_logo_url", V(v, "logo"), "business_terms_of_service_url", V(v, "terms"));
                await Change("PUT", FeatureUrl("captive_portal/configuration"), body, false, "Guest portal branding updated.");
            });
        }
    }
}
