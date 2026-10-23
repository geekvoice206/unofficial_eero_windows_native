using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

[assembly: System.Reflection.AssemblyTitle("eero Desktop (Unofficial) - Carlos B. (Los33)")]
[assembly: System.Reflection.AssemblyCompany("Carlos B.")]
[assembly: System.Reflection.AssemblyProduct("eero Desktop (Unofficial)")]
[assembly: System.Reflection.AssemblyDescription("Independent eero network manager for Windows. Made by: Carlos B. (Los33).")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace EeroDesktop
{
    public static partial class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test") return Tests.Run(args.Length > 1 ? args[1] : "test-results.txt");
            if (args.Length > 1 && args[0] == "--connection-test") return ConnectionDiagnostics.Run(args[1]);
            if (args.Length > 1 && args[0] == "--browser-runtime-test") return ServiceLinks.CheckRuntime(args[1]);
            if (args.Length > 1 && args[0] == "--bluetooth-test") return NativeBluetooth.Check(args[1]);
            if (args.Length == 2 && args[0] == "--vpn-control") return WindowsVpn.ElevatedControl(args[1]);
            AppInstance instance = null;
            try
            {
            bool isolated = args.Contains("--amazon-native-signin") || args.Contains("--preview") || args.Contains("--theme-preview") || args.Contains("--browser-preview") || args.Contains("--chat-preview") || (args.Length > 0 && (args[0] == "--render" || args[0] == "--render-chat"));
            if (!isolated)
            {
                instance = new AppInstance(AppInstance.UserScope);
                if (!instance.IsOwner) { if (!args.Contains("--background")) instance.ShowOwner(); return 0; }
                if (args.Contains("--background") && !UiPreferences.StartWithWindows) return 0;
            }
            var app = new Application();
            int themeIndex = Array.IndexOf(args, "--theme");
            if (themeIndex >= 0 && themeIndex + 1 < args.Length) UiPreferences.Theme = args[themeIndex + 1];
            ApplyTheme();
            if (args.Contains("--chat-preview")) { app.Run(MainWindow.ChatSample()); return 0; }
            if (args.Length > 1 && args[0] == "--render-chat")
            {
                var sample = MainWindow.ChatSample(); var content = (System.Windows.FrameworkElement)sample.Content;
                content.Measure(new Size(960, 780)); content.Arrange(new Rect(0, 0, 960, 780)); content.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
                var bitmap = new RenderTargetBitmap(960, 780, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(args[1])) png.Save(output);
                sample.Close(); return 0;
            }
            if (args.Contains("--browser-preview")) { app.Run(new ServiceBrowserWindow("Service browser sample", null, null, null, true)); return 0; }
            string statusPath = null;
            int statusIndex = Array.IndexOf(args, "--status-file");
            if (statusIndex >= 0 && statusIndex + 1 < args.Length) statusPath = args[statusIndex + 1];
            bool render = args.Length > 1 && args[0] == "--render";
            var w = new MainWindow(args.Contains("--preview") || args.Contains("--theme-preview") || render, statusPath, args.Contains("--background"), args.Contains("--amazon-native-signin"));
            if (instance != null) instance.Listen(delegate { if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(new Action(w.RestoreWindow)); });
            app.SessionEnding += delegate { w.PrepareForShutdown(); };
            if (args.Contains("--theme-preview")) w.PreviewPage("Theme preview");
            if (render)
            {
                if (args.Length > 2) w.PreviewPage(args[2]);
                int renderWidth = 1260, renderHeight = 820;
                int sizeIndex = Array.IndexOf(args, "--render-size");
                if (sizeIndex >= 0 && sizeIndex + 1 < args.Length)
                {
                    string[] dimensions = args[sizeIndex + 1].Split('x');
                    if (dimensions.Length != 2 || !Int32.TryParse(dimensions[0], out renderWidth) || !Int32.TryParse(dimensions[1], out renderHeight) || renderWidth < 1040 || renderWidth > 3200 || renderHeight < 720 || renderHeight > 2400) throw new ArgumentException("Render size must be between 1040x720 and 3200x2400.");
                }
                w.Width = renderWidth; w.Height = renderHeight;
                var surface = (Grid)w.Content;
                surface.Background = w.Background;
                surface.Measure(new Size(w.Width, w.Height)); surface.Arrange(new Rect(0, 0, w.Width, w.Height)); surface.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
                surface.UpdateLayout();
                var image = new RenderTargetBitmap(renderWidth, renderHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(surface);
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
                image.Clear(); image.Render(surface);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (var stream = File.Create(args[1])) encoder.Save(stream);
                w.Close();
                return 0;
            }
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                e.Handled = true;
                w.ShowApplicationError("The app could not complete that action. " + MainWindow.Message(e.Exception));
            };
            app.Run(w);
            return 0;
            }
            finally { if (instance != null) instance.Dispose(); }
        }

    }

    public sealed partial class MainWindow : Window
    {
        private EeroApi api;
        private Grid root;
        private StackPanel page, navigation;
        private TextBlock notice, accountLabel;
        private ComboBox networks;
        private FrameworkElement controls;
        private bool preview, busy, loading;
        private string currentPage = "Home", networkUrl;
        private object account, network;
        private List<object> networkList = new List<object>();
        private List<object> devices = new List<object>(), nodes = new List<object>(), profiles = new List<object>();
        private DateTime lastRefresh;
        private DateTime resendAfter;
        private readonly DispatcherTimer timer;
        private readonly Dictionary<string, string> loadErrors = new Dictionary<string, string>();
        private TextBox identifierBox, codeBox;
        private CheckBox rememberBox;
        private bool formatPhone = true;
        private readonly string statusPath;
        private static readonly Dictionary<string, SolidColorBrush> paletteBrushes = new Dictionary<string, SolidColorBrush>();

        public MainWindow(bool isPreview, string diagnosticsPath = null, bool startInBackground = false, bool startAmazonNativeSignIn = false)
        {
            if (Application.Current.TryFindResource("NavigationButton") == null) Program.ApplyTheme();
            statusPath = diagnosticsPath;
            preview = isPreview; backgroundLaunch = startInBackground && !isPreview;
            Title = "eero Desktop · Unofficial · v1.0";
            Icon = ApplicationIcon.WindowImage;
            Width = 1360; Height = 880; MinWidth = 1040; MinHeight = 720;
            UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); FontSize = 14; Foreground = Brush("#17263C"); Background = Brush("#F4F6FA");
            SourceInitialized += delegate { Program.ThemeNativeWindow(this); };
            bool lastDark = UiPreferences.Dark;
            Microsoft.Win32.UserPreferenceChangedEventHandler themeChanged = delegate
            {
                if (UiPreferences.Theme != "System" || Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(new Action(delegate { if (UiPreferences.Theme == "System" && lastDark != UiPreferences.Dark) { lastDark = UiPreferences.Dark; Program.ApplyTheme(); } }));
            };
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += themeChanged;
            Closed += delegate { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= themeChanged; };
            api = new EeroApi(new SessionStore(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EeroDesktop", "session.dat")));
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            timer.Tick += async delegate { if (!busy && !preview && api.SignedIn && IsActive && (currentPage == "Home" || currentPage == "Devices")) await Run(async delegate { await LoadNetwork(); RenderPage(); }, "Refreshing network…"); };
            Closed += delegate { timer.Stop(); api.Dispose(); };
            ConfigureDesktopNotifications();
            ReportStatus("ready");
            if (preview) { SampleData(); ShowShell(); }
            else
            {
                ShowLogin(false);
                bool initialized = false;
                Loaded += async delegate
                {
                    if (initialized) return; initialized = true;
                    if (backgroundLaunch) { ShowInTaskbar = false; Hide(); }
                    if (startAmazonNativeSignIn) { AmazonConsumerSignIn(); return; }
                    if (api.Restore()) await Run(async delegate { await LoadAccount(); ShowShell(); ReportStatus("dashboard_loaded"); }, "Restoring your session…");
                    if (!api.SignedIn && backgroundLaunch) SignInAttention();
                    if (api.SignedIn) await PollNotifications();
                };
            }
        }
        private void ReportStatus(string state, Exception error = null)
        {
            if (String.IsNullOrEmpty(statusPath)) return;
            try
            {
                var apiError = error as ApiException;
                File.WriteAllText(statusPath, J.Stringify(J.Obj("state", state, "time_utc", DateTime.UtcNow.ToString("o"), "error_type", error == null ? "" : error.GetType().Name, "transport_category", error == null || apiError != null ? "" : ConnectionDiagnostics.Category(error), "native_error", error == null ? 0 : ConnectionDiagnostics.NativeCode(error), "http_code", apiError == null ? 0 : apiError.Code, "api_error", apiError == null ? "" : apiError.ApiError, "network_count", networkList.Count, "device_count", devices.Count, "node_count", nodes.Count, "profile_count", profiles.Count, "section_errors", loadErrors.Keys.ToArray())));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private static SolidColorBrush Brush(string color)
        {
            SolidColorBrush brush;
            if (!paletteBrushes.TryGetValue(color, out brush)) { brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(UiPreferences.Color(color))); paletteBrushes[color] = brush; }
            return brush;
        }
        internal static void RefreshPalette()
        { foreach (var pair in paletteBrushes) pair.Value.Color = (Color)ColorConverter.ConvertFromString(UiPreferences.Color(pair.Key)); }
        private static TextBlock Text(string s, double size = 14, string color = "#17263C", bool bold = false)
        { return new TextBlock { Text = s, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }; }
        private static Border Card(UIElement child, double padding = 24)
        { return new Border { Background = Surface, BorderBrush = Brush("#E5EAF3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(padding), Margin = new Thickness(0, 0, 0, 16), Child = child }; }
        private Button Button(string title, Func<Task> action, bool primary = false)
        {
            var b = new Button { Content = title };
            if (primary) b.Style = (Style)FindResource("AccentButton");
            b.Click += async delegate { await Run(action, "Working…"); };
            return b;
        }
        private Button LocalButton(string title, Action action)
        { var b = new Button { Content = title }; b.Click += delegate { if (!busy) action(); }; return b; }
        private void SetNotice(string message, bool error = false)
        {
            if (notice == null) return;
            notice.Text = message;
            notice.Foreground = Brush(error ? "#B5333D" : "#52647C");
        }
        public static string Message(Exception e)
        {
            if (e is HttpRequestException || e is TaskCanceledException) return ConnectionDiagnostics.UserMessage(e);
            if (e is UnauthorizedAccessException || e is System.Security.Cryptography.CryptographicException) return "Windows could not access your saved session. Try signing in without Remember me.";
            return e.Message;
        }
        private async Task Run(Func<Task> action, string status)
        {
            if (busy) return;
            busy = true;
            SetNotice(status);
            if (controls != null) controls.IsEnabled = false;
            try { await action(); }
            catch (ApiException ex)
            {
                ReportStatus("error", ex);
                if (ex.SessionExpired)
                {
                    api.Forget(); timer.Stop(); ClearData(); ShowLogin(false);
                    SetNotice("Your session expired. Sign in again to continue.", true);
                }
                else SetNotice(Message(ex), true);
            }
            catch (Exception ex) { ReportStatus("error", ex); SetNotice(Message(ex), true); }
            finally { busy = false; if (controls != null) controls.IsEnabled = true; }
        }
        private void ClearData()
        { notificationEpoch++; CloseAccountWindows(); account = network = selectedProfile = selectedNode = null; networkUrl = null; currentPage = "Home"; networkList.Clear(); devices.Clear(); nodes.Clear(); profiles.Clear(); loadErrors.Clear(); featureData.Clear(); bluetoothResults.Clear(); wiredSetup = new WiredNetworkSetup(); }

        private void CloseAccountWindows()
        { foreach (Window child in OwnedWindows.Cast<Window>().ToArray()) child.Close(); }

        private void ShowLogin(bool verify)
        {
            root = new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.38, GridUnitType.Star) }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.62, GridUnitType.Star) });
            Content = root;
            var intro = new StackPanel { Margin = new Thickness(48), VerticalAlignment = VerticalAlignment.Center };
            var logo = Text("eero", 66, "#17263C"); logo.FontFamily = new FontFamily("Segoe UI Light"); intro.Children.Add(logo);
            intro.Children.Add(Text("Desktop for Windows", 13, "#64738A"));
            intro.Children.Add(new Border { Height = 42, Background = Brushes.Transparent });
            var wifi = Glyph("\uE701", 76, "#274CDB"); wifi.HorizontalAlignment = HorizontalAlignment.Left; wifi.Margin = new Thickness(0, 0, 0, 36); intro.Children.Add(wifi);
            intro.Children.Add(Text("Your wifi.\nFrom your desktop.", 30, "#17263C", true));
            intro.Children.Add(Text("A clear view of your network, devices, and profiles.", 15, "#64738A"));
            intro.Children.Add(Text("Independent desktop client\nNot affiliated with Amazon or eero.", 11, "#64738A"));
            var left = new Border { Background = Brush("#F3F3F3"), BorderBrush = Brush("#E5EAF3"), BorderThickness = new Thickness(0, 0, 1, 0), Child = intro }; root.Children.Add(left);
            var form = new StackPanel { MaxWidth = 410, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36) };
            var formScroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumn(formScroll, 1); root.Children.Add(formScroll); controls = form;
            form.Children.Add(Text(verify ? "Check your messages" : "Welcome home", 32, "#17263C", true));
            form.Children.Add(Text(verify ? "Enter the verification code sent to your eero account. Keep this window open while you get it." : "Sign in with the email address or phone number linked to your eero account.", 15, "#64738A"));
            form.Children.Add(new Border { Height = 18 });
            if (!verify)
            {
                form.Children.Add(Text("Sign in using", 13, "#17263C", true));
                var method = new ComboBox(); method.Items.Add("Phone number"); method.Items.Add("Email address"); method.SelectedIndex = formatPhone ? 0 : 1; form.Children.Add(method);
                var inputLabel = Text(formatPhone ? "Phone number" : "Email address", 13, "#17263C", true); form.Children.Add(inputLabel);
                identifierBox = new TextBox { MaxLength = 254 };
                System.Windows.Automation.AutomationProperties.SetName(identifierBox, "Email or phone number");
                form.Children.Add(identifierBox);
                var phoneHint = Text(formatPhone ? "10 digits automatically use +1 (US/Canada). For other countries, include + and the country code." : "Use the email address linked to your eero account.", 12, "#64738A"); form.Children.Add(phoneHint);
                bool formatting = false;
                Action formatNumber = delegate
                {
                    if (!formatPhone || formatting) return;
                    string before = identifierBox.Text;
                    string after = PhoneInput.FormatForDisplay(before);
                    if (before == after) return;
                    int caret = PhoneInput.PreserveCaret(before, identifierBox.CaretIndex, after);
                    formatting = true; identifierBox.Text = after; identifierBox.CaretIndex = caret; formatting = false;
                };
                identifierBox.TextChanged += delegate { formatNumber(); };
                method.SelectionChanged += delegate
                {
                    formatPhone = method.SelectedIndex == 0;
                    inputLabel.Text = formatPhone ? "Phone number" : "Email address";
                    phoneHint.Text = formatPhone ? "10 digits automatically use +1 (US/Canada). For other countries, include + and the country code." : "Use the email address linked to your eero account.";
                    formatNumber();
                };
                rememberBox = new CheckBox { Content = "Remember me on this Windows account", IsChecked = true };
                form.Children.Add(rememberBox);
                var send = Button("Send verification code", async delegate
                {
                    string id = identifierBox.Text.Trim();
                    if (formatPhone) { id = PhoneInput.Normalize(id); formatNumber(); }
                    else if (!Regex.IsMatch(id, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) throw new ArgumentException("Enter the email address linked to your eero account.");
                    api.Remember = rememberBox.IsChecked == true;
                    CloseAccountWindows();
                    await api.BeginLogin(id);
                    resendAfter = DateTime.UtcNow.AddSeconds(60);
                    ShowLogin(true); ReportStatus("code_requested"); SetNotice("Code requested. Check your email or text messages.");
                }, true);
                send.HorizontalAlignment = HorizontalAlignment.Stretch; send.Margin = new Thickness(0, 6, 0, 20); form.Children.Add(send);
                identifierBox.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && !busy) send.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };
                form.Children.Add(Text("Your session is encrypted by Windows when Remember me is enabled.", 12, "#64738A"));
            }
            else
            {
                form.Children.Add(Text("Verification code", 13, "#17263C", true));
                codeBox = new TextBox { MaxLength = 12, FontSize = 28 };
                System.Windows.Automation.AutomationProperties.SetName(codeBox, "Verification code");
                form.Children.Add(codeBox);
                var confirm = Button("Verify and sign in", async delegate
                {
                    string code = codeBox.Text.Trim();
                    if (!Regex.IsMatch(code, @"^[0-9]{4,10}$")) throw new ArgumentException("Enter the numeric code from your email or text message.");
                    if (api.Registering) await api.VerifyRegistration(code, registrationCountry, registrationConsents);
                    else await api.Verify(code);
                    ReportStatus("authenticated"); codeBox.Clear(); ShowShell(); await LoadAccount(); ShowShell(); ReportStatus("dashboard_loaded");
                }, true);
                confirm.HorizontalAlignment = HorizontalAlignment.Stretch; form.Children.Add(confirm);
                codeBox.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && !busy) confirm.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };
                var row = new WrapPanel();
                row.Children.Add(Button("Resend code", async delegate
                {
                    int wait = (int)Math.Ceiling((resendAfter - DateTime.UtcNow).TotalSeconds);
                    if (wait > 0) throw new InvalidOperationException("You can request another code in " + wait + " seconds.");
                    await api.Resend(); resendAfter = DateTime.UtcNow.AddSeconds(60); SetNotice("Another code has been requested.");
                }));
                row.Children.Add(LocalButton("Use a different account", delegate { api.CancelLogin(); ShowLogin(false); })); form.Children.Add(row);
            }
            notice = Text("", 13, "#52647C"); notice.Margin = new Thickness(0, 18, 0, 0); form.Children.Add(notice);
            form.Children.Add(Button("Check connection", async delegate
            {
                await api.CheckConnection();
                SetNotice("Connected to eero securely. You can request your verification code.");
                ReportStatus("connection_ok");
            }));
            if (!verify) form.Children.Add(LocalButton("Create an eero account", BeginRegistrationUi));
            if (!verify) form.Children.Add(LocalButton("Sign in on eero's website", WebsiteSignIn));
            if (!verify) form.Children.Add(Button("Sign in through an internet provider", ProviderSignIn));
            if (!verify) form.Children.Add(LocalButton("Sign in with Amazon", AmazonConsumerSignIn));
            if (!preview && WindowsVpn.OwnActiveScopes().Length > 0) form.Children.Add(Button("Disconnect active Windows VPN", async delegate
            { foreach (string scope in WindowsVpn.OwnActiveScopes()) await WindowsVpn.Control("disconnect", scope); SetNotice("Windows VPN disconnected."); }));
            form.Children.Add(Text("Uses eero’s cloud service. An internet connection is required.", 12, "#64738A"));
        }
        private UIElement NetworkArt()
        {
            var canvas = new Canvas { Height = 160, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
            for (int i = 0; i < 3; i++)
            {
                double size = 80 + i * 50;
                var circle = new Ellipse { Width = size, Height = size, Stroke = Brush("#355886"), StrokeThickness = 1.4 };
                Canvas.SetLeft(circle, 120 - size / 2); Canvas.SetTop(circle, 70 - size / 2); canvas.Children.Add(circle);
            }
            var node = new Border { Width = 80, Height = 54, Background = Brushes.White, CornerRadius = new CornerRadius(20), Child = new TextBlock { Text = "•", FontSize = 25, Foreground = Brush("#178E72"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            Canvas.SetLeft(node, 80); Canvas.SetTop(node, 44); canvas.Children.Add(node);
            return canvas;
        }

        private async Task LoadAccount()
        {
            account = await api.Send("GET", "account");
            networkList = J.List(J.Get(account, "networks"));
            if (networkList.Count == 0) networkList = J.List(await api.Send("GET", "networks"));
            networkUrl = networkList.Count > 0 ? J.S(networkList[0], "url") : null;
            if (!String.IsNullOrEmpty(networkUrl)) await LoadNetwork();
        }
        private string Resource(object o, string key, string fallback)
        {
            object r = J.Get(o, "resources", key);
            string s = r is string ? (string)r : J.S(r, "url");
            return String.IsNullOrEmpty(s) ? fallback : s;
        }
        private async Task LoadNetwork()
        {
            if (String.IsNullOrEmpty(networkUrl)) return;
            loadErrors.Clear();
            network = await api.Send("GET", networkUrl);
            devices = await LoadList("Devices", Resource(network, "devices", networkUrl + "/devices"));
            nodes = await LoadList("eero nodes", Resource(network, "eeros", networkUrl + "/eeros"));
            profiles = await LoadList("Profiles", Resource(network, "profiles", networkUrl + "/profiles"));
            lastRefresh = DateTime.Now;
        }
        private async Task<List<object>> LoadList(string name, string url)
        {
            try { return J.List(await api.Send("GET", url)); }
            catch (ApiException ex) { if (ex.SessionExpired) throw; loadErrors[name] = Message(ex); return new List<object>(); }
            catch (HttpRequestException ex) { loadErrors[name] = Message(ex); return new List<object>(); }
            catch (TaskCanceledException ex) { loadErrors[name] = Message(ex); return new List<object>(); }
        }
        private void ShowShell() { NativeShell(); }
        public void PreviewPage(string name)
        {
            if (name == "Login") { ShowLogin(false); return; }
            if (name == "Home/Profiles/Profile") selectedProfile = J.Obj("name", "Family", "url", "/2.2/networks/preview/profiles/1", "paused", false, "devices", devices.Take(2).ToArray(), "schedule", new[] { J.Obj("name", "Bedtime", "enabled", true, "days", new[] { "MON", "TUE", "WED", "THU", "FRI" }, "start", "22:00", "end", "07:00") }, "premium_dns", J.Obj("dns_policies", J.Obj("block_malware", true, "safe_search_enabled", true)));
            if (name == "Home/eeros/Details") selectedNode = nodes.First();
            currentPage = name; featureData[name] = PreviewFeature(name); RenderPage();
        }
        private void RenderPage()
        {
            if (page == null) return;
            loading = true; page.Children.Clear();
            UpdateNavigation();
            PageHeading();
            if (network == null && !AccountOnlyPage(currentPage))
            {
                page.Children.Add(Card(Text("No network loaded. Refresh your account, join a network or create one using a separate eero ready for setup.", 16))); page.Children.Add(Section("Join a network", "Settings/User permissions/Join a network")); page.Children.Add(Section("Pending transfers", "Settings/Network management/Pending transfers")); page.Children.Add(Section("Create a wired network", "Home/Add or replace eero/Create a network")); page.Children.Add(Section("Incoming access invitations", "Settings/User permissions/Incoming access invitations")); loading = false; return;
            }
            if (currentPage == "Home" || currentPage == "Overview") Overview();
            else if (currentPage == "Devices") DevicePage();
            else if (currentPage == "eero nodes") NodePage();
            else if (currentPage == "Profiles") ProfilesPageV2();
            else if (currentPage == "Activity") ActivityMenu();
            else if (currentPage == "Settings") FeatureMenu("Settings");
            else if (currentPage == "Help") HelpPage();
            else RenderFeature();
            SetNotice(preview ? "Preview mode · No network requests or changes" : "Updated " + lastRefresh.ToString("h:mm:ss tt") + " · Refreshes every minute while active");
            loading = false;
        }
        private void Sub(string text) { if (!String.Equals(text, currentPage.Split('/').Last(), StringComparison.OrdinalIgnoreCase)) page.Children.Add(Text(text, 14, "#64738A")); page.Children.Add(new Border { Height = 10 }); }
        private void Availability(string key)
        { if (loadErrors.ContainsKey(key)) page.Children.Add(Card(Text("Could not load " + key.ToLowerInvariant() + ": " + loadErrors[key], 14, "#B5333D"))); }
        private void Overview() { NativeOverview(); }
        private string Speed(string direction)
        {
            string value = J.S(network, "speed", direction, "value");
            return value.Length == 0 ? "—" : value + " " + (J.S(network, "speed", direction, "units").NullIfEmpty() ?? "Mbps");
        }
        private void Metric(Grid g, int index, string title, string value, string caption)
        {
            var s = new StackPanel(); s.Children.Add(Text(title, 10, "#64738A", true)); s.Children.Add(Text(value, 25, "#17263C", true)); s.Children.Add(Text(caption, 11, "#64738A"));
            var c = Card(s, 18); c.Margin = new Thickness(0, 0, index == 3 ? 0 : 12, 20); Grid.SetColumn(c, index); g.Children.Add(c);
        }
        public sealed class NetworkChoice { public string Name { get; set; } public string Url { get; set; } }
        public sealed class DeviceRow
        {
            public object Raw { get; set; } public string Name { get; set; } public string Status { get; set; } public string IP { get; set; } public string Connection { get; set; } public string Profile { get; set; }
        }
        private static DeviceRow Device(object d)
        {
            return new DeviceRow { Raw = d, Name = J.First(d, "nickname", "display_name", "hostname", "mac"), Status = J.B(d, "blocked") ? "Blocked" : J.B(d, "paused") ? "Paused" : J.B(d, "connected") ? "Online" : "Offline", IP = J.First(d, "ip", "ip_address"), Connection = J.B(d, "wireless") ? (J.S(d, "frequency").NullIfEmpty() ?? "Wifi") : "Ethernet", Profile = J.S(d, "profile", "name") };
        }
        private void DevicePage() { NativeDevicePage(); }
        private Window Dialog(string title, StackPanel body)
        {
            var w = new Window { Title = title, Owner = this, Width = 620, Height = 700, MinWidth = 480, MinHeight = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize, Background = Brush("#F4F6FA") };
            w.SourceInitialized += delegate { Program.ThemeNativeWindow(w); };
            body.Margin = new Thickness(28); w.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; return w;
        }
        private void Detail(StackPanel p, string name, string value)
        { p.Children.Add(Text(name, 12, "#64738A")); p.Children.Add(Text(String.IsNullOrEmpty(value) ? "Unavailable" : value, 15)); }
        private async Task Mutate(string method, string url, object body, string success)
        {
            if (preview) throw new InvalidOperationException("Preview mode is read only. Sign in to manage your network.");
            await api.Send(method, url, body); await LoadNetwork(); RenderPage(); SetNotice(success);
        }
        private void DeviceDialog(object d)
        {
            DeviceRow row = Device(d); var p = new StackPanel(); p.Children.Add(Text(row.Name, 25, "#17263C", true));
            Detail(p, "Connection", row.Status + " · " + row.Connection); Detail(p, "IP address", row.IP); Detail(p, "MAC address", J.S(d, "mac")); Detail(p, "Manufacturer", J.S(d, "manufacturer")); Detail(p, "Connected eero", J.S(d, "source", "location")); Detail(p, "Profile", row.Profile);
            p.Children.Add(Text("Device nickname", 13, "#17263C", true)); var name = new TextBox { Text = row.Name, MaxLength = 100 }; p.Children.Add(name);
            var w = Dialog(row.Name, p);
            string url = J.S(d, "url").NullIfEmpty() ?? networkUrl + "/devices/" + Uri.EscapeDataString(J.S(d, "mac"));
            p.Children.Add(Button("Save nickname", async delegate { ValidateName(name.Text); w.Close(); await Mutate("PUT", url, J.Obj("nickname", name.Text.Trim()), "Device nickname updated."); }, true));
            var buttons = new WrapPanel();
            buttons.Children.Add(Button(J.B(d, "paused") ? "Resume internet" : "Pause internet", async delegate
            {
                if (!Confirm("Change internet access for " + row.Name + "?")) return;
                w.Close(); await Mutate("PUT", url, J.Obj("paused", !J.B(d, "paused")), "Device access updated.");
            }));
            buttons.Children.Add(Button(J.B(d, "blocked") ? "Unblock device" : "Block device", async delegate
            {
                if (!Confirm((J.B(d, "blocked") ? "Allow " : "Block ") + row.Name + " on this network?")) return;
                w.Close(); await Change(J.B(d, "blocked") ? "DELETE" : "POST", FeatureUrl("blacklist" + (J.B(d, "blocked") ? "/" + Uri.EscapeDataString(J.S(d, "mac")) : "")), J.B(d, "blocked") ? null : J.Obj("mac", J.S(d, "mac")), true, "Device block setting updated.");
            })); p.Children.Add(buttons);
            DeviceExtraActions(d, w, p);
            p.Children.Add(Text("Pausing or blocking this PC may disconnect the app from the internet.", 12, "#64738A")); w.ShowDialog();
        }
        private void NodePage()
        {
            Sub("Inspect your mesh nodes and restart an individual eero."); Availability("eero nodes");
            page.Children.Add(Section("Placement tests", "Home/eeros/Placement tests"));
            foreach (object n in nodes) page.Children.Add(NodeCard(n));
            if (nodes.Count == 0 && !loadErrors.ContainsKey("eero nodes")) page.Children.Add(Card(Text("No eero nodes were returned.")));
        }
        private Border NodeCard(object n) { return NativeNodeCard(n); }
        private async Task StartSpeedTest()
        {
            if (preview) throw new InvalidOperationException("Preview mode is read only.");
            await api.Send("POST", Resource(network, "speedtest", networkUrl + "/speedtest"));
            SetNotice("Speed test started. Results can take a few minutes. Use Refresh results on the Activity page.");
        }
        private bool Confirm(string message)
        {
            var p = new StackPanel(); p.Children.Add(Text("Confirm change", 23, "#17263C", true)); p.Children.Add(Text(message));
            var w = Dialog("Confirm change", p); w.Height = 340; w.MinHeight = 260;
            var row = new WrapPanel(); var cancel = new Button { Content = "Cancel", IsCancel = true, IsDefault = true }; var accept = new Button { Content = "Continue" };
            cancel.Click += delegate { w.DialogResult = false; }; accept.Click += delegate { w.DialogResult = true; }; row.Children.Add(cancel); row.Children.Add(accept); p.Children.Add(row);
            return w.ShowDialog() == true;
        }
        internal void ShowApplicationError(string message) { Info("eero Desktop", J.Obj("message", message)); }
        private string Prompt(string title, string value)
        {
            var p = new StackPanel(); p.Children.Add(Text(title, 22, "#17263C", true)); var input = new TextBox { Text = value, MaxLength = 100 }; p.Children.Add(input);
            var w = Dialog(title, p); w.Height = 240; w.MinHeight = 220; var save = new Button { Content = "Save", IsDefault = true }; save.Click += delegate { w.DialogResult = true; }; p.Children.Add(save);
            return w.ShowDialog() == true ? input.Text : null;
        }
        internal static void ValidateName(string s) { if (String.IsNullOrWhiteSpace(s) || s.Trim().Length > 100) throw new ArgumentException("Enter a name between 1 and 100 characters."); }
        internal static void ValidateSsid(string s) { if (String.IsNullOrWhiteSpace(s) || Encoding.UTF8.GetByteCount(s.Trim()) > 32) throw new ArgumentException("Wifi names must contain between 1 and 32 UTF-8 bytes."); }
        internal static void ValidatePassword(string s) { if (s.Length < 8 || s.Length > 63 || s.Any(c => c < 32 || c > 126)) throw new ArgumentException("Use a wifi password with 8–63 printable ASCII characters."); }
        private void SampleData()
        {
            account = J.Parse("{\"name\":\"Preview account\"}"); networkUrl = "/2.2/networks/preview";
            networkList = J.List(J.Parse("[{\"name\":\"Maple House\",\"url\":\"/2.2/networks/preview\"}]"));
            network = J.Parse("{\"name\":\"Maple House\",\"health\":{\"internet\":{\"status\":\"connected\"}},\"speed\":{\"down\":{\"value\":942,\"units\":\"Mbps\"},\"up\":{\"value\":118,\"units\":\"Mbps\"}},\"wpa3\":true,\"band_steering\":true,\"sqm\":true,\"upnp\":false,\"ipv6\":true}");
            nodes = J.List(J.Parse("[{\"location\":\"Living room\",\"model\":\"eero Pro 6E\",\"gateway\":true,\"status\":\"green\",\"os_version\":\"Preview\",\"ip_address\":\"192.168.4.1\",\"connected_clients_count\":3},{\"location\":\"Office\",\"model\":\"eero Pro 6E\",\"gateway\":false,\"status\":\"green\",\"os_version\":\"Preview\",\"ip_address\":\"192.168.4.2\",\"connected_clients_count\":2}]"));
            devices = J.List(J.Parse("[{\"nickname\":\"Office PC\",\"connected\":true,\"wireless\":false,\"ip\":\"192.168.4.20\",\"manufacturer\":\"Microsoft\",\"mac\":\"02:00:00:00:00:01\"},{\"nickname\":\"Living room TV\",\"connected\":true,\"wireless\":true,\"frequency\":\"5 GHz\",\"ip\":\"192.168.4.34\",\"mac\":\"02:00:00:00:00:02\"},{\"nickname\":\"Kitchen speaker\",\"connected\":true,\"wireless\":true,\"frequency\":\"2.4 GHz\",\"ip\":\"192.168.4.45\",\"mac\":\"02:00:00:00:00:03\"},{\"nickname\":\"Laptop\",\"connected\":true,\"wireless\":true,\"frequency\":\"6 GHz\",\"ip\":\"192.168.4.52\",\"mac\":\"02:00:00:00:00:04\"},{\"nickname\":\"Tablet\",\"connected\":false,\"wireless\":true,\"ip\":\"192.168.4.63\",\"mac\":\"02:00:00:00:00:05\"} ]"));
            profiles = J.List(J.Parse("[{\"name\":\"Family\",\"paused\":false},{\"name\":\"Kids\",\"paused\":true}]")); lastRefresh = DateTime.Now;
            var nmap = (IDictionary<string, object>)network;
            nmap["ipv6_upstream"] = true; nmap["dns"] = J.Obj("mode", "automatic", "caching", true, "custom", J.Obj("ips", new[] { "1.1.1.1", "8.8.8.8" }));
            nmap["connection"] = J.Obj("mode", "nat"); nmap["timezone"] = J.Obj("value", "America/Chicago");
            nmap["auto_setup_mode"] = "DEFAULT"; nmap["accessories"] = new[] { J.Obj("name", "Sample accessory", "model", "Sample cellular accessory", "dsn", "SAMPLEACCESSORY1", "registered", true, "joined", true, "eero_id", "1") };
            nmap["amazon_account_linked"] = true; nmap["smart_home_hub"] = true;
            for (int i = 0; i < devices.Count; i++) ((IDictionary<string, object>)devices[i])["url"] = "/2.2/networks/preview/devices/" + i;
            for (int i = 0; i < nodes.Count; i++) { var node = (IDictionary<string, object>)nodes[i]; node["url"] = "/2.2/eeros/" + (i + 1); node["serial"] = "PREVIEWNODE000" + (i + 1); node["led_on"] = true; node["led_brightness"] = 100; node["resources"] = J.Obj("led_action", "/2.2/eeros/" + (i + 1) + "/led", "connections", "/2.2/eeros/" + (i + 1) + "/connections"); }
            for (int i = 0; i < profiles.Count; i++) ((IDictionary<string, object>)profiles[i])["url"] = "/2.2/networks/preview/profiles/" + (i + 1);
        }
    }
}

