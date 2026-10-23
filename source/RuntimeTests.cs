using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace EeroDesktop
{
    internal static partial class Tests
    {
        private static async Task RuntimeChecks(string scratch)
        {
            foreach (string url in new[] { "http://account.eero.com", "https://eero.com.attacker.example/", "https://user@account.eero.com/", "https://account.eero.com:444/", "file:///C:/test.html", "javascript:alert(1)", "https://127.0.0.1/" }) Assert(!ServiceLinks.Allowed(url), "service browser rejects " + url);
            Assert(ServiceLinks.Allowed("https://account.eero.com/") && ServiceLinks.Allowed("https://guardianapp.com/login?connect-api-env=wifi-company-prod"), "verified eero and Guardian services allowed");
            Assert(ServiceLinks.TokenHeader("test-temporary-token") == "x-temp-user-token: test-temporary-token\r\n", "API temporary service header");
            bool invalid = false; try { ServiceLinks.TokenHeader("token\r\nCookie: s=private"); } catch (InvalidDataException) { invalid = true; } Assert(invalid, "temporary token cannot inject another header");
            invalid = false; try { ServiceLinks.Eero("https://guardianapp.com/"); } catch (InvalidDataException) { invalid = true; } Assert(invalid, "temporary eero session target excludes partner hosts");
            Assert(ServiceLinks.KeepTemporaryHeader("https://account.eero.com/start?x=1", "https://account.eero.com/start?x=1"), "temporary header allowed only for initial target");
            Assert(!ServiceLinks.KeepTemporaryHeader("https://outside.example/start", "https://outside.example/start"), "temporary header cannot be initialized for an unrelated host");
            foreach (string redirected in new[] { "https://stripe.com/start?x=1", "https://account.eero.com/asset.js", "https://api-user.e2ro.com/2.2/account", "http://account.eero.com/start?x=1", "https://account.eero.com/start?x=2" }) Assert(!ServiceLinks.KeepTemporaryHeader("https://account.eero.com/start?x=1", redirected), "temporary header stripped from redirected or unrelated resource");
            Assert(J.Stringify(RuntimeContracts.AdminInvite()) == "{\"invite_role\":\"admin\"}", "admin invitation uses serialized API invite_role field");
            Assert(J.Stringify(RuntimeContracts.InvitationResponse("invite-7", false)) == "{\"invite_id\":\"invite-7\",\"accept\":false}", "member-promotion response uses invite_id and boolean acceptance");
            invalid = false; try { RuntimeContracts.InvitationResponse("../account", true); } catch (ArgumentException) { invalid = true; } Assert(invalid, "incoming invitation IDs cannot change a resource path");
            foreach (string mode in new[] { "OFF", "ZTS", "LTS", "DEFAULT" }) Assert(J.S(RuntimeContracts.AutoSetup(mode), "mode") == mode, "API automatic setup mode " + mode);
            invalid = false; try { RuntimeContracts.AutoSetup("enabled"); } catch (ArgumentException) { invalid = true; } Assert(invalid, "unrecognized automatic setup modes rejected");
            BluetoothEero ble = BluetoothEero.Advertisement(0, "3.0.0-1-AA==", -60);
            Assert(ble.InNetwork == true && ble.Ethernet == null, "BLE v3 reads membership without inventing an Ethernet flag");
            ble = BluetoothEero.Advertisement(0, "4.0.0-Model-1", -47);
            Assert(ble.InNetwork == null && ble.Ethernet == true && ble.Signal == -47, "BLE v4 reports Ethernet without inventing network membership");
            ble = BluetoothEero.Advertisement(0, "eero Setup", -70); Assert(ble.Version == null && ble.InNetwork == null && ble.Ethernet == null, "legacy setup advertisement remains unknown until read");
            ble = BluetoothEero.Advertisement(0, "malformed", -70); Assert(ble.Version == null, "malformed BLE name does not crash discovery");
            Assert(NativeBluetooth.EeroService == new Guid("d99a3d3f-c1ef-4533-809d-82de05d509ca"), "BLE scanner filters the API eero setup service");
            object create = RuntimeContracts.CreateNetwork("Test wifi", "sample-password", "America/Chicago", new Dictionary<string, object> { { "test_terms", true }, { "optional", false } });
            Assert(J.S(create, "timezone", "value") == "America/Chicago" && J.B(create, "settings", "consents", "test_terms") && !J.B(create, "settings", "consents", "optional"), "network creation preserves timezone and explicit consent decisions");
            invalid = false; try { RuntimeContracts.CreateNetwork("Test", "short", "America/Chicago", new Dictionary<string, object>()); } catch (ArgumentException) { invalid = true; } Assert(invalid, "invalid wifi password rejected before creating a network");

            var store = new SessionStore(Path.Combine(scratch, "runtime-session.dat")); store.Save("existing-test"); var fake = new Fake();
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=unverified-browser-test", Status = 401, Json = "{\"meta\":{\"code\":401,\"error\":\"error.session.invalid\"}}" });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=verified-browser-test", SetCookie = "s=rotated-browser-test; Path=/; Secure; HttpOnly" });
            using (var api = new EeroApi(store, fake))
            {
                api.Restore(); await Throws<ApiException>(() => api.ImportBrowserSession("unverified-browser-test"), "website sign-in is checked against account API");
                Assert(api.SignedIn && store.Load() == "existing-test", "rejected browser session preserves the existing saved sign-in");
                await api.ImportBrowserSession("verified-browser-test"); Assert(store.Load() == "rotated-browser-test", "verified browser session honors server cookie rotation");
                Assert(fake.Steps.Count == 0, "browser session verification completed without extra requests");
            }
            fake = new Fake(); store.Save("setup-test");
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.3/networks", Cookie = "s=setup-test", Body = J.Stringify(create), Json = "{\"meta\":{\"code\":200},\"data\":{\"url\":\"/2.2/networks/new-7\"}}" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/eeros", Query = "?requireSession=true", Cookie = "s=setup-test", Body = "serial=TESTSERIAL123&location=Office&network=%2F2.2%2Fnetworks%2Fnew-7", ContentType = "application/x-www-form-urlencoded", Status = 409, Json = "{\"meta\":{\"code\":409,\"error\":\"setup.not_ready\"}}" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/eeros", Query = "?requireSession=true", Cookie = "s=setup-test", Body = "serial=TESTSERIAL123&location=Office&network=%2F2.2%2Fnetworks%2Fnew-7", ContentType = "application/x-www-form-urlencoded" });
            using (var api = new EeroApi(store, fake))
            {
                api.Restore(); var setup = new WiredNetworkSetup(); await setup.Create(api, create, "TESTSERIAL123", "Office");
                await Throws<ApiException>(() => setup.Claim(api), "failed gateway addition remains recoverable");
                Assert(setup.CreatedUrl == "/2.2/networks/new-7" && !setup.CreationUncertain, "failed claim preserves the created network");
                await Throws<InvalidOperationException>(() => setup.Create(api, create, "TESTSERIAL123", "Office"), "claim failure cannot issue a second network creation");
                Assert(await setup.Claim(api) == "/2.2/networks/new-7" && setup.CreatedUrl == null, "retry claims the same network then clears completed setup state");
                Assert(fake.Steps.Count == 0, "wired setup sent exactly one creation and two explicit claims");
            }
            fake = new Fake(); fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.3/networks", Cookie = "s=setup-test", Status = 503, Json = "{\"meta\":{\"code\":503}}" });
            using (var api = new EeroApi(store, fake))
            {
                api.Restore(); var setup = new WiredNetworkSetup(); await Throws<ApiException>(() => setup.Create(api, create, "TESTSERIAL123", "Office"), "ambiguous network creation failure surfaced");
                Assert(setup.CreationUncertain, "uncertain creation remains blocked for account review");
                await Throws<InvalidOperationException>(() => setup.Create(api, create, "TESTSERIAL123", "Office"), "ambiguous create is never silently repeated");
            }
        }
    }
}
