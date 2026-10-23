using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EeroDesktop
{
    public static class ServiceLinks
    {
        public static Uri Eero(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length > 0 || !(uri.Host == "eero.com" || uri.Host.EndsWith(".eero.com", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("The service did not return a secure eero web address.");
            return uri;
        }
        public static string TokenHeader(string token)
        {
            if (String.IsNullOrEmpty(token) || token.Length > 8192 || token.Any(c => c < 33 || c > 126)) throw new InvalidDataException("The web service did not return a valid temporary sign-in token.");
            return "x-temp-user-token: " + token + "\r\n";
        }
        public static bool KeepTemporaryHeader(string initial, string request)
        {
            Uri target, resource;
            try { target = Eero(initial); } catch (InvalidDataException) { return false; }
            return Uri.TryCreate(request, UriKind.Absolute, out resource) && resource.Scheme == "https" && resource.Port == 443 && resource.UserInfo.Length == 0 && target.AbsoluteUri == resource.AbsoluteUri;
        }
        public static bool Allowed(string value)
        {
            if (value == "about:blank") return true;
            Uri u; if (!Uri.TryCreate(value, UriKind.Absolute, out u) || u.Scheme != "https" || u.Port != 443 || u.UserInfo.Length > 0) return false;
            string[] domains = { "eero.com", "e2ro.com", "amazon.com", "alexa.com", "amazon.co.uk", "amazon.de", "amazon.fr", "amazon.it", "amazon.es", "amazon.ca", "amazon.com.au", "amazon.co.jp", "amazon.in", "amazon.com.mx", "amazon.com.br", "guardianapp.com", "1password.com", "malwarebytes.com", "stripe.com", "paypal.com", "eero.zendesk.com" };
            return domains.Any(d => u.Host == d || u.Host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
        }
        public static string ProfileDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EeroDesktop", "WebServices"); } }
        public static bool ProviderNavigation(string value)
        { Uri uri; return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0; }
        public static void InitializeLoader()
        {
            string arch = Environment.Is64BitProcess ? "win-x64" : "win-x86";
            if ((Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "").Equals("ARM64", StringComparison.OrdinalIgnoreCase)) arch = "win-arm64";
            CoreWebView2Environment.SetLoaderDllFolderPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", arch, "native"));
        }
        public static int CheckRuntime(string output)
        {
            try { InitializeLoader(); string version = CoreWebView2Environment.GetAvailableBrowserVersionString(); File.WriteAllText(output, J.Stringify(J.Obj("ok", true, "webview2_runtime", version))); return 0; }
            catch (Exception e) { File.WriteAllText(output, J.Stringify(J.Obj("ok", false, "error", e.GetType().Name))); return 1; }
        }
    }

    public sealed class ServiceBrowserWindow : Window
    {
        private readonly WebView2 browser;
        private readonly TextBlock address, notice;
        private readonly string target, headers;
        private readonly bool sample;
        private readonly bool providerNavigation;
        private readonly Func<Uri, bool> callbackMatch;
        private readonly Func<Uri, Task> callback;
        private bool completing;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        public CancellationToken LifetimeToken { get { return lifetime.Token; } }
        private readonly Func<string, Task> importSession;
        private readonly Button import;
        private const string SampleHtml = "<html><body style='background:#101a2b;color:#e5ecf8;font:18px Segoe UI;padding:40px'><h1>Windows service browser</h1><p>WebView2 is running. This sample uses no account or network request.</p></body></html>";
        public ServiceBrowserWindow(string title, string url, string extraHeaders = null, Func<string, Task> import = null, bool preview = false, Func<Uri, bool> callbackMatch = null, Func<Uri, Task> callback = null, bool providerNavigation = false)
        {
            Title = title + " · eero Desktop"; Width = 1080; Height = 800; MinWidth = 850; MinHeight = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            target = url; headers = extraHeaders; importSession = import; sample = preview;
            this.callbackMatch = callbackMatch; this.callback = callback; this.providerNavigation = providerNavigation;
            SourceInitialized += delegate { Program.ThemeNativeWindow(this); };
            var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = new StackPanel { Margin = new Thickness(18) }; address = new TextBlock { Text = preview ? "Sample service window" : new Uri(url).Host, FontSize = 17, FontWeight = FontWeights.SemiBold }; top.Children.Add(address);
            if (import != null)
            {
                top.Children.Add(new TextBlock { Text = "Sign in yourself on eero's website. Use this sign-in imports only a verified eero API session from this private app browser.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
                this.import = new Button { Content = "Use this sign-in", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left }; this.import.Click += ImportClick; top.Children.Add(this.import);
            }
            root.Children.Add(top); browser = new WebView2 { CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = ServiceLinks.ProfileDirectory, IsInPrivateModeEnabled = true }, DefaultBackgroundColor = System.Drawing.Color.FromArgb(16, 26, 43) }; Grid.SetRow(browser, 1); root.Children.Add(browser);
            notice = new TextBlock { Text = "Opening secure service…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18, 10, 18, 16) }; Grid.SetRow(notice, 2); root.Children.Add(notice); Content = root;
            Loaded += async delegate { await Initialize(); }; Closed += delegate { lifetime.Cancel(); browser.Dispose(); };
        }
        private async Task Initialize()
        {
            try
            {
                ServiceLinks.InitializeLoader(); await browser.EnsureCoreWebView2Async();
                browser.CoreWebView2.Profile.PreferredColorScheme = UiPreferences.Dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
                browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                browser.CoreWebView2.WebResourceRequested += delegate(object sender, CoreWebView2WebResourceRequestedEventArgs e)
                {
                    if (e.Request.Headers.Contains("x-temp-user-token") && !ServiceLinks.KeepTemporaryHeader(target, e.Request.Uri)) e.Request.Headers.RemoveHeader("x-temp-user-token");
                };
                browser.CoreWebView2.NavigationStarting += async delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (sample && e.Uri == "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(SampleHtml))) return;
                    Uri destination;
                    if (callbackMatch != null && Uri.TryCreate(e.Uri, UriKind.Absolute, out destination) && callbackMatch(destination))
                    {
                        e.Cancel = true; if (completing) return; completing = true;
                        try { notice.Text = "Verifying the service response…"; await callback(destination); if (IsVisible) Close(); }
                        catch (Exception error) { notice.Text = MainWindow.Message(error); }
                        finally { completing = false; }
                        return;
                    }
                    if (!(ServiceLinks.Allowed(e.Uri) || providerNavigation && ServiceLinks.ProviderNavigation(e.Uri))) { e.Cancel = true; notice.Text = "This destination is outside the supported provider websites."; return; }
                    Uri u; if (Uri.TryCreate(e.Uri, UriKind.Absolute, out u) && u.Scheme == "https") address.Text = u.Host + u.AbsolutePath;
                };
                browser.CoreWebView2.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; Uri uri; if (ServiceLinks.Allowed(e.Uri) || providerNavigation && ServiceLinks.ProviderNavigation(e.Uri) || callbackMatch != null && Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) && callbackMatch(uri)) browser.CoreWebView2.Navigate(e.Uri); else notice.Text = "The provider requested a destination outside this service window."; };
                browser.CoreWebView2.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e) { notice.Text = e.IsSuccess ? sample ? "Local sample loaded. No account or network request." : "Secure provider service. Complete any sign-in or purchase yourself." : "The provider page did not finish loading (" + e.WebErrorStatus + ")."; if (import != null) import.IsEnabled = e.IsSuccess; };
                if (sample) browser.NavigateToString(SampleHtml);
                else if (headers == null) browser.CoreWebView2.Navigate(target);
                else { ServiceLinks.Eero(target); var request = browser.CoreWebView2.Environment.CreateWebResourceRequest(target, "GET", null, headers); browser.CoreWebView2.NavigateWithWebResourceRequest(request); }
            }
            catch (Exception e) { notice.Text = "The secure browser could not start: " + MainWindow.Message(e) + " Install or repair Microsoft Edge WebView2 Runtime if it is missing."; }
        }
        private async void ImportClick(object sender, RoutedEventArgs args)
        {
            import.IsEnabled = false;
            try
            {
                var cookies = await browser.CoreWebView2.CookieManager.GetCookiesAsync(EeroApi.Origin);
                var session = cookies.FirstOrDefault(c => c.Name == "s" && c.IsSecure && (c.Domain.TrimStart('.') == "api-user.e2ro.com" || c.Domain.TrimStart('.') == "e2ro.com"));
                if (session == null) { notice.Text = "This website has not provided a reusable eero API session. Finish signing in, or use email/phone verification in the Windows app."; return; }
                await importSession(session.Value); if (IsVisible) Close();
            }
            catch (Exception e) { notice.Text = MainWindow.Message(e); }
            finally { import.IsEnabled = true; }
        }
    }

    public sealed partial class MainWindow
    {
        private void OpenServiceWindow(string title, string url, string headers = null)
        {
            if (!ServiceLinks.Allowed(url)) throw new InvalidOperationException("The service returned an unsupported provider website.");
            var w = new ServiceBrowserWindow(title, url, headers) { Owner = this }; w.Show();
        }
        private async Task OpenPaymentService(string targetPage)
        {
            if (preview) throw new InvalidOperationException("Sign in to manage a subscription.");
            object session = await api.Send("GET", "/2.2/login/temporary?client_type=WEB_PAYMENTS&target_page=" + Uri.EscapeDataString(targetPage));
            string url = J.S(session, "target_url"); ServiceLinks.Eero(url);
            OpenBillingWindow(url, ServiceLinks.TokenHeader(J.S(session, "temporary_token")));
        }
        private void WebsiteSignIn()
        {
            api.Remember = rememberBox == null || rememberBox.IsChecked == true;
            ServiceBrowserWindow w = null;
            w = new ServiceBrowserWindow("Sign in on eero's website", "https://account.eero.com/", null, async token =>
            {
                await CompleteExternalSignIn(async delegate { await api.ImportBrowserSession(token, w.LifetimeToken); ClearData(); await LoadAccount(); ShowShell(); SetNotice("Website sign-in verified with eero."); });
            }) { Owner = this }; w.Show();
        }
    }
}
