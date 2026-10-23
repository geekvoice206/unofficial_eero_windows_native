using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EeroDesktop
{
    public static class UiPreferences
    {
        public static string Theme = "System";
        public static bool Notifications, Background, StartWithWindows;
        public static bool AllNetworks = true;
        private static readonly string Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EeroDesktop", "preferences.json");
        static UiPreferences()
        {
            try { if (File.Exists(Path)) { object p = J.Parse(File.ReadAllText(Path)); string theme = J.S(p, "theme"); if (new[] { "Light", "Dark", "System" }.Contains(theme)) Theme = theme; Notifications = J.B(p, "notifications"); Background = J.B(p, "background"); StartWithWindows = J.B(p, "start_with_windows"); AllNetworks = J.Get(p, "all_networks") == null || J.B(p, "all_networks"); } }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
        }
        public static bool Dark
        {
            get
            {
                if (Theme == "Dark") return true;
                if (Theme != "System") return false;
                try { using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) return key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0; }
                catch (System.Security.SecurityException) { return false; }
            }
        }
        public static void Save() { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)); string temporary = Path + ".tmp"; File.WriteAllText(temporary, J.Stringify(J.Obj("theme", Theme, "notifications", Notifications, "background", Background, "start_with_windows", StartWithWindows, "all_networks", AllNetworks))); if (File.Exists(Path)) File.Replace(temporary, Path, null); else File.Move(temporary, Path); }
        public static string Color(string color)
        {
            string key = color.ToUpperInvariant();
            // Legacy page colors feed one semantic palette, including dialogs and chat.
            switch (key)
            {
                case "#17263C": case "#E5ECF8": return Dark ? "#F3F3F5" : "#202445";
                case "#64738A": case "#52647C": case "#A3B2CC": return Dark ? "#B2B3BD" : "#646879";
                case "#F4F6FA": case "#101A2B": return Dark ? "#1C1C1E" : "#F7F7F7";
                case "#F3F3F3": return Dark ? "#202022" : "#F3F3F3";
                case "#19253A": return Dark ? "#272729" : "#FFFFFF";
                case "#E5EAF3": case "#D6DFEC": case "#34425B": return Dark ? "#3D3D42" : "#E1E1E5";
                case "#274CDB": case "#345BEB": case "#9BB4FF": return Dark ? "#82AAFF" : "#0057FF";
                case "#0D7959": return Dark ? "#65D3B5" : "#007D68";
                case "#E4F5EE": return Dark ? "#193D34" : "#E4F5EE";
                case "#926410": return Dark ? "#F0C079" : "#8B5A0A";
                case "#B5333D": return Dark ? "#FFA4AC" : "#B22D3B";
                case "#EEF3FF": case "#233654": return Dark ? "#28364E" : "#EDF2FF";
                case "#152137": return Dark ? "#2D2D30" : "#FFFFFF";
                default: return color;
            }
        }
    }
    public sealed partial class MainWindow
    {
        private System.Windows.Forms.NotifyIcon tray;
        private DispatcherTimer notificationTimer;
        private bool exiting, pollingNotifications;
        private int notificationEpoch;
        private static SolidColorBrush Surface { get { return Brush("#19253A"); } }
        private void ConfigureDesktopNotifications()
        {
            if (notificationTimer == null)
            {
                notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
                notificationTimer.Tick += async delegate { await PollNotifications(); };
                Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!exiting && UiPreferences.Background && api.SignedIn && !preview) { e.Cancel = true; Hide(); } };
                Closed += delegate { notificationTimer.Stop(); if (tray != null) { var icon = tray.Icon; tray.Dispose(); if (icon != null) icon.Dispose(); } };
            }
            if ((UiPreferences.Notifications || UiPreferences.Background || backgroundLaunch) && !preview)
            {
                if (tray == null)
                {
                    tray = new System.Windows.Forms.NotifyIcon { Icon = ApplicationIcon.CreateTrayIcon(), Text = "eero Desktop", Visible = true };
                    var menu = new System.Windows.Forms.ContextMenuStrip();
                    menu.Items.Add("Open eero Desktop", null, delegate { Dispatcher.BeginInvoke(new Action(RestoreWindow)); });
                    menu.Items.Add("Exit", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { exiting = true; Close(); })); }); tray.ContextMenuStrip = menu;
                    tray.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(RestoreWindow)); };
                    tray.BalloonTipClicked += delegate { Dispatcher.BeginInvoke(new Action(RestoreWindow)); };
                }
                tray.Visible = true;
            }
            else if (tray != null) tray.Visible = false;
            UpdateTrayAppearance();
            if (UiPreferences.Notifications && !preview) notificationTimer.Start(); else notificationTimer.Stop();
        }
        internal void UpdateTrayAppearance()
        {
            if (tray == null || tray.ContextMenuStrip == null) return;
            var menu = tray.ContextMenuStrip;
            menu.Renderer = UiPreferences.Dark ? new System.Windows.Forms.ToolStripProfessionalRenderer(new DarkTrayColors()) : new System.Windows.Forms.ToolStripProfessionalRenderer();
            menu.BackColor = System.Drawing.ColorTranslator.FromHtml(UiPreferences.Dark ? "#272729" : "#FFFFFF");
            menu.ForeColor = System.Drawing.ColorTranslator.FromHtml(UiPreferences.Dark ? "#F3F3F5" : "#202445");
            foreach (System.Windows.Forms.ToolStripItem item in menu.Items) item.ForeColor = menu.ForeColor;
        }
        private sealed class DarkTrayColors : System.Windows.Forms.ProfessionalColorTable
        {
            private static System.Drawing.Color SurfaceColor { get { return System.Drawing.Color.FromArgb(39, 39, 41); } }
            private static System.Drawing.Color HoverColor { get { return System.Drawing.Color.FromArgb(61, 61, 66); } }
            public override System.Drawing.Color ToolStripDropDownBackground { get { return SurfaceColor; } }
            public override System.Drawing.Color ImageMarginGradientBegin { get { return SurfaceColor; } }
            public override System.Drawing.Color ImageMarginGradientMiddle { get { return SurfaceColor; } }
            public override System.Drawing.Color ImageMarginGradientEnd { get { return SurfaceColor; } }
            public override System.Drawing.Color MenuBorder { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemBorder { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemSelected { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemSelectedGradientBegin { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemSelectedGradientEnd { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemPressedGradientBegin { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemPressedGradientMiddle { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemPressedGradientEnd { get { return HoverColor; } }
        }
        private async Task PollNotifications()
        {
            if (pollingNotifications || busy || !api.SignedIn || String.IsNullOrEmpty(networkUrl) || !UiPreferences.Notifications) return;
            pollingNotifications = true; int epoch = notificationEpoch;
            try
            {
                if (notificationJournal == null) notificationJournal = new NotificationJournal(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EeroDesktop", "notifications-state.dat"));
                string owner = J.First(account, "id", "user_id", "email", "phone");
                var targets = UiPreferences.AllNetworks ? networkList.ToList() : new List<object> { J.Obj("url", networkUrl, "name", J.S(network, "name")) };
                foreach (object target in targets)
                {
                    if (!UiPreferences.Notifications || !api.SignedIn || epoch != notificationEpoch) return;
                    string url = J.S(target, "url"); if (String.IsNullOrEmpty(url)) continue;
                    object data;
                    try { data = await api.Send("GET", url.TrimEnd('/') + "/notifications_history?limit=50"); }
                    catch (ApiException e) { if (e.SessionExpired) throw; continue; }
                    if (!api.SignedIn || epoch != notificationEpoch) return;
                    var fresh = notificationJournal.Observe(owner, url, Entries(data, "notifications", "items"));
                    if (fresh.Count == 0 || tray == null || !UiPreferences.Notifications) continue;
                    object latest = fresh.Last();
                    string title = fresh.Count == 1 ? J.S(latest, "title").NullIfEmpty() ?? "eero network notification" : fresh.Count + " eero network updates";
                    string body = (J.S(target, "name").NullIfEmpty() ?? "Your network") + ": " + (J.S(latest, "body").NullIfEmpty() ?? "Open eero Desktop to view the update.");
                    tray.BalloonTipTitle = title.Length > 63 ? title.Substring(0, 63) : title; tray.BalloonTipText = body.Length > 240 ? body.Substring(0, 240) : body; tray.ShowBalloonTip(5000);
                }
                notificationJournal.Save();
            }
            catch (ApiException e) { if (e.SessionExpired) { api.Forget(); ClearData(); ShowLogin(false); SetNotice("Sign in again to restore network alerts.", true); SignInAttention(); } }
            catch (Exception) { }
            finally { pollingNotifications = false; }
        }
        private void AppearancePage()
        {
            Sub("Choose how eero Desktop looks on this Windows account.");
            var body = new StackPanel(); body.Children.Add(Text("App theme", 20, "#17263C", true));
            body.Children.Add(Text("Use your Windows preference, or choose a light or dark appearance.", 13, "#64738A"));
            var choices = new WrapPanel { Margin = new Thickness(0, 16, 0, 10) };
            foreach (string value in new[] { "System", "Light", "Dark" })
            {
                string selected = value;
                var choice = new RadioButton { Content = value == "System" ? "Use Windows setting" : value, GroupName = "AppTheme", IsChecked = UiPreferences.Theme == value };
                System.Windows.Automation.AutomationProperties.SetName(choice, value + " theme");
                choice.Checked += delegate { UiPreferences.Theme = selected; UiPreferences.Save(); Program.ApplyTheme(); Background = Brush("#F4F6FA"); Foreground = Brush("#17263C"); ShowShell(); };
                choices.Children.Add(choice);
            }
            body.Children.Add(choices); page.Children.Add(Card(body));
            var examples = Columns();
            for (int i = 0; i < 2; i++)
            {
                bool dark = i == 1; var sample = new StackPanel { Margin = new Thickness(14) };
                sample.Children.Add(new TextBlock { Text = "eero", FontFamily = new FontFamily("Segoe UI Light"), FontSize = 25, Foreground = dark ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.MidnightBlue });
                sample.Children.Add(new TextBlock { Text = dark ? "Dark appearance" : "Light appearance", FontSize = 12, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#B2B3BD" : "#646879")), Margin = new Thickness(0, 2, 0, 12) });
                var mini = new Grid(); mini.ColumnDefinitions.Add(new ColumnDefinition()); mini.ColumnDefinitions.Add(new ColumnDefinition());
                for (int j = 0; j < 2; j++) { var content = new StackPanel { Margin = new Thickness(12) }; content.Children.Add(new TextBlock { Text = j == 0 ? "Internet" : "Devices", FontSize = 12, Foreground = dark ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.MidnightBlue }); content.Children.Add(new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#82AAFF" : "#0057FF")), Width = j == 0 ? 65 : 24, Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) }); var tile = new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#272729" : "#FFFFFF")), BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#3D3D42" : "#E1E1E5")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = content, Margin = new Thickness(0, 0, j == 0 ? 8 : 0, 0) }; Grid.SetColumn(tile, j); mini.Children.Add(tile); }
                sample.Children.Add(mini);
                var frame = new Border { Child = sample, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#202022" : "#EEEEF1")), Margin = new Thickness(i == 0 ? 0 : 10, 0, i == 0 ? 10 : 0, 0), Height = 180 };
                Grid.SetColumn(frame, i); examples.Children.Add(frame);
            }
            page.Children.Add(examples);
        }
    }
}
