using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace EeroDesktop
{
    public static class J
    {
        public static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 100 };
        private static readonly object JsonSync = new object();
        public static Dictionary<string, object> Obj(params object[] pairs)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i < pairs.Length; i += 2) d[(string)pairs[i]] = pairs[i + 1];
            return d;
        }
        public static object Get(object o, params string[] keys)
        {
            foreach (string k in keys)
            {
                var d = o as IDictionary<string, object>;
                if (d == null || !d.TryGetValue(k, out o)) return null;
            }
            return o;
        }
        public static string S(object o, params string[] keys)
        {
            object v = keys.Length == 0 ? o : Get(o, keys);
            return v == null ? "" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }
        public static bool B(object o, params string[] keys) { return S(o, keys).Equals("true", StringComparison.OrdinalIgnoreCase); }
        public static List<object> List(object o)
        {
            var d = o as IDictionary<string, object>;
            if (d != null) return d.ContainsKey("data") ? List(d["data"]) : new List<object>();
            var a = o as IEnumerable;
            if (a == null || o is string) return new List<object>();
            return a.Cast<object>().ToList();
        }
        public static string First(object o, params string[] keys)
        {
            foreach (string k in keys) { string s = S(o, k); if (s.Length > 0) return s; }
            return "";
        }
        public static object Parse(string s) { lock (JsonSync) return Serializer.DeserializeObject(s); }
        public static string Stringify(object o) { lock (JsonSync) return Serializer.Serialize(o); }
    }

    public sealed class ApiException : Exception
    {
        public readonly int Code;
        public readonly string ApiError;
        public readonly bool SessionExpired;
        public ApiException(int code, string error, bool session) : base(Friendly(code, error))
        { Code = code; ApiError = error; SessionExpired = session && (code == 401 || code == 419); }
        private static string Friendly(int code, string error)
        {
            string detail = Regex.IsMatch(error ?? "", @"^[a-zA-Z0-9_.-]{1,140}$") ? " (" + error + ")" : "";
            if (code == 401 || code == 419) return "Your code or session was not accepted. Try again or request a new code." + detail;
            if (code == 429) return "eero is limiting requests. Wait a few minutes before trying again.";
            if (code == 402) return "This feature requires an active eero subscription.";
            if (code == 403) return "Your account does not have permission for this action." + detail;
            if (code == 404 || code == 405) return "This feature is not available for this network or API version.";
            if (code >= 500) return "eero's service could not complete the request. Try again later.";
            return "eero rejected the request (" + code + ")." + detail;
        }
    }

    public sealed class SessionStore
    {
        public readonly string FilePath;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EeroDesktop.Session.v1");
        public SessionStore(string path) { FilePath = path; }
        public void Save(string token)
        {
            byte[] clear = Encoding.UTF8.GetBytes(token);
            byte[] encrypted;
            try { encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser); }
            finally { Array.Clear(clear, 0, clear.Length); }
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, encrypted);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
        public string Load()
        {
            if (!File.Exists(FilePath)) return null;
            try
            {
                byte[] clear = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
                try { return Encoding.UTF8.GetString(clear); }
                finally { Array.Clear(clear, 0, clear.Length); }
            }
            catch (CryptographicException) { Clear(); return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        public void Clear() { if (File.Exists(FilePath)) File.Delete(FilePath); }
    }

    public sealed class VerifiedBrowserIdentity
    {
        public object Account { get; private set; }
        internal readonly string SessionCredential;
        internal VerifiedBrowserIdentity(object account, string credential) { Account = account; SessionCredential = credential; }
    }
    public sealed partial class EeroApi : IDisposable
    {
        public const string Origin = "https://api-user.e2ro.com";
        private readonly HttpClient http;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly SessionStore store;
        private string pending;
        private bool registering;
        private string token;
        public bool Remember;
        public bool SignedIn { get { return !String.IsNullOrEmpty(token); } }
        public bool Pending { get { return !String.IsNullOrEmpty(pending); } }
        public bool Registering { get { return registering; } }
        public EeroApi(SessionStore session, HttpMessageHandler handler = null)
        {
            store = session;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("EeroDesktop/1.0.0 (Windows; unofficial)");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }
        public bool Restore() { string saved = store.Load(); try { token = saved == null ? null : ValidToken(saved); } catch (InvalidDataException) { store.Clear(); token = null; } Remember = SignedIn; return SignedIn; }
        public async Task CheckConnection()
        {
            try { await Raw("GET", "account", null, null, false, false).ConfigureAwait(false); }
            catch (ApiException e) { if (e.Code == 401) return; throw; }
            throw new InvalidDataException("eero returned an unexpected response to the connection check.");
        }
        public static Uri Resource(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("The server did not provide a resource URL.");
            Uri uri;
            if (path.StartsWith("/")) uri = new Uri(new Uri(Origin), path);
            else if (path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) uri = new Uri(path);
            else if (path.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || path.Contains("://")) throw new ArgumentException("Untrusted API resource URL.");
            else uri = new Uri(Origin + "/2.2/" + path.TrimStart('/'));
            if (uri.Scheme != "https" || uri.Host != "api-user.e2ro.com" || uri.Port != 443 || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0 || !(uri.AbsolutePath.StartsWith("/2.2/") || uri.AbsolutePath.StartsWith("/2.3/") || uri.AbsolutePath.StartsWith("/2.4/")))
                throw new ArgumentException("Untrusted API resource URL.");
            return uri;
        }
        private static string ValidToken(string t)
        {
            if (t != null && t.StartsWith("eero-web-", StringComparison.Ordinal)) throw new InvalidDataException("This retired preview session requires a fresh email or phone sign-in.");
            if (String.IsNullOrEmpty(t) || t.Length > 8192 || t.Any(c => c <= 32 || c >= 127 || c == ';' || c == ',')) throw new InvalidDataException("eero returned an invalid session credential.");
            return t;
        }
        private void Commit(string t)
        {
            token = ValidToken(t);
            if (Remember) store.Save(token);
            else store.Clear();
        }
        private sealed class Reply { public object Data; public string Cookie; }
        private async Task<Reply> Raw(string method, string path, object body, string credential, bool form, bool session, CancellationToken cancellation = default(CancellationToken))
        {
            using (var req = new HttpRequestMessage(new HttpMethod(method), Resource(path)))
            {
                if (!String.IsNullOrEmpty(credential)) req.Headers.Add("Cookie", "s=" + ValidToken(credential));
                if (body != null)
                {
                    if (form)
                        req.Content = new FormUrlEncodedContent(((IDictionary<string, object>)body).Select(p => new KeyValuePair<string, string>(p.Key, p.Value is bool ? ((bool)p.Value ? "true" : "false") : J.S(p.Value))));
                    else req.Content = new StringContent(J.Stringify(body), Encoding.UTF8, "application/json");
                }
                using (HttpResponseMessage response = await http.SendAsync(req, cancellation).ConfigureAwait(false))
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    object envelope = null;
                    if (!String.IsNullOrWhiteSpace(text))
                    {
                        try { envelope = J.Parse(text); }
                        catch (ArgumentException) { if (response.IsSuccessStatusCode) throw new InvalidDataException("eero returned an unexpected response. Please try again later."); }
                    }
                    int httpCode = (int)response.StatusCode;
                    int code = httpCode;
                    int parsed;
                    if (Int32.TryParse(J.S(envelope, "meta", "code"), out parsed)) code = parsed;
                    if (!response.IsSuccessStatusCode || code < 200 || code >= 300)
                        throw new ApiException(response.IsSuccessStatusCode ? code : httpCode, J.S(envelope, "meta", "error"), session);
                    string cookie = null;
                    IEnumerable<string> cookies;
                    if (response.Headers.TryGetValues("Set-Cookie", out cookies))
                        foreach (string s in cookies)
                        {
                            var m = Regex.Match(s, @"(?:^|,\s*)s=([^; ,]+)");
                            if (m.Success) cookie = ValidToken(m.Groups[1].Value);
                        }
                    return new Reply { Data = J.Get(envelope, "data"), Cookie = cookie };
                }
            }
        }
        public async Task BeginLogin(string identifier)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                pending = null;
                registering = false;
                token = null;
                store.Clear();
                Reply reply = await Raw("POST", "login", J.Obj("login", identifier), null, true, false).ConfigureAwait(false);
                pending = ValidToken(J.S(reply.Data, "user_token"));
            }
            finally { gate.Release(); }
        }
        public async Task Verify(string code)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!Pending) throw new InvalidOperationException("Request a verification code first.");
                if (registering) throw new InvalidOperationException("Use account registration verification for this code.");
                Reply reply = await Raw("POST", "login/verify", J.Obj("code", code), pending, true, false).ConfigureAwait(false);
                Commit(reply.Cookie ?? J.First(reply.Data, "user_token", "session_token").NullIfEmpty() ?? pending);
                pending = null;
            }
            finally { gate.Release(); }
        }
        public void CancelLogin() { pending = null; registering = false; }
        public async Task ImportBrowserSession(string credential, CancellationToken cancellation = default(CancellationToken))
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try { string candidate = ValidToken(credential); Reply verified = await Raw("GET", "account", null, candidate, false, true, cancellation).ConfigureAwait(false); cancellation.ThrowIfCancellationRequested(); Commit(verified.Cookie ?? candidate); pending = null; registering = false; }
            finally { gate.Release(); }
        }
        public async Task<VerifiedBrowserIdentity> InspectBrowserSession(string credential, CancellationToken cancellation = default(CancellationToken))
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try { string candidate = ValidToken(credential); Reply reply = await Raw("GET", "account", null, candidate, false, true, cancellation).ConfigureAwait(false); return new VerifiedBrowserIdentity(reply.Data, reply.Cookie ?? candidate); }
            finally { gate.Release(); }
        }
        public async Task<object> AmazonSignIn(string accessToken, CancellationToken cancellation = default(CancellationToken))
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                Reply login = await Raw("POST", "login/amazon", J.Obj("auth_token", ValidToken(accessToken), "create_account", false), null, true, false, cancellation).ConfigureAwait(false);
                string candidate = login.Cookie ?? ValidToken(J.S(login.Data, "user_token"));
                Reply verified = await Raw("GET", "account", null, candidate, false, true, cancellation).ConfigureAwait(false);
                if (verified.Data == null || J.First(verified.Data, "user_id", "id", "url").Length == 0 && J.Get(verified.Data, "email") == null) throw new InvalidDataException("eero did not return a valid account identity.");
                cancellation.ThrowIfCancellationRequested();
                Commit(verified.Cookie ?? candidate); pending = null; registering = false; return verified.Data;
            }
            finally { gate.Release(); }
        }
        public async Task<object> RegistrationConsents(string country)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try { return (await Raw("GET", "consents/account?country=" + Uri.EscapeDataString(country), null, null, false, false).ConfigureAwait(false)).Data; }
            finally { gate.Release(); }
        }
        public async Task BeginRegistration(string name, string email, string phone)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                pending = null; token = null; registering = false; store.Clear();
                Reply r = await Raw("POST", "register", J.Obj("name", name, "email", email, "phone", phone, "send_verification", true), null, true, false).ConfigureAwait(false);
                pending = ValidToken(J.S(r.Data, "user_token")); registering = true;
            }
            finally { gate.Release(); }
        }
        public async Task VerifyRegistration(string code, string country, IDictionary<string, object> consents)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!Pending || !registering) throw new InvalidOperationException("Start account creation first.");
                Reply r = await Raw("POST", "register/verify", J.Obj("code", code, "country", country, "consents", consents), pending, false, false).ConfigureAwait(false);
                Commit(r.Cookie ?? J.First(r.Data, "user_token", "session_token").NullIfEmpty() ?? pending); pending = null; registering = false;
            }
            finally { gate.Release(); }
        }
        public async Task Resend()
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!Pending) throw new InvalidOperationException("Request a verification code first.");
                Reply r = await Raw("POST", registering ? "register/phone/resend" : "login/resend", null, pending, false, false).ConfigureAwait(false);
                string updated = r.Cookie ?? J.S(r.Data, "user_token").NullIfEmpty();
                if (updated != null) pending = ValidToken(updated);
            }
            finally { gate.Release(); }
        }
        public async Task<object> Send(string method, string path, object body = null, bool form = false)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!SignedIn) throw new ApiException(401, "session.missing", true);
                Reply r = null;
                bool refresh = false;
                try { r = await Raw(method, path, body, token, form, true).ConfigureAwait(false); }
                catch (ApiException ex)
                {
                    if (ex.Code != 401 || ex.ApiError != "error.session.refresh") throw;
                    refresh = true;
                }
                if (refresh)
                {
                    Reply fresh = await Raw("POST", "login/refresh", null, token, false, true).ConfigureAwait(false);
                    Commit(fresh.Cookie ?? J.S(fresh.Data, "user_token").NullIfEmpty() ?? token);
                    r = await Raw(method, path, body, token, form, true).ConfigureAwait(false);
                }
                if (r.Cookie != null) Commit(r.Cookie);
                return r.Data;
            }
            finally { gate.Release(); }
        }
        public async Task Logout()
        {
            try { if (SignedIn) await Send("POST", "logout").ConfigureAwait(false); }
            catch (ApiException) { }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            finally { Forget(); }
        }
        public void Forget() { token = null; pending = null; registering = false; Remember = false; store.Clear(); }
        public void Dispose() { http.Dispose(); gate.Dispose(); }
    }
    internal static class StringExtensions { public static string NullIfEmpty(this string s) { return String.IsNullOrEmpty(s) ? null : s; } }
}
