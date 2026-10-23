using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EeroDesktop
{
    internal static partial class Tests
    {
        private sealed class ServiceFake : HttpMessageHandler
        {
            public readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> Steps = new Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            { if (Steps.Count == 0) throw new Exception("Unexpected service request."); return Steps.Dequeue()(request); }
        }
        private sealed class WaitingChatStream : Stream
        {
            public readonly TaskCompletionSource<bool> Started = new TaskCompletionSource<bool>();
            private readonly TaskCompletionSource<int> read = new TaskCompletionSource<int>();
            public override bool CanRead { get { return true; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) { Started.TrySetResult(true); return read.Task; }
            protected override void Dispose(bool disposing) { read.TrySetException(new ObjectDisposedException("test chat stream")); base.Dispose(disposing); }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); } public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
        private static HttpResponseMessage ServiceResponse(object data)
        { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(J.Stringify(data), Encoding.UTF8, "application/json") }; }
        private static async Task ServiceIntegrationChecks(string scratch)
        {
            var retiredStore = new SessionStore(Path.Combine(scratch, "retired-web-session.dat"));
            retiredStore.Save("eero-web-v1.retired-preview"); var retiredHandler = new ServiceFake();
            using (var api = new EeroApi(retiredStore, retiredHandler))
            {
                Assert(!api.Restore() && !File.Exists(retiredStore.FilePath), "retired Insight credentials cannot be restored as native API cookies");
                await Throws<InvalidDataException>(() => api.ImportBrowserSession("eero-web-v1.retired-preview"), "retired web credentials are rejected before making native API requests");
                Assert(retiredHandler.Steps.Count == 0 && !api.SignedIn, "rejecting a retired preview credential makes no request and leaves the app signed out");
            }
            string json = "{\"type\":\"InitialMessage\",\"content\":\"héllo { \\\"braces\\\" } 😀\"}\n{\"type\":\"MessageDelta\",\"content\":\"tail\",\"finished\":true}";
            for (int chunk = 1; chunk <= 9; chunk++)
            {
                var parser = new JsonObjectStream(); var messages = new List<object>(); char[] chars = json.ToCharArray();
                for (int start = 0; start < chars.Length; start += chunk) { char[] part = chars.Skip(start).Take(chunk).ToArray(); parser.Feed(part, part.Length, messages.Add); }
                parser.Finish(); Assert(messages.Count == 2 && J.S(messages[0], "content").Contains("😀") && J.S(messages[1], "content") == "tail", "chat JSON framing across chunk size " + chunk);
            }
            bool invalid = false; try { var parser = new JsonObjectStream(); parser.Feed("{\"message\":".ToCharArray(), 11, x => { }); parser.Finish(); } catch (EndOfStreamException) { invalid = true; } Assert(invalid, "truncated chat stream is reported, not accepted as a complete reply");
            invalid = false; try { new JsonObjectStream().Feed("<html>".ToCharArray(), 6, x => { }); } catch (InvalidDataException) { invalid = true; } Assert(invalid, "HTML cannot masquerade as a chat stream");
            invalid = false; try { ConversationContracts.Request("network", null, new string('x', 8001)); } catch (ArgumentException) { invalid = true; } Assert(invalid, "chat message length is checked before sending");
            object chat = ConversationContracts.Request("network-1", "conversation-1", "Check connectivity", "test-mcp");
            Assert(J.S(chat, "context", "focused_network_id") == "network-1" && J.List(J.Get(chat, "context", "client_capabilities", "opt_in_widgets")).Count == 0, "chat sends network context and no unsupported opt-in widgets");
            object grant = ConversationContracts.Request("network-1", "conversation-1", null, null, J.Obj("approved", false, "interrupt_id", "permission-1"));
            Assert(!J.B(grant, "permission_grant", "approved") && J.S(grant, "permission_grant", "interrupt_id") == "permission-1", "declining a permission sends the exact interrupt ID without granting it");
            var store = new SessionStore(Path.Combine(scratch, "service-session.dat"));
            var fake = new Fake(); fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=stream-session", Json = "{\"meta\":{\"code\":200},\"data\":{}}" });
            fake.Steps.Enqueue(new Step { Method = "PUT", Path = "/2.2/conversations/messages", Cookie = "s=stream-session", Body = J.Stringify(chat), Json = json, SetCookie = "s=stream-rotated; Secure; HttpOnly" });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=stream-rotated", Json = "{\"data\":{}}" });
            using (var api = new EeroApi(store, fake))
            {
                await api.ImportBrowserSession("stream-session"); var values = new List<object>(); await api.StreamConversation(chat, values.Add, CancellationToken.None); await api.Send("GET", "account");
                Assert(values.Count == 2 && fake.Steps.Count == 0, "authenticated chat stream rotates its cookie and releases the API gate");
            }
            fake = new Fake(); fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=cancel-session" });
            fake.Steps.Enqueue(new Step { Method = "PUT", Path = "/2.2/conversations/messages", Cookie = "s=cancel-session", Json = json });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=cancel-session" });
            using (var api = new EeroApi(store, fake)) using (var cancel = new CancellationTokenSource())
            {
                await api.ImportBrowserSession("cancel-session"); int seen = 0;
                await Throws<OperationCanceledException>(async delegate { await api.StreamConversation(chat, x => { seen++; cancel.Cancel(); }, cancel.Token); }, "chat cancellation aborts delivery inside a buffered response");
                await api.Send("GET", "account"); Assert(seen == 1 && fake.Steps.Count == 0, "cancelled chat releases the API gate without delivering later events");
            }
            fake = new Fake(); fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=old-session" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/login/amazon", Body = "auth_token=test-amazon&create_account=false", ContentType = "application/x-www-form-urlencoded", Json = "{\"data\":{\"user_token\":\"candidate-amazon\"}}" });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=candidate-amazon", Status = 403, Json = "{\"meta\":{\"code\":403}}" });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=old-session" });
            using (var api = new EeroApi(store, fake))
            {
                await api.ImportBrowserSession("old-session"); await Throws<ApiException>(async delegate { await api.AmazonSignIn("test-amazon"); }, "Amazon sign-in is rejected if eero cannot verify the resulting account"); await api.Send("GET", "account");
                Assert(api.SignedIn && fake.Steps.Count == 0, "rejected Amazon login preserves the existing verified eero session");
            }
            Assert(SignInContracts.SsoReturn(new Uri("https://app.eero.com/auth/index.html?token=sample")) && !SignInContracts.SsoReturn(new Uri("https://app.eero.com.evil.test/auth/index.html?token=sample")), "provider sign-in callback is confined to eero's exact return endpoint");
            Assert(SignInContracts.ProviderUrl("provider-1").StartsWith("https://sso.eero.com/"), "provider sign-in starts at the official eero broker");
            foreach (string allowed in new[] { "https://bank.example.test/3ds", "https://provider.example.test/login" }) Assert(ServiceLinks.ProviderNavigation(allowed), "payment and identity providers may navigate to secure HTTPS pages");
            foreach (string denied in new[] { "http://bank.example.test/", "https://user@bank.example.test/", "javascript:alert(1)", "file:///C:/example", "https://bank.example.test:8443/" }) Assert(!ServiceLinks.ProviderNavigation(denied), "provider navigation rejects nonstandard or unsafe destinations");
            Assert(BillingContracts.Return(new Uri("eero://jumpto/pluspurchasecomplete")) && !BillingContracts.Return(new Uri("eero://jumpto/reset")) && !BillingContracts.Return(new Uri("https://app.eero.com.evil.test/pluspurchasecomplete")), "billing accepts only recognized completion callbacks and refreshes server status separately");
            string key = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());
            Assert(VpnContracts.CanRegister(J.Obj("seats_available", true)) && !VpnContracts.CanRegister(J.Obj("seats_available", false)), "VPN registration requires a reported available device slot");
            Assert(!VpnContracts.CanRegister(J.Obj()) && !VpnContracts.CanRegister(J.Obj("seats_available", "true")), "missing or malformed VPN eligibility never enables registration");
            Assert(VpnContracts.EligibilityMessage(J.Obj("seats_available", false)).Contains("device slots") && !VpnContracts.EligibilityMessage(J.Obj("seats_available", false)).Contains("does not have permission"), "full VPN device allowance is distinct from account permission denial");
            Assert(!VpnContracts.CanRegister(new ApiException(403, "", false)) && VpnContracts.EligibilityMessage(new ApiException(403, "", false)).Contains("provider"), "VPN entitlement rejection explains provider availability without offering registration");
            Assert(VpnContracts.EligibilityMessage(new ApiException(402, "", false)).Contains("eligible active benefit") && VpnContracts.EligibilityMessage(new ApiException(503, "", false)).Contains("Try again"), "VPN subscription and temporary service errors stay distinct");
            object profile = VpnContracts.Profile(key, "new-york-1.sgw.guardianapp.com", J.Obj("server-public-key", key, "mapped-ipv4-address", "10.20.0.2", "mapped-ipv6-address", "fd00::2"));
            string configuration = VpnContracts.Configuration(profile);
            Assert(configuration.Contains("AllowedIPs = 0.0.0.0/0, ::/0") && configuration.Contains("DNS = 1.1.1.1, 1.0.0.1") && configuration.Contains(":51821"), "Guardian WireGuard config covers both address families with SDK DNS and endpoint port");
            Assert(!configuration.Contains("PostUp") && !configuration.Contains("PreUp") && configuration.Contains("10.20.0.2/32, fd00::2/128"), "generated VPN config has validated addresses and no command hooks");
            foreach (string bad in new[] { "evil.test", "guardianapp.com.evil.test", "good.sgw.guardianapp.com\nPostUp=bad", "good.sgw.guardianapp.com:443" })
            { invalid = false; try { VpnContracts.Gateway(bad); } catch (InvalidDataException) { invalid = true; } Assert(invalid, "VPN gateway cannot redirect credentials or inject configuration"); }
            invalid = false; try { VpnContracts.Profile(key, "valid.sgw.guardianapp.com", J.Obj("server-public-key", key, "mapped-ipv4-address", "10.0.0.2/8")); } catch (InvalidDataException) { invalid = true; } Assert(invalid, "VPN server cannot inject a broad interface prefix");
            string scope = VpnContracts.Scope("account-1", "network-1"); Assert(scope != VpnContracts.Scope("account-2", "network-1") && scope != VpnContracts.Scope("account-1", "network-2") && WindowsVpn.Tunnel(scope).Length < 40, "VPN profile names isolate accounts and networks without exposing identifiers");
            byte[] bytes = Encoding.UTF8.GetBytes("non-secret DPAPI interoperability fixture"); byte[] encrypted = WindowsVpn.ProtectMachine(bytes, WindowsVpn.Tunnel(scope));
            Assert(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine).SequenceEqual(bytes), "WireGuard machine profile encryption uses interoperable DPAPI protection");
            invalid = false; try { VendorSignature.Verify(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch (InvalidDataException) { invalid = true; } Assert(invalid, "unsigned files cannot be executed as a verified WireGuard component");
            var serviceFake = new ServiceFake();
            serviceFake.Steps.Enqueue(async request =>
            {
                Assert(request.RequestUri.Host == VpnContracts.PartnerHost && !request.Headers.Contains("Cookie"), "Guardian receives no eero API cookie");
                object body = J.Parse(await request.Content.ReadAsStringAsync());
                Assert(J.S(body, "ep-grd-subscriber-identifier") == "sample-id" && J.S(body, "connect-publishable-key") == VpnContracts.PublishableKey, "Guardian device list uses partner subscriber fields and publishable routing key");
                return ServiceResponse(new[] { J.Obj("ep-grd-device-uuid", "device-1", "ep-grd-device-nickname", "Windows") });
            });
            using (var guardian = new GuardianApi(serviceFake))
            {
                object devices = await guardian.Devices(J.Obj("guardian_id", "sample-id", "guardian_secret", "sample-secret")); Assert(J.List(devices).Count == 1, "Guardian device list is parsed without exposing secrets in UI details");
                await Throws<InvalidDataException>(async delegate { await guardian.Send("POST", "evil.test", "/api/v1/test", J.Obj()); }, "Guardian API refuses off-provider credential destinations");
            }
            Assert(serviceFake.Steps.Count == 0, "all service test requests were exercised without any real account calls");
            serviceFake = new ServiceFake();
            serviceFake.Steps.Enqueue(async request =>
            {
                object body = J.Parse(await request.Content.ReadAsStringAsync());
                Assert(request.RequestUri.Host == VpnContracts.ServerHost && request.RequestUri.AbsolutePath == "/api/v1.3/servers/hostnames-for-region" && J.S(body, "region") == "test-city", "VPN provisioning selects an online server in the requested city");
                return ServiceResponse(new[] { J.Obj("hostname", "offline.sgw.guardianapp.com", "offline", true), J.Obj("hostname", "online.sgw.guardianapp.com", "offline", false, "capacity-score", 2) });
            });
            serviceFake.Steps.Enqueue(async request =>
            {
                object body = J.Parse(await request.Content.ReadAsStringAsync());
                Assert(request.RequestUri.AbsolutePath == "/api/v1.2/subscriber-credential/create" && J.S(body, "validation-method") == "pe-token" && J.S(body, "pe-token") == "test-pet", "VPN provisioning exchanges a device PE token for a subscriber credential");
                return ServiceResponse(J.Obj("subscriber-credential", "test-subscriber"));
            });
            serviceFake.Steps.Enqueue(async request =>
            {
                object body = J.Parse(await request.Content.ReadAsStringAsync());
                Assert(request.RequestUri.Host == "online.sgw.guardianapp.com" && request.RequestUri.AbsolutePath == "/api/v1.4/device-credentials" && J.S(body, "transport-protocol") == "wireguard" && J.S(body, "public-key") == key, "Guardian receives the public key on the selected gateway");
                Assert(J.Get(body, "private_key") == null && !request.Headers.Contains("Cookie"), "VPN private key and eero cookie are never sent to Guardian");
                return ServiceResponse(J.Obj("server-public-key", key, "mapped-ipv4-address", "10.20.0.2"));
            });
            using (var guardian = new GuardianApi(serviceFake))
            {
                object provisioned = await guardian.CreateProfile("test-pet", "test-city", key, key);
                Assert(J.S(provisioned, "gateway") == "online.sgw.guardianapp.com" && serviceFake.Steps.Count == 0, "complete simulated Guardian provisioning produces a validated Windows profile");
            }
            Assert(WindowsVpn.ElevatedControl("../invalid") == 2, "VPN helper rejects invalid request IDs before reading files or changing Windows");
            var inspectedStore = new SessionStore(Path.Combine(scratch, "inspected-session.dat")); fake = new Fake();
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=provider-candidate", SetCookie = "s=provider-rotated; Secure; HttpOnly", Json = "{\"data\":{\"name\":\"Sample person\"}}" });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=provider-rotated", SetCookie = "s=provider-final; Secure; HttpOnly" });
            using (var api = new EeroApi(inspectedStore, fake))
            {
                api.Remember = true; var identity = await api.InspectBrowserSession("provider-candidate");
                Assert(!api.SignedIn && !File.Exists(inspectedStore.FilePath) && J.S(identity.Account, "name") == "Sample person", "provider identity can be reviewed without signing in or saving credentials");
                Assert(!J.Stringify(identity).Contains("provider-rotated"), "reviewed identity does not serialize its session credential");
                await api.ImportBrowserSession(identity.SessionCredential); Assert(inspectedStore.Load() == "provider-final", "reviewed provider sign-in preserves cookie rotation through final verification");
            }
            inspectedStore.Save("preserved-session"); serviceFake = new ServiceFake();
            using (var cancelled = new CancellationTokenSource())
            {
                serviceFake.Steps.Enqueue(async request => { cancelled.Cancel(); await Task.FromResult(0); return ServiceResponse(J.Obj("data", J.Obj())); });
                using (var api = new EeroApi(inspectedStore, serviceFake))
                {
                    api.Restore(); await Throws<OperationCanceledException>(async delegate { await api.ImportBrowserSession("cancelled-candidate", cancelled.Token); }, "closing a sign-in window cancels candidate verification");
                    Assert(api.SignedIn && inspectedStore.Load() == "preserved-session", "cancelled alternate sign-in cannot replace the saved session");
                }
            }
            serviceFake = new ServiceFake(); var waiting = new WaitingChatStream();
            serviceFake.Steps.Enqueue(async request => { await Task.FromResult(0); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(waiting) }; });
            serviceFake.Steps.Enqueue(async request => { Assert(request.Method == HttpMethod.Get, "normal account refresh runs while chat is awaiting its next chunk"); await Task.FromResult(0); return ServiceResponse(J.Obj("data", J.Obj())); });
            using (var cancel = new CancellationTokenSource()) using (var api = new EeroApi(inspectedStore, serviceFake))
            {
                api.Restore(); Task streaming = api.StreamConversation(chat, x => { }, cancel.Token); await waiting.Started.Task;
                Task<object> refresh = api.Send("GET", "account"); Task first = await Task.WhenAny(refresh, Task.Delay(1500)); cancel.Cancel();
                try { await streaming; } catch (ObjectDisposedException) { } catch (OperationCanceledException) { }
                await refresh; Assert(first == refresh, "chat streaming does not lock out refresh or sign-out operations");
            }
            int probes = 0, stops = 0, confirmations = 0;
            VpnTunnelLifecycle.Start(() => { }, () => true, () => ++probes == 3, () => stops++, () => confirmations++, 5, () => { });
            Assert(probes == 3 && stops == 0 && confirmations == 1, "delayed VPN startup retries before confirming one fresh handshake");
            stops = confirmations = 0; invalid = false;
            try { VpnTunnelLifecycle.Start(() => { }, () => true, () => false, () => stops++, () => confirmations++, 3, () => { }); } catch (InvalidOperationException) { invalid = true; }
            Assert(invalid && stops == 1 && confirmations == 0, "VPN handshake failure removes the tunnel and cannot report connected");
            stops = 0; invalid = false;
            try { VpnTunnelLifecycle.Start(() => { throw new IOException("partial install"); }, () => true, () => true, () => stops++, () => { }, 1, () => { }); } catch (IOException) { invalid = true; }
            Assert(invalid && stops == 1, "partially installed VPN service is removed after an installation error");
            stops = 0; invalid = false;
            try { VpnTunnelLifecycle.Start(() => { throw new IOException("no install"); }, () => false, () => true, () => stops++, () => { }, 1, () => { }); } catch (IOException) { invalid = true; }
            Assert(invalid && stops == 0, "failed install without a service does not issue an unrelated stop");
            invalid = false;
            try { VpnTunnelLifecycle.Start(() => { }, () => true, () => false, () => { throw new IOException("stop failed"); }, () => { }, 1, () => { }); } catch (VpnCleanupException) { invalid = true; }
            Assert(invalid, "failed VPN cleanup is reported explicitly instead of claiming disconnected");
            stops = 0; invalid = false;
            try { VpnTunnelLifecycle.Start(() => { }, () => true, () => true, () => stops++, () => { throw new IOException("cannot save success"); }, 1, () => { }); } catch (IOException) { invalid = true; }
            Assert(invalid && stops == 1, "VPN rolls back if its successful connection cannot be reported to the app");
        }
    }
}
