using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace EeroDesktop
{
    public static class ExtendedContracts
    {
        public static string InviteCode(string input)
        {
            string code = (input ?? "").Trim(); Uri url;
            if (Uri.TryCreate(code, UriKind.Absolute, out url))
            {
                if (url.Scheme != "https" || url.Port != 443 || url.UserInfo.Length > 0 || !(url.Host == "eero.com" || url.Host.EndsWith(".eero.com", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Use the eero invitation link or its invitation code.");
                var query = url.Query.TrimStart('?').Split('&').Select(p => p.Split(new[] { '=' }, 2)).Where(p => p.Length == 2).ToList();
                var pair = query.FirstOrDefault(p => new[] { "invite_code", "inviteCode", "code" }.Contains(Uri.UnescapeDataString(p[0])));
                code = pair == null ? url.AbsolutePath.Trim('/').Split('/').Last() : Uri.UnescapeDataString(pair[1]);
            }
            if (!Regex.IsMatch(code, "^[A-Za-z0-9_~.+/=-]{4,2048}$")) throw new ArgumentException("Enter a valid invitation code or eero invitation link.");
            return code;
        }
        public static string Identifier(string input)
        {
            if (!Regex.IsMatch(input ?? "", "^[A-Za-z0-9_-]{1,128}$")) throw new ArgumentException("The service returned an invalid identifier.");
            return input;
        }
        public static object InviteResponse(string code, bool accept) { return J.Obj("invite_code", InviteCode(code), "accept", accept); }
        public static object License(string key, string networkId = null, bool autoRenew = false)
        {
            key = (key ?? "").Trim(); if (key.Length < 4 || key.Length > 512 || key.Any(Char.IsControl)) throw new ArgumentException("Enter the software license key supplied with your subscription.");
            return networkId == null ? J.Obj("software_key", key) : J.Obj("software_key", key, "network_id", Identifier(networkId), "auto_renew", autoRenew);
        }
        public static object Pppoe(bool enabled, string username, string password, bool replaceCredentials, string vlan)
        {
            var pppoe = J.Obj("enabled", enabled);
            if (enabled && replaceCredentials)
            {
                if (String.IsNullOrWhiteSpace(username) || String.IsNullOrEmpty(password)) throw new ArgumentException("Enter both the PPPoE username and password provided by your ISP.");
                pppoe["credentials"] = J.Obj("username", username.Trim(), "password", password);
            }
            var body = J.Obj("pppoe", pppoe); int tag;
            if (!String.IsNullOrWhiteSpace(vlan)) { if (!Int32.TryParse(vlan, out tag) || tag < 0 || tag > 4094) throw new ArgumentException("VLAN ID must be between 0 and 4094."); body["vlan"] = tag.ToString(System.Globalization.CultureInfo.InvariantCulture); }
            else body["vlan"] = "";
            return body;
        }
        public static Uri UploadUrl(string input)
        {
            Uri u;
            if (!Uri.TryCreate(input, UriKind.Absolute, out u) || u.Scheme != "https" || u.Port != 443 || u.UserInfo.Length > 0 || u.Fragment.Length > 0 || !Regex.IsMatch(u.Host, @"^(?:[a-z0-9][a-z0-9.-]*\.)?s3(?:[.-][a-z0-9-]+)?\.amazonaws\.com$", RegexOptions.IgnoreCase)) throw new InvalidDataException("eero did not return a supported secure S3 upload destination.");
            return u;
        }
        public static string PortalMime(string path, byte[] bytes, bool logo)
        {
            if (bytes == null || bytes.Length < 8 || bytes.Length > (logo ? 5 : 10) * 1024 * 1024) throw new ArgumentException("Select a logo up to 5 MB or a PDF up to 10 MB.");
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (logo && ext == ".png" && bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
            if (logo && new[] { ".jpg", ".jpeg" }.Contains(ext) && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "image/jpeg";
            if (!logo && ext == ".pdf" && bytes.Take(5).SequenceEqual(new byte[] { 37, 80, 68, 70, 45 })) return "application/pdf";
            throw new ArgumentException(logo ? "Select a PNG or JPEG image." : "Select a PDF document.");
        }
        public static object ImportJob(string importPath, bool logo, string configurationId)
        {
            if (String.IsNullOrWhiteSpace(importPath) || importPath.Length > 4096) throw new InvalidDataException("eero did not return an import path.");
            return J.Obj("import_file_path", importPath, "import_job_type", logo ? "CaptivePortalLogo" : "CaptivePortalToS", "metadata", J.Stringify(J.Obj("configuration-id", Identifier(configurationId))));
        }
        public static string Email(string value)
        {
            value = (value ?? "").Trim(); if (value.Length > 254 || !Regex.IsMatch(value, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) throw new ArgumentException("Enter a valid email address."); return value;
        }
        public static object DnsPerformance(string text, int port)
        {
            string[] servers = (text ?? "").Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Distinct().ToArray();
            if (servers.Length == 0 || servers.Length > 8 || port < 1 || port > 65535) throw new ArgumentException("Enter one to eight DNS server IP addresses and a port between 1 and 65535.");
            var entries = new List<object>();
            foreach (string server in servers) { IPAddress ip; if (!IPAddress.TryParse(server, out ip)) throw new ArgumentException("Enter DNS server IP addresses rather than hostnames or URLs."); entries.Add(J.Obj("ip", ip.ToString(), "port", port)); }
            return J.Obj("servers", entries);
        }
    }
}
