using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EeroDesktop
{
    internal static partial class Tests
    {
        private static readonly List<string> results = new List<string>();
        private static void Assert(bool pass, string name)
        { if (!pass) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); }
        private static async Task Throws<T>(Func<Task> action, string name) where T : Exception
        {
            try { await action(); }
            catch (T) { results.Add("PASS: " + name); return; }
            throw new Exception("FAIL: " + name);
        }
        private sealed class Step
        { public string Method, Path, Cookie, Body, ContentType, Json, SetCookie, Query; public int Status = 200; }
        private sealed class Fake : HttpMessageHandler
        {
            public readonly Queue<Step> Steps = new Queue<Step>();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
            {
                if (Steps.Count == 0) throw new Exception("Unexpected request: " + r.Method + " " + r.RequestUri.AbsolutePath);
                Step s = Steps.Dequeue();
                Assert(r.Method.Method == s.Method && r.RequestUri.AbsolutePath == s.Path, "request " + s.Method + " " + s.Path);
                if (s.Query != null) Assert(r.RequestUri.Query == s.Query, "correct query parameters");
                Assert(r.RequestUri.Host == "api-user.e2ro.com" && r.RequestUri.Scheme == "https", "HTTPS trusted host");
                IEnumerable<string> values;
                string cookie = r.Headers.TryGetValues("Cookie", out values) ? String.Join("", values) : null;
                Assert(cookie == s.Cookie, "correct cookie scope");
                if (s.Body != null) Assert(r.Content != null && await r.Content.ReadAsStringAsync() == s.Body, "request payload");
                if (s.ContentType != null) Assert(r.Content.Headers.ContentType.MediaType == s.ContentType, "request content type");
                var response = new HttpResponseMessage((HttpStatusCode)s.Status) { Content = new StringContent(s.Json ?? "{\"meta\":{\"code\":200},\"data\":{}}", Encoding.UTF8, "application/json") };
                if (s.SetCookie != null) response.Headers.TryAddWithoutValidation("Set-Cookie", s.SetCookie);
                return response;
            }
        }
        public static int Run(string output)
        {
            string scratch = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output)), "session-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            int exit = 0;
            try { RunAsync(scratch).GetAwaiter().GetResult(); FeatureChecks(); ExtendedChecks().GetAwaiter().GetResult(); RuntimeChecks(scratch).GetAwaiter().GetResult(); EverydayChecks(scratch); ServiceIntegrationChecks(scratch).GetAwaiter().GetResult(); AmazonNativeChecks(scratch).GetAwaiter().GetResult(); UiChecks(); ThemeChecks(); NativeChecks(); }
            catch (Exception e) { results.Add(e.ToString()); exit = 1; }
            finally
            {
                foreach (string file in Directory.GetFiles(scratch)) File.Delete(file);
                Directory.Delete(scratch);
                File.WriteAllLines(output, results);
            }
            return exit;
        }
        private static void FeatureChecks()
        {
            Assert(J.Stringify(FeatureContracts.Reservation("Server", "0200.0000.0001", "192.168.4.20")) == "{\"description\":\"Server\",\"mac\":\"02:00:00:00:00:01\",\"ip\":\"192.168.4.20\"}", "API reservation fields and MAC normalization");
            object forward = FeatureContracts.Forward("192.168.4.20", "8000-8010", "9000-9010", "both", "Example", true);
            Assert(J.S(forward, "gateway_port") == "8000-8010" && J.S(forward, "client_port") == "9000-9010" && J.S(forward, "protocol") == "both", "API port-forward contract uses gateway_port/client_port/both");
            bool invalid = false; try { FeatureContracts.Forward("192.168.4.20", "8000-8010", "9000", "tcp", "Example", true); } catch (ArgumentException) { invalid = true; } Assert(invalid, "mismatched forwarded ranges rejected");
            invalid = false; try { FeatureContracts.PortRange("70000"); } catch (ArgumentException) { invalid = true; } Assert(invalid, "out-of-range ports rejected");
            object dns = FeatureContracts.Dns(false, "", true); Assert(J.S(dns, "dns", "mode") == "automatic", "API automatic DNS enum");
            dns = FeatureContracts.Dns(true, "1.1.1.1\n2606:4700:4700::1111", false); Assert(J.List(J.Get(dns, "dns", "custom", "ips")).Count == 2, "mixed IPv4/IPv6 custom DNS payload");
            invalid = false; try { FeatureContracts.Dns(true, "not-an-ip", false); } catch (ArgumentException) { invalid = true; } Assert(invalid, "invalid DNS server rejected");
            object dhcp = FeatureContracts.Dhcp("bridge", "", "", "", "", true); Assert(J.S(dhcp, "connection", "mode") == "bridged", "API bridge mode serializes as bridged");
            dhcp = FeatureContracts.Dhcp("custom", "192.168.4.0", "255.255.255.0", "192.168.4.10", "192.168.4.200", false); Assert(J.S(dhcp, "dhcp", "custom", "start_ip") == "192.168.4.10", "manual DHCP payload");
            invalid = false; try { FeatureContracts.Dhcp("custom", "192.168.4.0", "255.255.255.0", "192.168.5.10", "192.168.5.20", false); } catch (ArgumentException) { invalid = true; } Assert(invalid, "DHCP addresses outside subnet rejected");
            invalid = false; try { FeatureContracts.Dhcp("custom", "192.168.4.0", "255.0.255.0", "192.168.4.10", "192.168.4.20", false); } catch (ArgumentException) { invalid = true; } Assert(invalid, "noncontiguous subnet mask rejected");
            object schedule = FeatureContracts.Schedule("Bedtime", new[] { "MON", "TUE" }, "22:00", "07:00", true); Assert(J.S(schedule, "start") == "22:00" && J.List(J.Get(schedule, "days")).Count == 2, "API scheduled-pause fields and overnight times");
            object power = FeatureContracts.Schedule("Night", new[] { "MON" }, "22:00", "07:00", true, true); Assert(J.S(power, "start_time") == "22:00" && J.Get(power, "start") == null, "power schedules use distinct start_time/end_time fields");
            invalid = false; try { FeatureContracts.Schedule("Bedtime", new string[0], "22:00", "07:00", true); } catch (ArgumentException) { invalid = true; } Assert(invalid, "empty schedule days rejected");
            invalid = false; try { FeatureContracts.Schedule("Bedtime", new[] { "MON" }, "22:00", "22:00", true); } catch (ArgumentException) { invalid = true; } Assert(invalid, "ambiguous zero-length schedule rejected");
            Assert(FeatureContracts.Domain("Example.COM.") == "example.com", "website rule normalization");
            invalid = false; try { FeatureContracts.Domain("https://example.com/path"); } catch (ArgumentException) { invalid = true; } Assert(invalid, "website rules reject URLs and paths");
            string qr = FeatureContracts.WifiQr("Home;Wifi", "pass:word\\test"); Assert(qr == "WIFI:T:WPA;S:Home\\;Wifi;P:pass\\:word\\\\test;;", "wifi QR payload escapes reserved characters");
            using (var generator = new QRCoder.QRCodeGenerator()) using (var data = generator.CreateQrCode(qr, QRCoder.QRCodeGenerator.ECCLevel.Q)) using (var png = new QRCoder.PngByteQRCode(data)) { byte[] bytes = png.GetGraphic(4); Assert(bytes.Length > 100 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71, "local wifi QR generation produces PNG"); }
            Assert(FeatureCatalog.Entries.Select(e => e.Path).Distinct().Count() == FeatureCatalog.Entries.Length, "feature paths are unique");
            Assert(FeatureCatalog.Parent("Settings/Advanced networking/DNS") == "Settings/Advanced networking", "settings breadcrumb");
            Assert(FeatureCatalog.Root("Home/Profiles/Profile") == "Home", "profile remains grouped under Home");
            Assert(EeroApi.Resource("/2.4/networks/1/placement/wifi_coverage_scan").Scheme == "https", "coverage scan API version allowed only on trusted host");
        }
        private static void UiChecks()
        {
            if (System.Windows.Application.Current == null) new System.Windows.Application();
            var window = new MainWindow(true);
            foreach (string theme in new[] { "Light", "Dark" })
            {
                UiPreferences.Theme = theme; Program.ApplyTheme();
                foreach (string path in new[] { "Home", "Devices", "Activity", "Settings", "Help", "Home/Profiles/Profile", "Home/eeros/Details" }.Concat(FeatureCatalog.Entries.Select(e => e.Path)))
                {
                    window.PreviewPage(path);
                    var surface = (System.Windows.FrameworkElement)window.Content;
                    surface.Measure(new System.Windows.Size(1260, 820)); surface.Arrange(new System.Windows.Rect(0, 0, 1260, 820)); surface.UpdateLayout();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(delegate { }));
                    Assert(surface.ActualWidth > 0 && surface.ActualHeight > 0, "render " + theme + " " + path);
                    if (theme == "Dark") Assert(!ThemeVerification.HasWhiteControlSurface(surface), "no white fallback controls on " + path);
                }
            }
            window.Close();
            foreach (string theme in new[] { "Light", "Dark" })
            {
                UiPreferences.Theme = theme; Program.ApplyTheme(); var chat = MainWindow.ChatSample();
                var surface = (System.Windows.FrameworkElement)chat.Content; surface.Measure(new System.Windows.Size(960, 780)); surface.Arrange(new System.Windows.Rect(0, 0, 960, 780)); surface.UpdateLayout();
                Assert(surface.ActualWidth > 0, "native chat window renders in " + theme);
                if (theme == "Dark") Assert(!ThemeVerification.HasWhiteControlSurface(surface), "chat composer, feedback and permission controls have no white fallback");
                if (theme == "Dark") Assert(!ThemeVerification.HasWhiteCardSurface(surface), "chat messages and permission cards remain dark with readable text");
                chat.Close();
            }
        }
        private static Step Login()
        { return new Step { Method = "POST", Path = "/2.2/login", Body = "login=person%40example.com", ContentType = "application/x-www-form-urlencoded", Json = "{\"meta\":{\"code\":200},\"data\":{\"user_token\":\"pending-test\"}}" }; }
        private static async Task RunAsync(string scratch)
        {
            var store = new SessionStore(System.IO.Path.Combine(scratch, "session.dat"));
            var fake = new Fake();
            fake.Steps.Enqueue(Login());
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/login/verify", Cookie = "s=pending-test", Body = "code=123456", ContentType = "application/x-www-form-urlencoded", SetCookie = "s=verified-test; Path=/; HttpOnly; Secure" });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=verified-test", Json = "{\"meta\":{\"code\":200},\"data\":{\"networks\":{\"data\":[{\"name\":\"Home\",\"url\":\"/2.2/networks/1\"}]}}}" });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/networks/1", Cookie = "s=verified-test", Status = 401, Json = "{\"meta\":{\"code\":401,\"error\":\"error.session.refresh\"}}" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/login/refresh", Cookie = "s=verified-test", Json = "{\"meta\":{\"code\":200},\"data\":{\"user_token\":\"refreshed-test\"}}" });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/networks/1", Cookie = "s=refreshed-test", Json = "{\"meta\":{\"code\":200},\"data\":{\"name\":\"Home\"}}" });
            fake.Steps.Enqueue(new Step { Method = "PUT", Path = "/2.2/networks/1/devices/aa", Cookie = "s=refreshed-test", Body = "{\"paused\":true}", ContentType = "application/json" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/networks/1/speedtest", Cookie = "s=refreshed-test", Status = 202, Json = "{\"meta\":{\"code\":202}}" });
            fake.Steps.Enqueue(new Step { Method = "PUT", Path = "/2.2/eeros/1/led", Cookie = "s=refreshed-test", Body = "led_on=true", ContentType = "application/x-www-form-urlencoded" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/logout", Cookie = "s=refreshed-test", Status = 503, Json = "" });
            using (var api = new EeroApi(store, fake))
            {
                api.Remember = true;
                await api.BeginLogin("person@example.com"); Assert(api.Pending && !api.SignedIn, "pending login is not authenticated");
                Assert(!File.Exists(store.FilePath), "unverified token never saved");
                await api.Verify("123456"); Assert(api.SignedIn && !api.Pending, "verification promotes session");
                Assert(store.Load() == "verified-test", "DPAPI session round trip");
                Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(store.FilePath)).Contains("verified-test"), "saved token not plaintext");
                object account = await api.Send("GET", "account"); Assert(J.List(J.Get(account, "networks")).Count == 1, "network envelope parsing");
                object network = await api.Send("GET", "/2.2/networks/1"); Assert(J.S(network, "name") == "Home", "expired session refresh and retry");
                Assert(store.Load() == "refreshed-test", "refreshed token persisted");
                await api.Send("PUT", "/2.2/networks/1/devices/aa", J.Obj("paused", true));
                Assert(await api.Send("POST", "/2.2/networks/1/speedtest") == null, "accepted speed test without data");
                await api.Send("PUT", "/2.2/eeros/1/led", J.Obj("led_on", true), true);
                try { await api.Logout(); } catch (ApiException) { }
                Assert(!api.SignedIn && !File.Exists(store.FilePath), "logout clears local credentials even on server failure");
                Assert(fake.Steps.Count == 0, "complete authentication and management sequence");
            }
            fake = new Fake(); fake.Steps.Enqueue(Login());
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/login/verify", Cookie = "s=pending-test", Body = "code=000000", Status = 401, Json = "{\"meta\":{\"code\":401,\"error\":\"error.code.invalid\"}}" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/login/resend", Cookie = "s=pending-test" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/login/verify", Cookie = "s=pending-test", Body = "code=123456" });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=pending-test", Status = 429, Json = "{\"meta\":{\"code\":429}}" });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=pending-test", Status = 401, Json = "{\"meta\":{\"code\":401,\"error\":\"error.session.expired\"}}" });
            using (var api = new EeroApi(store, fake))
            {
                await Throws<InvalidOperationException>(() => api.Verify("123456"), "verification requires pending challenge");
                await api.BeginLogin("person@example.com");
                await Throws<ApiException>(() => api.Verify("000000"), "wrong code rejected");
                Assert(api.Pending && !api.SignedIn, "wrong code preserves challenge");
                await api.Resend(); await api.Verify("123456");
                Assert(api.SignedIn, "verification supports existing user cookie"); Assert(!File.Exists(store.FilePath), "remember off leaves no credentials on disk");
                await Throws<ApiException>(() => api.Send("GET", "account"), "rate limit surfaced without auto retry");
                try { await api.Send("GET", "account"); throw new Exception("Expected expiration"); }
                catch (ApiException e) { Assert(e.SessionExpired, "expired session flagged for sign in"); }
                Assert(fake.Steps.Count == 0, "no unexpected retry or logout requests");
            }
            fake = new Fake();
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/consents/account", Query = "?country=US", Json = "{\"meta\":{\"code\":200},\"data\":[{\"name\":\"terms\",\"requires_consent\":true}]}" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/register", Body = "name=Test+Person&email=person%40example.com&phone=%2B12025550123&send_verification=true", ContentType = "application/x-www-form-urlencoded", Json = "{\"meta\":{\"code\":200},\"data\":{\"user_token\":\"register-pending\"}}" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/register/phone/resend", Cookie = "s=register-pending" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/register/verify", Cookie = "s=register-pending", Body = "{\"code\":\"123456\",\"country\":\"US\",\"consents\":{\"terms\":true}}", ContentType = "application/json", SetCookie = "s=registered-test; Path=/; Secure; HttpOnly" });
            using (var api = new EeroApi(store, fake))
            {
                object agreements = await api.RegistrationConsents("US"); Assert(J.List(agreements).Count == 1, "registration loads country-specific consent metadata without credentials");
                await api.BeginRegistration("Test Person", "person@example.com", "+12025550123"); Assert(api.Registering && api.Pending && !api.SignedIn, "registration is separate from authenticated login");
                await Throws<InvalidOperationException>(() => api.Verify("123456"), "registration code cannot be sent to normal login verification");
                await api.Resend(); await api.VerifyRegistration("123456", "US", J.Obj("terms", true));
                Assert(api.SignedIn && !api.Registering && !api.Pending && fake.Steps.Count == 0, "registration completes only after code and explicit consents");
            }
            Assert(EeroApi.Resource("account").AbsolutePath == "/2.2/account", "relative resource resolution");
            Assert(EeroApi.Resource("/2.3/networks/1").Host == "api-user.e2ro.com", "new API version resource resolution");
            foreach (string url in new[] { "https://evil.example/2.2/account", "http://api-user.e2ro.com/2.2/account", "//evil.example/2.2/account", "https://api-user.e2ro.com:444/2.2/account", "https://user@api-user.e2ro.com/2.2/account", "/2.2/../../outside", "https://api-user.e2ro.com/2.2/account#secret" })
            {
                bool rejected = false; try { EeroApi.Resource(url); } catch (ArgumentException) { rejected = true; }
                Assert(rejected, "reject untrusted URL " + url);
            }
            MainWindow.ValidateSsid("Home"); MainWindow.ValidatePassword("password123");
            Assert(PhoneInput.FormatForDisplay("2025550123") == "+1 (202) 555-0123", "10-digit phone auto formatting");
            Assert(PhoneInput.FormatForDisplay("12025550123") == "+1 (202) 555-0123", "11-digit phone auto formatting");
            Assert(PhoneInput.FormatForDisplay("+12025550123") == "+1 (202) 555-0123", "international +1 formatting");
            Assert(PhoneInput.Normalize("+1 (202) 555-0123") == "+12025550123", "formatted phone normalizes for API");
            Assert(PhoneInput.FormatForDisplay("20255") == "20255", "partial phone remains editable");
            Assert(PhoneInput.FormatForDisplay("+1 (202) 555-012") == "+1 (202) 555-012", "backspace does not restore deleted digits");
            Assert(PhoneInput.Normalize("+44 (20) 7946 0958") == "+442079460958", "international country code preserved");
            Assert(PhoneInput.FormatForDisplay("person@example.com") == "person@example.com", "email not modified by phone formatter");
            var blocked = new HttpRequestException("Request failed", new WebException("Connect failed", new System.Net.Sockets.SocketException(10013), WebExceptionStatus.ConnectFailure, null));
            Assert(ConnectionDiagnostics.Category(blocked) == "network_access_blocked" && ConnectionDiagnostics.NativeCode(blocked) == 10013, "sandbox network access denied identified");
            Assert(MainWindow.Message(blocked).Contains("File Explorer"), "blocked network message explains normal desktop launch");
            Assert(ConnectionDiagnostics.Category(new HttpRequestException("Request failed", new System.Net.Sockets.SocketException(11001))) == "dns_failure", "DNS error distinguished from general connectivity");
            Assert(ConnectionDiagnostics.Category(new HttpRequestException("Request failed", new System.Security.Authentication.AuthenticationException("TLS failed"))) == "tls_failure", "TLS error distinguished from general connectivity");
            Assert(ConnectionDiagnostics.Category(new TaskCanceledException()) == "timeout", "timeout distinguished from general connectivity");
            Assert(PhoneInput.PreserveCaret("2025550123", 10, "+1 (202) 555-0123") == 17, "caret remains at end after phone formatting");
            Assert(PhoneInput.PreserveCaret("2025550123", 3, "+1 (202) 555-0123") == 9, "caret keeps following digits when editing middle");
            bool invalidPhone = false; try { PhoneInput.Normalize("+1 20255"); } catch (ArgumentException) { invalidPhone = true; } Assert(invalidPhone, "incomplete phone rejected before login");
            invalidPhone = false; try { PhoneInput.Normalize("2025550123123456789"); } catch (ArgumentException) { invalidPhone = true; } Assert(invalidPhone, "overlong phone is never silently truncated");
            bool bad = false; try { MainWindow.ValidateSsid(new string('界', 12)); } catch (ArgumentException) { bad = true; } Assert(bad, "SSID byte limit enforced");
            bad = false; try { MainWindow.ValidatePassword("short"); } catch (ArgumentException) { bad = true; } Assert(bad, "wifi password length enforced");
            store.Save("restored-test");
            using (var api = new EeroApi(store, new Fake())) { Assert(api.Restore() && api.Remember, "session restored between launches"); api.Forget(); }
            File.WriteAllBytes(store.FilePath, Encoding.UTF8.GetBytes("corrupt-session")); Assert(store.Load() == null, "corrupt credentials handled safely");
            results.Add("All checks completed using an injected HTTP handler. No live eero requests or real account credentials were used.");
        }
    }
}
