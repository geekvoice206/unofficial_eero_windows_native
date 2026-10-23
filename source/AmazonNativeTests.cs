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
        private static Uri AmazonTestReturn(AmazonNativeAttempt attempt, string code = "test-code")
        {
            var fields = SignInContracts.Parameters(new Uri(attempt.SignInUrl).Query);
            return new Uri(fields["openid.return_to"] + "&openid.oa2.authorization_code=" + Uri.EscapeDataString(code) + "&openid.oa2.scope=device_auth_access");
        }
        private static async Task AmazonNativeChecks(string scratch)
        {
            Assert(AmazonNativeAttempt.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "consumer Amazon PKCE matches the published RFC 7636 vector");
            using (var attempt = new AmazonNativeAttempt(new ServiceFake())) using (var other = new AmazonNativeAttempt(new ServiceFake()))
            {
                var fields = SignInContracts.Parameters(new Uri(attempt.SignInUrl).Query);
                var otherFields = SignInContracts.Parameters(new Uri(other.SignInUrl).Query);
                Assert(new Uri(attempt.SignInUrl).Host == "www.amazon.com" && fields["openid.oa2.response_type"] == "code" && fields["openid.oa2.scope"] == "device_auth_access", "consumer Amazon uses the APK's authorization-code scope on Amazon's own portal");
                Assert(!attempt.SignInUrl.Contains("insight") && !attempt.SignInUrl.Contains("client_secret") && !fields.ContainsKey("client_id"), "consumer Amazon does not require an Insight account or developer OAuth client");
                Assert(fields["openid.oa2.client_id"] != otherFields["openid.oa2.client_id"] && fields["openid.oa2.code_challenge"] != otherFields["openid.oa2.code_challenge"] && fields["openid.return_to"] != otherFields["openid.return_to"], "each sign-in has an independent virtual device, verifier and return nonce");
                object body = attempt.ExchangeBody(AmazonTestReturn(attempt));
                Assert(fields["openid.oa2.code_challenge"] == AmazonNativeAttempt.Challenge(J.S(body, "code_verifier")) && fields["openid.oa2.client_id"] == "device:" + J.S(body, "client_id"), "consumer token exchange is bound to the original device client and PKCE challenge");
                Assert(J.S(body, "client_domain") == "DeviceLegacy" && J.S(body, "code_algorithm") == "SHA-256" && J.S(body, "requested_token_type") == "access_token" && J.Get(body, "client_secret") == null, "consumer exchange uses the APK's token protocol without a client secret or device registration");
                string good = AmazonTestReturn(attempt).AbsoluteUri;
                foreach (string value in new[] { good.Replace("www.amazon.com", "www.amazon.com.evil.test"), good.Replace("https://", "http://"), good.Replace("/ap/maplanding", "/ap/other"), good + "&state=duplicate", good + "#fragment", good.Replace("device_auth_access", "profile"), good.Replace("test-code", "bad%0D%0Acode"), AmazonTestReturn(other).AbsoluteUri })
                    await Throws<InvalidDataException>(() => Task.FromResult(attempt.ExchangeBody(new Uri(value))), "consumer callback rejects address, nonce, duplicate-parameter, scope and code injection");
                foreach (string value in new[] { "http://www.amazon.com/", "https://www.amazon.com.evil.test", "https://user@www.amazon.com/", "https://www.amazon.com:8443/", "file:///C:/example", "https://insight.eero.com/" })
                    Assert(!AmazonNativeAttempt.Navigation(value), "consumer sign-in navigation rejects insecure, impersonated and unrelated origins");
            }
            var handler = new ServiceFake(); AmazonNativeAttempt exchange = null;
            handler.Steps.Enqueue(async request =>
            {
                Assert(request.Method == HttpMethod.Post && request.RequestUri.AbsoluteUri == AmazonNativeAttempt.TokenEndpoint, "consumer authorization code is exchanged only at Amazon's HTTPS token endpoint");
                Assert(!request.Headers.Contains("Cookie") && !request.Headers.Contains("Authorization") && request.Headers.GetValues("x-amzn-identity-auth-domain").Single() == "api.amazon.com", "Amazon token exchange receives no eero session, browser cookies or preexisting credentials");
                object body = J.Parse(await request.Content.ReadAsStringAsync());
                Assert(J.S(body, "source_token") == "test-code" && J.S(body, "source_token_type") == "authorization_code" && J.S(body, "app_name") == "EeroDesktop", "desktop identifies itself and exchanges only this attempt's returned code");
                return ServiceResponse(J.Obj("token_type", "bearer", "access_token", "test-amazon-access", "expires_in", 3600));
            });
            using (exchange = new AmazonNativeAttempt(handler))
            {
                Assert(await exchange.Exchange(AmazonTestReturn(exchange), CancellationToken.None) == "test-amazon-access", "consumer exchange accepts a valid bearer response");
                await Throws<InvalidOperationException>(() => exchange.Exchange(AmazonTestReturn(exchange), CancellationToken.None), "a consumer authorization code cannot be exchanged twice");
                Assert(handler.Steps.Count == 0, "single-use protection causes no repeated token request");
            }
            foreach (object result in new[] { J.Obj("token_type", "mac", "access_token", "test"), J.Obj("token_type", "bearer"), J.Obj("token_type", "bearer", "access_token", "bad\r\nCookie:bad") })
            {
                handler = new ServiceFake(); handler.Steps.Enqueue(request => Task.FromResult(ServiceResponse(result)));
                using (exchange = new AmazonNativeAttempt(handler)) await Throws<InvalidDataException>(() => exchange.Exchange(AmazonTestReturn(exchange), CancellationToken.None), "malformed Amazon token responses cannot become eero credentials");
            }
            handler = new ServiceFake(); handler.Steps.Enqueue(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Content = new StringContent("") }));
            using (exchange = new AmazonNativeAttempt(handler)) await Throws<ApiException>(() => exchange.Exchange(AmazonTestReturn(exchange), CancellationToken.None), "Amazon exchange redirects are rejected instead of forwarding the authorization code");
            handler = new ServiceFake(); handler.Steps.Enqueue(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":\"invalid_grant\",\"error_description\":\"test-sensitive-phrase\"}") }));
            using (exchange = new AmazonNativeAttempt(handler))
            {
                try { await exchange.Exchange(AmazonTestReturn(exchange), CancellationToken.None); throw new Exception("Expected rejected Amazon exchange."); }
                catch (ApiException error) { Assert(error.ApiError == "amazon.token.exchange.invalid_grant" && !error.Message.Contains("test-sensitive-phrase"), "Amazon errors retain only a known diagnostic category without echoing service response details"); }
            }
            var store = new SessionStore(Path.Combine(scratch, "amazon-native-session.dat")); store.Save("previous-session");
            var fake = new Fake();
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/login/amazon", Body = "auth_token=test-amazon-access&create_account=false", Json = "{\"data\":{\"user_token\":\"candidate-session\"}}" });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=candidate-session", Json = "{\"data\":{}}" });
            using (var api = new EeroApi(store, fake))
            {
                api.Restore(); await Throws<InvalidDataException>(() => api.AmazonSignIn("test-amazon-access"), "Amazon login refuses a success-shaped response without an actual eero account identity");
                Assert(api.SignedIn && store.Load() == "previous-session", "an incomplete Amazon account response preserves the previous verified session");
            }
            fake = new Fake();
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/login/amazon", Body = "auth_token=test-amazon-access&create_account=false", Json = "{\"data\":{\"user_token\":\"candidate-session\"}}" });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/account", Cookie = "s=candidate-session", SetCookie = "s=verified-session; Secure; HttpOnly", Json = "{\"data\":{\"user_id\":\"test-user\"}}" });
            using (var api = new EeroApi(store, fake))
            {
                api.Remember = true; object account = await api.AmazonSignIn("test-amazon-access");
                Assert(api.SignedIn && store.Load() == "verified-session" && J.S(account, "user_id") == "test-user" && fake.Steps.Count == 0, "Amazon login saves only the rotated eero session after verifying an existing account");
            }
            using (var cancel = new CancellationTokenSource())
            {
                handler = new ServiceFake(); handler.Steps.Enqueue(request => Task.FromResult(ServiceResponse(J.Obj("data", J.Obj("user_token", "candidate-session")))));
                handler.Steps.Enqueue(request => { cancel.Cancel(); return Task.FromResult(ServiceResponse(J.Obj("data", J.Obj("user_id", "test-user")))); });
                using (var api = new EeroApi(store, handler))
                {
                    api.Restore(); await Throws<OperationCanceledException>(() => api.AmazonSignIn("test-amazon-access", cancel.Token), "closing consumer Amazon sign-in cancels verification before committing its candidate session");
                    Assert(api.SignedIn && store.Load() == "verified-session", "cancelled consumer Amazon login preserves the remembered session");
                }
            }
        }
    }
}
