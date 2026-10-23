using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public static class VpnContracts
    {
        public const string PartnerHost = "wifi-connect-api.guardianapp.com";
        public const string ServerHost = "connect-api.guardianapp.com";
        // Publishable routing identifier used by eero's Guardian integration; not an account credential.
        public const string PublishableKey = "pk_2B2CE7masv5UM6JYgPTVUJ3JF";
        public static string EligibilityMessage(object availability)
        {
            var error = availability as ApiException;
            if (error != null)
            {
                if (error.Code == 402) return "Guardian VPN requires an eligible active benefit. Some internet providers do not include Guardian with their service.";
                if (error.Code == 403) return "Guardian VPN is not enabled for this network, or your account does not have permission to use it. Your internet provider may not offer this benefit.";
                if (error.Code == 404 || error.Code == 405) return "Guardian VPN availability is not provided for this network or service version.";
                return error.Message;
            }
            object seats = J.Get(availability, "seats_available");
            if (!(seats is bool)) return "Guardian VPN eligibility has not been confirmed. Check eligibility before installing the Windows component or registering a device.";
            return (bool)seats ? "Guardian reports a VPN device slot is available for this network." : "No Guardian VPN device slots are currently available. If your service includes Guardian, manage registered devices or check benefit activation.";
        }
        public static bool CanRegister(object availability)
        { return !(availability is ApiException) && J.Get(availability, "seats_available") is bool && J.B(availability, "seats_available"); }
        public static string Scope(string account, string network)
        {
            if (String.IsNullOrEmpty(account) || String.IsNullOrEmpty(network)) throw new ArgumentException("Sign in and select a network first.");
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(account + "\n" + network))).Replace("-", "").Substring(0, 24).ToLowerInvariant();
        }
        public static string Gateway(string host)
        {
            if (!Regex.IsMatch(host ?? "", @"^[a-z0-9][a-z0-9-]{0,62}\.sgw\.guardianapp\.com$", RegexOptions.IgnoreCase)) throw new InvalidDataException("Guardian returned an unsupported VPN server.");
            return host.ToLowerInvariant();
        }
        public static string Key(string value)
        {
            byte[] bytes; try { bytes = Convert.FromBase64String(value ?? ""); } catch (FormatException) { throw new InvalidDataException("A VPN key is invalid."); }
            try { if (bytes.Length != 32 || bytes.All(b => b == 0)) throw new InvalidDataException("A VPN key is invalid."); return Convert.ToBase64String(bytes); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        private static string Address(string value, AddressFamily family)
        {
            string[] parts = (value ?? "").Split('/'); IPAddress address;
            if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out address) || address.AddressFamily != family || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) throw new InvalidDataException("Guardian returned an invalid tunnel address.");
            int prefix = family == AddressFamily.InterNetwork ? 32 : 128;
            if (parts.Length == 2 && parts[1] != prefix.ToString()) throw new InvalidDataException("Guardian returned an unsupported tunnel prefix.");
            return address + "/" + prefix;
        }
        public static object Profile(string privateKey, string gateway, object response)
        {
            string ipv6 = J.S(response, "mapped-ipv6-address");
            return J.Obj("private_key", Key(privateKey), "server_key", Key(J.S(response, "server-public-key")), "ipv4", Address(J.S(response, "mapped-ipv4-address"), AddressFamily.InterNetwork), "ipv6", ipv6.Length == 0 ? "" : Address(ipv6, AddressFamily.InterNetworkV6), "gateway", Gateway(gateway));
        }
        public static string Configuration(object profile)
        {
            string ipv4 = Address(J.S(profile, "ipv4"), AddressFamily.InterNetwork), ipv6 = J.S(profile, "ipv6");
            if (ipv6.Length > 0) ipv6 = ", " + Address(ipv6, AddressFamily.InterNetworkV6);
            return "[Interface]\nPrivateKey = " + Key(J.S(profile, "private_key")) + "\nAddress = " + ipv4 + ipv6 + "\nDNS = 1.1.1.1, 1.0.0.1\n\n[Peer]\nPublicKey = " + Key(J.S(profile, "server_key")) + "\nAllowedIPs = 0.0.0.0/0, ::/0\nEndpoint = " + Gateway(J.S(profile, "gateway")) + ":51821\nPersistentKeepalive = 25\n";
        }
        public static object Subscriber(object credentials)
        {
            string id = J.S(credentials, "guardian_id"), secret = J.S(credentials, "guardian_secret");
            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(secret)) throw new InvalidDataException("eero did not return Guardian account credentials.");
            return J.Obj("ep-grd-subscriber-identifier", id, "ep-grd-subscriber-secret", secret);
        }
    }
    public sealed class GuardianApi : IDisposable
    {
        private readonly HttpClient http;
        public GuardianApi(HttpMessageHandler handler = null)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            http.Timeout = TimeSpan.FromSeconds(35);
        }
        public async Task<object> Send(string method, string host, string path, object body = null)
        {
            if (host != VpnContracts.PartnerHost && host != VpnContracts.ServerHost) VpnContracts.Gateway(host);
            if (!path.StartsWith("/api/v1") || path.Contains("..") || path.Any(c => c <= 32) || path.Contains("#") || path.Contains("\\")) throw new ArgumentException("Invalid Guardian API path.");
            var uri = new Uri("https://" + host + path);
            if (uri.Host != host || uri.Port != 443 || uri.UserInfo.Length > 0) throw new ArgumentException("Invalid Guardian API host.");
            using (var request = new HttpRequestMessage(new HttpMethod(method), uri))
            {
                if (host == VpnContracts.PartnerHost) request.Headers.Add("GRD-Connect-Publishable-Key", VpnContracts.PublishableKey);
                if (body != null)
                {
                    var values = body as IDictionary<string, object>;
                    if (host == VpnContracts.PartnerHost && values != null) { values = new Dictionary<string, object>(values); values["connect-publishable-key"] = VpnContracts.PublishableKey; body = values; }
                    request.Content = new StringContent(J.Stringify(body), Encoding.UTF8, "application/json");
                }
                using (var response = await http.SendAsync(request).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Guardian could not complete the request (HTTP " + (int)response.StatusCode + "). Check your benefit eligibility and try again.");
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (text.Length > 4 * 1024 * 1024) throw new InvalidDataException("Guardian returned an oversized response.");
                    return String.IsNullOrWhiteSpace(text) ? J.Obj() : J.Parse(text);
                }
            }
        }
        public Task<object> Regions() { return Send("GET", VpnContracts.ServerHost, "/api/v1.3/servers/all-server-regions/city"); }
        public Task<object> Devices(object credentials) { return Send("POST", VpnContracts.PartnerHost, "/api/v1.2/partners/subscriber/devices/list", VpnContracts.Subscriber(credentials)); }
        public Task<object> RemoveDevice(object credentials, string uuid)
        {
            var body = (Dictionary<string, object>)VpnContracts.Subscriber(credentials);
            body["ep-grd-device-uuid"] = ExtendedContracts.Identifier(uuid);
            return Send("POST", VpnContracts.PartnerHost, "/api/v1.2/partners/subscriber/device/delete", body);
        }
        public async Task<object> CreateProfile(string pet, string region, string privateKey, string publicKey)
        {
            if (String.IsNullOrEmpty(pet)) throw new InvalidOperationException("Register this Windows device first.");
            object rows = await Send("POST", VpnContracts.ServerHost, "/api/v1.3/servers/hostnames-for-region", J.Obj("region", region, "region-precision", "city", "paid", true, "feature-environment", 0, "beta-capable", false));
            object host = J.List(rows).Where(r => !J.B(r, "offline")).OrderBy(r => { int score; return Int32.TryParse(J.S(r, "capacity-score"), out score) ? score : Int32.MaxValue; }).FirstOrDefault();
            if (host == null) throw new InvalidOperationException("Guardian did not return an online server for that region.");
            string gateway = VpnContracts.Gateway(J.S(host, "hostname"));
            object credential = await Send("POST", VpnContracts.ServerHost, "/api/v1.2/subscriber-credential/create", J.Obj("validation-method", "pe-token", "pe-token", pet));
            string subscriber = J.S(credential, "subscriber-credential");
            if (String.IsNullOrEmpty(subscriber)) throw new InvalidDataException("Guardian did not return a subscriber credential.");
            object device = await Send("POST", gateway, "/api/v1.4/device-credentials", J.Obj("subscriber-credential", subscriber, "transport-protocol", "wireguard", "public-key", VpnContracts.Key(publicKey)));
            return VpnContracts.Profile(privateKey, gateway, device);
        }
        public void Dispose() { http.Dispose(); }
    }
}
