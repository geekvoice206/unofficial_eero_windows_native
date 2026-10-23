using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace EeroDesktop
{
    public static class FeatureContracts
    {
        public static readonly string[] DeviceTypes = { "air_conditioner", "air_purifier", "alarm_system", "audio", "bed", "cable_box", "car", "cash_register", "coffee_maker", "credit_card_terminal", "desktop_computer", "digital_assistant", "door_bell", "door_lock", "exercise_bike", "e_reader", "fan", "game_console", "garage_door", "generic", "hard_drive", "hub", "laptop_computer", "light", "media_streamer", "network_equipment", "oven", "pet_device", "phone", "plug", "printer", "refrigerator", "remote", "scale", "security_camera", "smoke_detector", "sprinkler", "tablet", "television", "thermostat", "toaster", "unknown_computer", "vacuum", "washer_dryer", "watch" };
        public static string Mac(string input)
        {
            string s = Regex.Replace(input ?? "", "[-:. ]", "");
            if (!Regex.IsMatch(s, "^[0-9a-fA-F]{12}$")) throw new ArgumentException("Enter a MAC address with 12 hexadecimal digits.");
            return String.Join(":", Enumerable.Range(0, 6).Select(i => s.Substring(i * 2, 2).ToLowerInvariant()));
        }
        public static string Address(string input, AddressFamily family)
        {
            IPAddress a;
            string s = (input ?? "").Trim();
            if (!IPAddress.TryParse(s, out a) || a.AddressFamily != family || (family == AddressFamily.InterNetwork && !Regex.IsMatch(s, @"^(\d{1,3}\.){3}\d{1,3}$")))
                throw new ArgumentException("Enter a valid " + (family == AddressFamily.InterNetwork ? "IPv4" : "IPv6") + " address.");
            return a.ToString();
        }
        public static string PortRange(string input)
        {
            string[] parts = (input ?? "").Trim().Split('-');
            int a, b;
            if (parts.Length > 2 || !Int32.TryParse(parts[0], out a) || a < 1 || a > 65535) throw new ArgumentException("Ports must be between 1 and 65535, for example 443 or 8000-8010.");
            b = a;
            if (parts.Length == 2 && (!Int32.TryParse(parts[1], out b) || b < a || b > 65535)) throw new ArgumentException("Enter an ascending port range between 1 and 65535.");
            return a == b ? a.ToString() : a + "-" + b;
        }
        private static int PortCount(string range) { string[] a = range.Split('-'); return a.Length == 1 ? 1 : Int32.Parse(a[1]) - Int32.Parse(a[0]) + 1; }
        public static Dictionary<string, object> Forward(string ip, string external, string internalPort, string protocol, string description, bool enabled)
        {
            string gateway = PortRange(external), client = PortRange(internalPort);
            if (PortCount(gateway) != PortCount(client)) throw new ArgumentException("External and internal port ranges must contain the same number of ports.");
            if (!new[] { "tcp", "udp", "both" }.Contains(protocol)) throw new ArgumentException("Choose TCP, UDP, or both.");
            return J.Obj("ip", Address(ip, AddressFamily.InterNetwork), "gateway_port", gateway, "client_port", client, "protocol", protocol, "description", description.Trim(), "enabled", enabled);
        }
        public static Dictionary<string, object> Reservation(string name, string mac, string ip)
        { return J.Obj("description", name.Trim(), "mac", Mac(mac), "ip", Address(ip, AddressFamily.InterNetwork)); }
        public static Dictionary<string, object> Schedule(string name, IEnumerable<string> days, string start, string end, bool enabled, bool power = false)
        {
            MainWindow.ValidateName(name);
            var selected = days.Distinct().ToArray();
            if (selected.Length == 0 || selected.Any(d => !new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" }.Contains(d))) throw new ArgumentException("Choose at least one day.");
            DateTime a, b;
            if (!DateTime.TryParseExact(start.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out a) || !DateTime.TryParseExact(end.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out b) || a.TimeOfDay == b.TimeOfDay)
                throw new ArgumentException("Use different start and end times in 24-hour HH:mm format. Overnight schedules are supported.");
            return J.Obj("name", name.Trim(), "days", selected, power ? "start_time" : "start", a.ToString("HH:mm"), power ? "end_time" : "end", b.ToString("HH:mm"), "enabled", enabled);
        }
        public static Dictionary<string, object> Dns(bool custom, string servers, bool caching)
        {
            var ips = (servers ?? "").Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            foreach (string s in ips) { IPAddress address; if (!IPAddress.TryParse(s, out address)) throw new ArgumentException("Every DNS server must be a valid IPv4 or IPv6 address."); }
            if (custom && ips.Length == 0) throw new ArgumentException("Enter at least one custom DNS server.");
            if (ips.Length > 4) throw new ArgumentException("Enter no more than four DNS servers.");
            var result = J.Obj("mode", custom ? "custom" : "automatic", "caching", caching);
            if (custom) result["custom"] = J.Obj("ips", ips);
            return J.Obj("dns", result);
        }
        public static Dictionary<string, object> Dhcp(string mode, string subnet, string mask, string first, string last, bool randomization)
        {
            if (!new[] { "automatic", "custom", "bridge" }.Contains(mode)) throw new ArgumentException("Choose Automatic, Manual, or Bridge.");
            var result = J.Obj("connection", J.Obj("mode", mode == "bridge" ? "bridged" : "nat"), "nat_port_randomization", randomization);
            var dhcp = J.Obj("mode", mode == "custom" ? "custom" : "automatic");
            if (mode == "custom")
            {
                string s = Address(subnet, AddressFamily.InterNetwork), m = Address(mask, AddressFamily.InterNetwork), f = Address(first, AddressFamily.InterNetwork), l = Address(last, AddressFamily.InterNetwork);
                Func<string, uint> number = v => IPAddress.Parse(v).GetAddressBytes().Aggregate(0u, (n, octet) => (n << 8) | octet);
                uint network = number(s), netmask = number(m), begin = number(f), end = number(l), inverse = ~netmask;
                if (netmask == 0 || (inverse & (inverse + 1)) != 0 || (network & netmask) != network) throw new ArgumentException("Enter a contiguous subnet mask and the matching subnet network address.");
                if ((begin & netmask) != network || (end & netmask) != network || begin <= network || end >= (network | inverse) || begin > end) throw new ArgumentException("The DHCP range must be ordered and inside the subnet, excluding network and broadcast addresses.");
                dhcp["custom"] = J.Obj("subnet_ip", s, "subnet_mask", m, "start_ip", f, "end_ip", l);
            }
            if (mode != "bridge") result["dhcp"] = dhcp;
            return result;
        }
        public static string Domain(string value)
        {
            string s = (value ?? "").Trim().TrimEnd('.').ToLowerInvariant();
            if (s.Contains("://") || s.Contains("/") || s.Contains(" ") || s.Contains("@")) throw new ArgumentException("Enter a domain such as example.com, without https:// or a path.");
            try { s = new IdnMapping().GetAscii(s); } catch (ArgumentException) { throw new ArgumentException("Enter a valid domain name."); }
            if (Uri.CheckHostName(s) != UriHostNameType.Dns || !s.Contains(".") || s.Length > 253) throw new ArgumentException("Enter a valid domain such as example.com.");
            return s;
        }
        public static string WifiQr(string name, string password)
        {
            Func<string, string> escape = s => (s ?? "").Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace(":", "\\:").Replace("\"", "\\\"");
            return "WIFI:T:" + (String.IsNullOrEmpty(password) ? "nopass" : "WPA") + ";S:" + escape(name) + ";P:" + escape(password) + ";;";
        }
    }

    public sealed class FeatureEntry
    {
        public string Path, Description, Evidence, Limitation;
        public FeatureEntry(string path, string description, string evidence, string limitation = null) { Path = path; Description = description; Evidence = evidence; Limitation = limitation; }
        public string Title { get { return Path.Substring(Path.LastIndexOf('/') + 1); } }
        public string Parent { get { int i = Path.LastIndexOf('/'); return i < 0 ? "" : Path.Substring(0, i); } }
    }
    public static class FeatureCatalog
    {
        public static readonly FeatureEntry[] Entries = {
            new FeatureEntry("Home/Internet", "Internet status, speed tests, WAN and backup connectivity", "TabBarMode, HomeViewModel"),
            new FeatureEntry("Home/eeros", "Node details, status lights, connections and restarts", "IEeroService"),
            new FeatureEntry("Home/Profiles", "Create profiles, assign devices, pause access and schedule pauses", "ProfileUpdateRequest, ScheduledPause"),
            new FeatureEntry("Home/Security & privacy", "Advanced Security, content filters, ad blocking and website controls", "DnsPolicySlug, INetworkService"),
            new FeatureEntry("Home/Security & privacy/Content filters", "Unified content filters for additional wifi subnets", "SubnetContentFiltersRequest"),
            new FeatureEntry("Home/Add or replace eero", "Add by serial number, discover nearby eeros and configure automatic setup", "IEeroService.addEeroWithSerial"),
            new FeatureEntry("Home/Add or replace eero/Find nearby eeros", "Native Bluetooth discovery and read-only eero identity checks", "EeroBleManagerLive, EeroUuids"),
            new FeatureEntry("Home/Add or replace eero/Automatic setup", "Cloud-discovered eeros and automatic setup preferences", "getAutoDiscoveredEeros, SetAutoSetupMode"),
            new FeatureEntry("Home/Add or replace eero/Create a network", "Create a wired DHCP network and claim its gateway by serial", "CreateNetworkRequest, IEeroService.addEeroWithSerial", "Not validated on unconfigured hardware; Bluetooth write provisioning and gateway replacement are incomplete."),
            new FeatureEntry("Home/eeros/Placement tests", "Run cloud placement tests and review eero signal ratings", "runPlacementTest, PlacementResult"),
            new FeatureEntry("Home/Security & privacy/VPN", "Guardian device registration, locations, devices and Windows VPN controls", "GuardianVpnPetRequest, Guardian Connect API, WireGuard Windows"),
            new FeatureEntry("Devices/Blocked devices", "Review and unblock devices", "NetworkService.blockDevice, unblockDevice"),
            new FeatureEntry("Activity/Data usage", "Network data usage and device breakdown", "IDataUsageService"),
            new FeatureEntry("Activity/Security activity", "Threat and content-filter activity", "IInsightsService"),
            new FeatureEntry("Activity/Network events", "Recent network events", "INetworkService.getNetworkEvents"),
            new FeatureEntry("Settings/Wifi name & password", "View, change, copy, and share wifi details as a QR code", "NetworkResources.password"),
            new FeatureEntry("Settings/Guest wifi network", "Guest network access, credentials and QR sharing", "GuestNetwork, GuestResources"),
            new FeatureEntry("Settings/Additional wifi networks", "Additional SSIDs, guest and IoT access, bandwidth limits and subnet filters", "SubnetConfig"),
            new FeatureEntry("Settings/Business guest wifi", "Existing guest portal branding, session duration and visitor fields", "CaptivePortalConfigurationRequest"),
            new FeatureEntry("Settings/Nickname & location", "Network nickname and timezone", "NetworkService.updateBusinessNameOrNetworkNickname"),
            new FeatureEntry("Settings/Advanced networking", "DHCP, DNS, reservations, firewall, wireless and smart-home options", "SettingsRoute, INetworkService"),
            new FeatureEntry("Settings/Advanced networking/Internet connection", "WAN address, DHCP, static IP and PPPoE status", "Network.ip_settings, NetworkPppoeConfig"),
            new FeatureEntry("Settings/Advanced networking/Internet connection/ISP settings", "Existing-network PPPoE credentials and ISP VLAN tagging", "NetworkPppoeConfig, Network.vlan"),
            new FeatureEntry("Settings/Advanced networking/DHCP & NAT", "Automatic/manual addressing, bridge mode and NAT port randomization", "DhcpConfigurationRequest"),
            new FeatureEntry("Settings/Advanced networking/DNS", "ISP or custom DNS and local DNS caching", "Dns, HostInfoList"),
            new FeatureEntry("Settings/Advanced networking/DNS/DNS performance", "Compare resolver latency and review gateway DNS test results", "DnsPerfTestRequest, NetworkService.runDnsPerfTest"),
            new FeatureEntry("Settings/Advanced networking/Reservations & port forwarding", "IPv4 reservations, port mappings and IPv6 firewall rules", "NetRules, NetworkReservation, NetworkForward"),
            new FeatureEntry("Settings/Advanced networking/IPv6", "Enable or disable upstream IPv6", "Network.ipv6_upstream"),
            new FeatureEntry("Settings/Advanced networking/UPnP", "Automatic port mapping", "Network.upnp"),
            new FeatureEntry("Settings/Advanced networking/Wireless settings", "WPA3, client steering, optimization, compatibility and MLO", "Network, MultiLinkOperationModeRequest"),
            new FeatureEntry("Settings/Advanced networking/Thread", "Thread network and credential syncing", "UpdateThreadNetworkRequest"),
            new FeatureEntry("Settings/Advanced networking/Dynamic DNS", "eero DDNS access and hostname", "INetworkService.ddnsEnable, ddnsDisable"),
            new FeatureEntry("Settings/Advanced networking/Power saving", "Power optimization and scheduled power saving", "PowerOptimizationRequest, PowerSavingScheduleRequest"),
            new FeatureEntry("Settings/Advanced networking/Internet backup", "Backup access points and connectivity tests", "AddBackupNetworkRequest, BackupInternetEnabledRequest"),
            new FeatureEntry("Settings/Advanced networking/Multi-WAN", "WAN links, failover and secondary-WAN device access", "INetworkService.coGetWanLinks"),
            new FeatureEntry("Settings/Advanced networking/Multiple static IPs", "Public LAN and NAT public-address ranges", "MultiStaticIpRequest"),
            new FeatureEntry("Settings/User permissions", "Network members, admin invitations and revocation", "CreateInviteRequest, INetworkService.removeAdmin"),
            new FeatureEntry("Settings/User permissions/Join a network", "Review, accept or decline an eero invitation", "InviteQueryRequest, ExternalInviteRequest"),
            new FeatureEntry("Settings/User permissions/Incoming access invitations", "Review, accept or decline account access invitations", "MemberPromotionResponse, PromotionInviteRequest"),
            new FeatureEntry("Settings/Software updates", "Update status, install available firmware and preferred update hour", "NetworkUpdates, PreferredUpdateHour"),
            new FeatureEntry("Settings/Appearance", "Light, dark or Windows system appearance", "SettingsRoute.Appearance"),
            new FeatureEntry("Settings/Notifications", "Network notification preferences, history and Windows notifications", "NetworkNotifications, PushSettings"),
            new FeatureEntry("Settings/Troubleshooting", "Connection checks, diagnostics, coverage and upgrade scans", "INetworkService.wifiCoverageScan, wifiGenerationScan"),
            new FeatureEntry("Settings/Troubleshooting/Guided diagnostics", "Diagnose slow internet, offline service, dropped connections and red eeros", "HealthCheckSymptom, HealthCheckResults"),
            new FeatureEntry("Settings/Troubleshooting/Connect a device", "Temporarily pause and restore 5 GHz for device setup", "TemporaryFlags.hide_5g, updateTemporaryFlags"),
            new FeatureEntry("Settings/Advanced networking/Wireless settings/Fast roaming", "Review and configure fast transition compatibility", "FastTransitionStatus"),
            new FeatureEntry("Settings/Advanced networking/Internet backup/Find backup wifi", "Discover nearby backup wifi networks and add a selected result", "BackupNetworkDiscoveryStatusResponse"),
            new FeatureEntry("Settings/Notifications/Windows notifications", "Windows startup, background alerts and missed-update tracking", "Windows background notifications"),
            new FeatureEntry("Settings/Account settings", "Account details, name, verified email/phone changes and email preferences", "IUserService"),
            new FeatureEntry("Settings/Account settings/Subscriptions", "Subscription status, software-key activation and partner benefits", "IPremiumService, ActivateLicenseKeyRequestBody"),
            new FeatureEntry("Settings/Account settings/Subscriptions/Redeem a license key", "Validate a software key, review renewal and activate for a network", "ValidateLicenseKeyRequestBody, ActivateLicenseKeyRequestBody"),
            new FeatureEntry("Settings/Account settings/Subscriptions/Partner benefits", "1Password and Malwarebytes codes, benefit status and VPN seats", "IPremiumService"),
            new FeatureEntry("Settings/Account settings/Subscriptions/Manage subscriptions & purchases", "Official eero payment service using a temporary account token", "TemporaryExternalToken, TargetPage"),
            new FeatureEntry("Settings/Account settings/Subscriptions/Offers & referrals", "Eligible plan offers, referral links, referral email and referral status", "EeroPlusOfferRequest, SubscriptionReferralRequest"),
            new FeatureEntry("Settings/Account settings/Subscriptions/Billing & status", "Refresh benefits, payment methods and purchase history", "IPremiumService.getCustomer, TargetPage"),
            new FeatureEntry("Settings/Account settings/Subscriptions/Business billing", "Business payment-account management and onboarding", "PaymentAccountManagementResponse, PaymentAccountOnboardingResponse"),
            new FeatureEntry("Home/Security & privacy/Identity protection", "Identity protection activation, digital-footprint scans and email verification", "DigitalFootprintScanRequest, DigitalFootprintVerifyCodeRequest"),
            new FeatureEntry("Settings/Connected services", "Amazon, eero Built-in and connected-service status", "Network.proxied_nodes, simple_setup"),
            new FeatureEntry("Settings/Connected services/Connected device setup", "Review and configure per-vendor device setup permissions", "SimpleSetupNetworkConfiguration"),
            new FeatureEntry("Settings/Connected services/Amazon connected home", "Linked-account status, Zigbee discovery and account unlinking", "runZigbeeDeviceDiscovery, unlinkAmazonAccount"),
            new FeatureEntry("Settings/Connected services/Accessories & cellular", "Accessory setup status, details, removal and phone-line associations", "AccessoryStatus, Accessory, SipConfigAssociation"),
            new FeatureEntry("Settings/Business guest wifi/eero Insight", "Open the official business management portal", "SettingsViewModel.setupEeroInsight"),
            new FeatureEntry("Settings/Network management", "Network switching, ownership transfer and removal", "NetworkResources.transfer, deleteNetwork"),
            new FeatureEntry("Settings/Network management/Pending transfers", "Accept or decline incoming network transfers", "PendingTransfersData, acceptNetworkTransfer, denyNetworkTransfer"),
            new FeatureEntry("Settings/Network management/Outgoing transfer", "Review, resend or cancel a pending ownership invitation", "getTransferNetworkInvite, resendTransferNetwork"),
            new FeatureEntry("Settings/About", "Version, sources, capabilities and compatibility information", "Feature inventory"),
            new FeatureEntry("Home/Chat", "Streaming eero chat, recommendations and permission requests", "IConversationsService, ConversationStreamingService"),
            new FeatureEntry("Help/Support history", "Read and continue previous eero conversations", "ConversationsResponse, ConversationResponse"),
            new FeatureEntry("Help/Contact support", "Account-specific chat, help links and contact information", "SupportInfo, SupportInfoItem, TYPE.CHAT"),
            new FeatureEntry("Help", "Official support, support details and feature availability", "TabBarMode.Troubleshooting"),
            new FeatureEntry("Help/Service availability", "Account, hardware and service requirements", "Service integrations", "Full Bluetooth provisioning and gateway replacement are unavailable. Amazon sign-in requires an existing eero account using the same Amazon login; no developer account or Business subscription is needed. Email/phone sign-in remains available for accounts that use it. Purchases remain managed by their billing provider. Guardian VPN needs an eligible account, the Windows WireGuard component and administrator approval.")
        };
        public static string Parent(string path) { int i = path.LastIndexOf('/'); return i < 0 ? "Home" : path.Substring(0, i); }
        public static string Root(string path) { return path.Split('/')[0]; }
        public static IEnumerable<FeatureEntry> Children(string path) { return Entries.Where(f => f.Parent == path); }
    }
}
