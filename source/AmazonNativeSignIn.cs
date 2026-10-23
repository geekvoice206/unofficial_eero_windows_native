using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EeroDesktop
{
    // Protocol constants, not SDK code. See the local APK verification notes.
    internal sealed class AmazonNativeAttempt : IDisposable
    {
        internal const string TokenEndpoint = "https://api.amazon.com/auth/token";
        private const string DeviceType = "A2W01JEFAJFTXI";
        private readonly HttpClient http;
        private readonly string verifier, state, client;
        private int consumed;
        internal AmazonNativeAttempt(HttpMessageHandler handler = null)
        {
            verifier = Random(); state = Random();
            string serial = Guid.NewGuid().ToString("N").ToUpperInvariant();
            client = String.Concat(Encoding.UTF8.GetBytes(serial + "#" + DeviceType).Select(b => b.ToString("x2")));
            http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
            http.Timeout = TimeSpan.FromSeconds(30); http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            http.DefaultRequestHeaders.UserAgent.ParseAdd("EeroDesktop/1.0.0 (Windows; unofficial)");
        }
        private static string Base64Url(byte[] bytes) { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        private static string Random() { var bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return Base64Url(bytes); }
        internal static string Challenge(string value) { using (var hash = SHA256.Create()) return Base64Url(hash.ComputeHash(Encoding.ASCII.GetBytes(value))); }
        internal string SignInUrl
        {
            get
            {
                var fields = new Dictionary<string, string> {
                    { "openid.assoc_handle", "amzn_eero_mobile_android_us" }, { "pageId", "eero_mobile_accounts" }, { "language", "en_US" },
                    { "openid.ns", "http://specs.openid.net/auth/2.0" }, { "openid.mode", "checkid_setup" },
                    { "openid.claimed_id", "http://specs.openid.net/auth/2.0/identifier_select" }, { "openid.identity", "http://specs.openid.net/auth/2.0/identifier_select" },
                    { "openid.ns.pape", "http://specs.openid.net/extensions/pape/1.0" }, { "openid.pape.max_auth_age", "0" },
                    { "openid.ns.oa2", "http://www.amazon.com/ap/ext/oauth/2" }, { "openid.oa2.response_type", "code" },
                    { "openid.oa2.scope", "device_auth_access" }, { "openid.oa2.client_id", "device:" + client },
                    { "openid.oa2.code_challenge", Challenge(verifier) }, { "openid.oa2.code_challenge_method", "S256" },
                    { "openid.return_to", "https://www.amazon.com/ap/maplanding?state=" + state }, { "accountStatusPolicy", "P1" }
                };
                return "https://www.amazon.com/ap/signin?" + String.Join("&", fields.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)).ToArray());
            }
        }
        internal static bool ReturnAddress(Uri uri)
        { return uri != null && uri.Scheme == "https" && uri.Host == "www.amazon.com" && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/ap/maplanding" && uri.Fragment.Length == 0; }
        internal static bool Navigation(string value)
        {
            Uri uri; if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length > 0) return false;
            return new[] { "amazon.com", "amazon.co.uk", "amazon.ca", "amazon.de", "amazon.fr", "amazon.it", "amazon.es", "amazon.co.jp", "amazon.com.au", "amazon.in", "amazon.com.mx", "amazon.com.br" }
                .Any(host => uri.Host == host || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
        }
        internal object ExchangeBody(Uri returned)
        {
            if (!ReturnAddress(returned)) throw new InvalidDataException("Amazon returned to an unexpected address.");
            var query = SignInContracts.Parameters(returned.Query);
            string received, code;
            if (!query.TryGetValue("state", out received) || received != state) throw new InvalidDataException("The Amazon response did not match this sign-in attempt. Close this window and try again.");
            if (query.ContainsKey("error") || query.ContainsKey("openid.error")) throw new InvalidOperationException("Amazon sign-in was cancelled or rejected.");
            if (!query.TryGetValue("openid.oa2.authorization_code", out code) || code.Length == 0 || code.Length > 8192 || code.Any(c => c <= 32 || c >= 127)) throw new InvalidDataException("Amazon did not return a valid authorization code.");
            if (query.ContainsKey("openid.oa2.scope") && query["openid.oa2.scope"] != "device_auth_access") throw new InvalidDataException("Amazon returned an unexpected sign-in scope.");
            return J.Obj("app_name", "EeroDesktop", "app_version", "1.0.0", "source_token_type", "authorization_code", "source_token", code,
                "requested_token_type", "access_token", "code_verifier", verifier, "code_algorithm", "SHA-256", "client_id", client, "client_domain", "DeviceLegacy");
        }
        internal async Task<string> Exchange(Uri returned, CancellationToken cancellation)
        {
            object body = ExchangeBody(returned);
            if (Interlocked.Exchange(ref consumed, 1) != 0) throw new InvalidOperationException("This sign-in attempt has already been used. Close this window and start again.");
            using (var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint))
            {
                request.Headers.Add("x-amzn-identity-auth-domain", "api.amazon.com");
                request.Content = new StringContent(J.Stringify(body), Encoding.UTF8, "application/json");
                using (var response = await http.SendAsync(request, cancellation).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string category = "";
                        string failure = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (failure.Length < 65536)
                        {
                            try
                            {
                                string name = J.S(J.Parse(failure), "error").ToLowerInvariant();
                                if (new[] { "invalid_request", "invalid_grant", "invalid_client", "invalid_token", "unauthorized_client", "access_denied", "invalid_scope" }.Contains(name)) category = "." + name;
                            }
                            catch (ArgumentException) { }
                        }
                        throw new ApiException((int)response.StatusCode, "amazon.token.exchange" + category, false);
                    }
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (text.Length > 65536) throw new InvalidDataException("Amazon returned an oversized sign-in response.");
                    object result; try { result = J.Parse(text); } catch (ArgumentException) { throw new InvalidDataException("Amazon returned an unexpected sign-in response."); }
                    string token = J.S(result, "access_token");
                    if (!J.S(result, "token_type").Equals("bearer", StringComparison.OrdinalIgnoreCase) || token.Length == 0 || token.Length > 8192 || token.Any(c => c <= 32 || c >= 127 || c == ';' || c == ',')) throw new InvalidDataException("Amazon did not return a valid access token.");
                    cancellation.ThrowIfCancellationRequested(); return token;
                }
            }
        }
        public void Dispose() { http.Dispose(); }
    }

    internal sealed class AmazonNativeWindow : Window
    {
        private readonly AmazonNativeAttempt attempt = new AmazonNativeAttempt();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly WebView2 browser;
        private readonly TextBlock notice, address;
        private readonly Func<string, CancellationToken, Task> complete;
        private readonly Action<string, object> report;
        private bool completing, closed;
        internal AmazonNativeWindow(Func<string, CancellationToken, Task> complete, Action<string, object> report)
        {
            this.complete = complete; this.report = report;
            Title = "Sign in with Amazon · eero Desktop"; Width = 1080; Height = 820; MinWidth = 850; MinHeight = 650;
            Icon = ApplicationIcon.WindowImage; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SourceInitialized += delegate { Program.ThemeNativeWindow(this); };
            var root = new Grid { Margin = new Thickness(16) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = new StackPanel(); top.Children.Add(new TextBlock { Text = "Sign in with Amazon", FontSize = 24, FontWeight = FontWeights.SemiBold });
            top.Children.Add(new TextBlock { Text = "Sign in with the Amazon account linked to your eero account. This window closes after eero verifies your sign-in.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
            address = new TextBlock { Text = "www.amazon.com", Margin = new Thickness(0, 0, 0, 12) }; top.Children.Add(address); root.Children.Add(top);
            browser = new WebView2 { CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = ServiceLinks.ProfileDirectory, ProfileName = "AmazonNative-" + Guid.NewGuid().ToString("N"), IsInPrivateModeEnabled = true }, DefaultBackgroundColor = System.Drawing.Color.FromArgb(28, 28, 30) };
            Grid.SetRow(browser, 1); root.Children.Add(browser);
            var bottom = new StackPanel(); notice = new TextBlock { Text = "Opening Amazon’s secure sign-in…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) }; bottom.Children.Add(notice);
            var cancel = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Left }; cancel.Click += delegate { Close(); }; bottom.Children.Add(cancel); Grid.SetRow(bottom, 2); root.Children.Add(bottom); Content = root;
            Loaded += async delegate { await Initialize(); };
            Closed += delegate { closed = true; lifetime.Cancel(); browser.Dispose(); attempt.Dispose(); };
        }
        private async Task Initialize()
        {
            try
            {
                ServiceLinks.InitializeLoader(); await browser.EnsureCoreWebView2Async(); if (closed) return;
                browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                browser.CoreWebView2.Profile.PreferredColorScheme = UiPreferences.Dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
                browser.CoreWebView2.NavigationStarting += async delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    Uri uri;
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) && AmazonNativeAttempt.ReturnAddress(uri))
                    {
                        e.Cancel = true; if (completing || closed) return; completing = true;
                        string phase = "amazon_token_exchange";
                        try
                        {
                            report("amazon_code_returned", null); notice.Text = "Finishing Amazon sign-in…";
                            string token = await attempt.Exchange(uri, lifetime.Token); report("amazon_token_received", null);
                            phase = "eero_account_verification";
                            notice.Text = "Verifying your eero account…"; await complete(token, lifetime.Token); token = null;
                            if (!closed) Close();
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception error)
                        {
                            var failure = error as ApiException; string code = failure == null ? "" : failure.ApiError;
                            if (!closed) notice.Text = phase == "eero_account_verification" && code == "error.login.unknown"
                                ? "Amazon sign-in succeeded, but eero does not recognize this Amazon account for an existing eero login. Use your eero email or phone number. Amazon login can be managed in eero’s Account settings; no account was created or changed here."
                                : (phase == "amazon_token_exchange" ? "Amazon could not finish sign-in: " : "eero could not verify this Amazon sign-in: ") + MainWindow.Message(error) + " Close this window to start a fresh sign-in.";
                            report("amazon_signin_error", J.Obj("phase", phase, "error_type", error.GetType().Name, "http_code", failure == null ? 0 : failure.Code, "api_error", Regex.IsMatch(code ?? "", "^[a-zA-Z0-9_.-]{1,140}$") ? code : ""));
                        }
                        return;
                    }
                    if (!AmazonNativeAttempt.Navigation(e.Uri)) { e.Cancel = true; notice.Text = "Amazon requested an unsupported destination."; return; }
                    address.Text = uri.Host;
                };
                browser.CoreWebView2.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; if (AmazonNativeAttempt.Navigation(e.Uri)) browser.CoreWebView2.Navigate(e.Uri); };
                browser.CoreWebView2.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (completing || closed) return;
                    notice.Text = e.IsSuccess ? "Complete Amazon sign-in and any verification on the page above. The window closes only after eero verifies your account." : "The Amazon page could not load (" + e.WebErrorStatus + ").";
                    report(e.IsSuccess ? "amazon_page_loaded" : "amazon_page_error", e.IsSuccess ? null : J.Obj("browser_error", e.WebErrorStatus.ToString()));
                };
                report("amazon_browser_ready", null); browser.CoreWebView2.Navigate(attempt.SignInUrl);
            }
            catch (Exception error) { if (!closed) notice.Text = MainWindow.Message(error); report("amazon_browser_error", J.Obj("error_type", error.GetType().Name)); }
        }
    }

    public sealed partial class MainWindow
    {
        private void AmazonConsumerSignIn()
        {
            var existing = OwnedWindows.OfType<AmazonNativeWindow>().FirstOrDefault(); if (existing != null) { existing.Activate(); return; }
            var window = new AmazonNativeWindow(async delegate(string token, CancellationToken cancellation)
            {
                await CompleteExternalSignIn(async delegate
                {
                    bool remember = api.Remember, success = false;
                    try
                    {
                        api.Remember = rememberBox == null || rememberBox.IsChecked == true;
                        await api.AmazonSignIn(token, cancellation); success = true; ReportStatus("amazon_authenticated");
                        ClearData(); await LoadAccount(); ShowShell(); SetNotice("Signed in with Amazon."); ReportStatus("dashboard_loaded");
                    }
                    finally { if (!success) api.Remember = remember; }
                });
            }, delegate(string state, object details)
            {
                if (String.IsNullOrEmpty(statusPath)) return;
                try
                {
                    string json = J.Stringify(J.Obj("state", state, "time_utc", DateTime.UtcNow.ToString("o"), "details", details));
                    File.WriteAllText(statusPath + ".amazon-native.json", json);
                    string history = statusPath + ".amazon-native.history.jsonl";
                    if (!File.Exists(history) || new FileInfo(history).Length < 65536) File.AppendAllText(history, json + Environment.NewLine);
                }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }) { Owner = this }; window.Show();
        }
    }
}
