using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public static class SignInContracts
    {
        public static bool SsoReturn(Uri uri)
        { return uri.UserInfo.Length == 0 && (uri.Scheme == "https" && uri.Host == "app.eero.com" && uri.Port == 443 && uri.AbsolutePath == "/auth/index.html" || uri.Scheme == "eero" && uri.Host == "jumpto" && uri.AbsolutePath == "/auth"); }
        public static Dictionary<string, string> Parameters(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string item in query.TrimStart('?', '#').Split('&'))
            {
                if (item.Length == 0) continue;
                string[] pair = item.Split(new[] { '=' }, 2); string key = Uri.UnescapeDataString(pair[0].Replace("+", " "));
                if (result.ContainsKey(key)) throw new InvalidDataException("The sign-in response contains duplicate parameters.");
                result[key] = pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace("+", " ")) : "";
            }
            return result;
        }
        public static string ProviderUrl(string id)
        {
            if (!Regex.IsMatch(id ?? "", "^[a-zA-Z0-9_-]{1,128}$")) throw new ArgumentException("The provider identifier is invalid.");
            return "https://sso.eero.com/?idp=" + Uri.EscapeDataString(id) + "&redirect=" + Uri.EscapeDataString("https://app.eero.com/auth/index.html") + "&clientID=28971b56-0016-4416-bef8-689caac23011";
        }
    }
    public sealed class SignInService : IDisposable
    {
        private readonly HttpClient http;
        public SignInService(HttpMessageHandler handler = null)
        { http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }); http.Timeout = TimeSpan.FromSeconds(30); }
        public async Task<object> Providers()
        {
            using (var response = await http.GetAsync("https://sso.eero.com/api/v1/providers?assetType=mdpi").ConfigureAwait(false))
            { response.EnsureSuccessStatusCode(); string value = await response.Content.ReadAsStringAsync().ConfigureAwait(false); if (value.Length > 1024 * 1024) throw new InvalidDataException("The provider list exceeded its size limit."); return J.Parse(value); }
        }
        public void Dispose() { http.Dispose(); }
    }
    public sealed partial class MainWindow
    {
        private async Task CompleteExternalSignIn(Func<Task> action)
        {
            if (busy) throw new InvalidOperationException("Another sign-in operation is in progress. Wait for it to finish.");
            busy = true; if (controls != null) controls.IsEnabled = false;
            try { await action(); }
            finally { busy = false; if (controls != null) controls.IsEnabled = true; }
        }
        private async Task ProviderSignIn()
        {
            object data; using (var service = new SignInService()) data = await service.Providers();
            var choices = new Dictionary<string, string>();
            foreach (object provider in J.List(data)) { string name = J.S(provider, "name"), id = J.S(provider, "idp"); if (name.Length > 0 && id.Length > 0) choices[name] = id; }
            if (choices.Count == 0) throw new InvalidOperationException("eero did not return any sign-in providers.");
            Edit("Sign in through your internet provider", new[] { new Field("provider", "Provider", choices.Keys.OrderBy(x => x).First(), "choice", choices.Keys.OrderBy(x => x).ToArray()) }, async values =>
            {
                api.Remember = rememberBox == null || rememberBox.IsChecked == true;
                ServiceBrowserWindow window = null;
                window = new ServiceBrowserWindow("Sign in with " + V(values, "provider"), SignInContracts.ProviderUrl(choices[V(values, "provider")]), null, null, false, SignInContracts.SsoReturn, async uri =>
                {
                    await CompleteExternalSignIn(async delegate {
                    var query = SignInContracts.Parameters(uri.Query); string candidate;
                    if (!query.TryGetValue("token", out candidate) || candidate.Length == 0) throw new InvalidOperationException("The provider did not return an eero sign-in token.");
                    VerifiedBrowserIdentity identity = await api.InspectBrowserSession(candidate, window.LifetimeToken); object verified = identity.Account;
                    window.LifetimeToken.ThrowIfCancellationRequested();
                    if (!Confirm("Continue as " + J.First(verified, "name", "email", "phone") + "?\n" + J.First(verified, "email", "phone"))) throw new InvalidOperationException("Sign-in was cancelled.");
                    await api.ImportBrowserSession(identity.SessionCredential, window.LifetimeToken); ClearData(); await LoadAccount(); ShowShell(); SetNotice("Provider sign-in verified with eero."); });
                }, true) { Owner = this }; window.Show(); await Task.FromResult(0);
            }, "Continue", "Choose the provider associated with your eero account. Credentials are entered only on its sign-in page.");
        }
    }
}
