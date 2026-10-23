using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private ScrollViewer pageScroll;
        private string renderedRoute;

        private static TextBlock Glyph(string glyph, double size = 20, string color = "#64738A")
        {
            return new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size,
                Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }

        private static string RouteIcon(string route)
        {
            string title = route.Split('/').Last().ToLowerInvariant();
            if (title.Contains("password") || title.Contains("wifi") || title.Contains("wireless")) return "\uE701";
            if (title.Contains("profile") || title.Contains("permissions") || title.Contains("account")) return "\uE77B";
            if (title.Contains("security") || title.Contains("filter") || title.Contains("protection") || title.Contains("vpn")) return "\uEA18";
            if (title.Contains("internet") || title.Contains("wan")) return "\uE774";
            if (title.Contains("activity") || title.Contains("usage") || title.Contains("analytics")) return "\uE9D9";
            if (title.Contains("eero") || title.Contains("device") || title.Contains("setup")) return "\uE950";
            if (title.Contains("notification")) return "\uEA8F";
            if (title.Contains("appearance")) return "\uE790";
            if (title.Contains("update")) return "\uE895";
            if (title.Contains("help") || title.Contains("support") || title.Contains("troubleshoot")) return "\uE897";
            if (title.Contains("about")) return "\uE946";
            if (title.Contains("chat")) return "\uE8F2";
            if (title.Contains("dns") || title.Contains("dhcp") || title.Contains("network")) return "\uE968";
            return "\uE713";
        }

        private Button ToolButton(string icon, string label, Action action)
        {
            var b = LocalButton(label, action); b.Content = Glyph(icon); b.Width = 36; b.Height = 34;
            b.Padding = new Thickness(0); b.Margin = new Thickness(0, 0, 6, 0); b.ToolTip = label;
            System.Windows.Automation.AutomationProperties.SetName(b, label); return b;
        }

        private void AddNavigation(string title, string route, string icon)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var glyph = Glyph(icon, 18, "#17263C"); row.Children.Add(glyph);
            var text = Text(title, 14); text.Margin = new Thickness(8, 0, 0, 0); text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, 1); row.Children.Add(text);
            var button = LocalButton(title, delegate { Navigate(route); }); button.Content = row; button.Tag = route;
            button.Style = (Style)FindResource("NavigationButton");
            System.Windows.Automation.AutomationProperties.SetName(button, title); navigation.Children.Add(button);
        }

        private void UpdateNavigation()
        {
            string selected = currentPage.StartsWith("Home/Profiles") ? "Home/Profiles" : currentPage.StartsWith("Home/Security & privacy") ? "Home/Security & privacy" : FeatureCatalog.Root(currentPage);
            foreach (Button button in navigation.Children.OfType<Button>())
            {
                bool active = (string)button.Tag == selected;
                button.Style = (Style)FindResource(active ? "SelectedNavigationButton" : "NavigationButton");
                var grid = button.Content as Grid;
                if (grid != null) foreach (TextBlock text in grid.Children.OfType<TextBlock>()) { text.Foreground = Brush(active ? "#274CDB" : "#17263C"); text.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal; }
            }
        }

        private void NativeShell()
        {
            root = new Grid { Background = Brush("#F3F3F3"), UseLayoutRounding = true };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(212) }); root.ColumnDefinitions.Add(new ColumnDefinition()); Content = root;
            var rail = new Grid(); rail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); rail.RowDefinitions.Add(new RowDefinition()); rail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.Children.Add(rail);
            var brand = new StackPanel { Margin = new Thickness(24, 16, 0, 28) };
            var wordmark = Text("eero", 42, "#17263C"); wordmark.FontFamily = new FontFamily("Segoe UI Light"); wordmark.Margin = new Thickness(0); brand.Children.Add(wordmark);
            brand.Children.Add(Text("Desktop for Windows", 11, "#64738A")); rail.Children.Add(brand);
            navigation = new StackPanel { Margin = new Thickness(12, 0, 12, 0) }; Grid.SetRow(navigation, 1); rail.Children.Add(navigation);
            AddNavigation("Home", "Home", "\uE80F"); AddNavigation("Devices", "Devices", "\uE772"); AddNavigation("Activity", "Activity", "\uE9D9"); AddNavigation("Settings", "Settings", "\uE713");
            navigation.Children.Add(new Border { Height = 1, Background = Brush("#E5EAF3"), Margin = new Thickness(12, 16, 12, 16) });
            AddNavigation("Profiles", "Home/Profiles", "\uE716"); AddNavigation("Security & privacy", "Home/Security & privacy", "\uEA18"); AddNavigation("Help & support", "Help", "\uE897");
            var accountPanel = new StackPanel { Margin = new Thickness(20, 12, 20, 16) }; Grid.SetRow(accountPanel, 2); rail.Children.Add(accountPanel);
            var accountButton = LocalButton("Account settings", delegate { Navigate("Settings/Account settings"); });
            accountButton.Style = (Style)FindResource("NavigationButton"); accountButton.Margin = new Thickness(-8, 0, -8, 4);
            var accountRow = new Grid(); accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); accountRow.ColumnDefinitions.Add(new ColumnDefinition()); accountRow.Children.Add(Glyph("\uE77B", 18));
            accountLabel = Text(preview ? "Preview account" : J.First(account, "name", "email", "phone"), 12); accountLabel.TextWrapping = TextWrapping.NoWrap; accountLabel.TextTrimming = TextTrimming.CharacterEllipsis; accountLabel.VerticalAlignment = VerticalAlignment.Center; accountLabel.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(accountLabel, 1); accountRow.Children.Add(accountLabel); accountButton.Content = accountRow; accountPanel.Children.Add(accountButton);
            var signout = Button(preview ? "Back to sign in" : "Sign out", async delegate { timer.Stop(); CloseAccountWindows(); if (!preview) await api.Logout(); preview = false; ClearData(); ShowLogin(false); SetNotice("Signed out. Your saved session has been cleared."); });
            signout.Style = (Style)FindResource("QuietButton"); signout.Margin = new Thickness(0, 0, 0, 12); accountPanel.Children.Add(signout);
            accountPanel.Children.Add(Text("Unofficial client · v1.0", 10, "#64738A"));

            var main = new Grid(); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition()); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var contentFrame = new Border { Background = Brush("#F4F6FA"), BorderBrush = Brush("#E5EAF3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 8, 8, 8), Child = main }; Grid.SetColumn(contentFrame, 1); root.Children.Add(contentFrame);
            var header = new Grid { Margin = new Thickness(28, 20, 24, 12) }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var networkRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; networkRow.Children.Add(Glyph("\uE701", 20, "#274CDB"));
            networks = new ComboBox { Width = 230, Margin = new Thickness(12, 0, 16, 0), HorizontalAlignment = HorizontalAlignment.Left, DisplayMemberPath = "Name", SelectedValuePath = "Url" };
            System.Windows.Automation.AutomationProperties.SetName(networks, "Selected network");
            foreach (object n in networkList) networks.Items.Add(new NetworkChoice { Name = J.First(n, "name", "url"), Url = J.S(n, "url") });
            networks.SelectedValue = networkUrl; networkRow.Children.Add(networks);
            var connection = Text(preview ? "Sample network" : "eero network", 12, "#64738A"); connection.Margin = new Thickness(0); connection.VerticalAlignment = VerticalAlignment.Center; networkRow.Children.Add(connection); header.Children.Add(networkRow);
            networks.SelectionChanged += async delegate
            {
                if (busy || loading || networks.SelectedItem == null) return;
                var choice = (NetworkChoice)networks.SelectedItem; if (networkUrl == choice.Url) return;
                await Run(async delegate { CloseAccountWindows(); networkUrl = choice.Url; network = selectedProfile = selectedNode = null; currentPage = "Home"; devices.Clear(); nodes.Clear(); profiles.Clear(); featureData.Clear(); notificationEpoch++; if (!preview) await LoadNetwork(); RenderPage(); }, "Loading network…");
            };
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(toolbar, 1); header.Children.Add(toolbar);
            var refresh = Button("Refresh", async delegate { if (!preview) { if (network == null) { await LoadAccount(); ShowShell(); } else await LoadNetwork(); } if (currentPage.Contains("/")) await LoadFeature(currentPage); RenderPage(); ReportStatus("dashboard_loaded"); });
            refresh.Content = Glyph("\uE72C", 17); refresh.Width = 36; refresh.Height = 34; refresh.Padding = new Thickness(0); refresh.Margin = new Thickness(0, 0, 6, 0); refresh.ToolTip = "Refresh network"; System.Windows.Automation.AutomationProperties.SetName(refresh, "Refresh network"); toolbar.Children.Add(refresh);
            toolbar.Children.Add(ToolButton("\uEA8F", "Notifications", delegate { Navigate("Settings/Notifications"); }));
            toolbar.Children.Add(ToolButton("\uE710", "Add or replace eero", delegate { Navigate("Home/Add or replace eero"); })); main.Children.Add(header);
            pageScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            page = new StackPanel { Margin = new Thickness(28, 10, 28, 24) }; pageScroll.Content = page; Grid.SetRow(pageScroll, 1); main.Children.Add(pageScroll);
            notice = Text("", 11, "#64738A"); notice.Margin = new Thickness(28, 10, 24, 10);
            var statusBar = new Border { BorderBrush = Brush("#E5EAF3"), BorderThickness = new Thickness(0, 1, 0, 0), Child = notice }; Grid.SetRow(statusBar, 2); main.Children.Add(statusBar);
            controls = root; renderedRoute = null; RenderPage(); if (!preview) timer.Start();
        }

        private void PageHeading()
        {
            if (renderedRoute != currentPage && pageScroll != null) pageScroll.ScrollToTop(); renderedRoute = currentPage;
            if (currentPage.Contains("/"))
            {
                var crumbs = new WrapPanel { Margin = new Thickness(-8, -4, 0, 12) }; string[] parts = currentPage.Split('/'); string route = "";
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    route += (i == 0 ? "" : "/") + parts[i]; string target = route;
                    var crumb = LocalButton(parts[i], delegate { Navigate(target); }); crumb.Style = (Style)FindResource("QuietButton"); crumb.Padding = new Thickness(8, 4, 8, 4); crumb.Margin = new Thickness(0); crumbs.Children.Add(crumb);
                    var chevron = Glyph("\uE76C", 10); chevron.Margin = new Thickness(2, 0, 2, 0); crumbs.Children.Add(chevron);
                }
                page.Children.Add(crumbs);
            }
            string heading = currentPage.Split('/').Last(); if (heading == "Details" && selectedNode != null) heading = J.First(selectedNode, "location", "model"); if (heading == "Profile" && selectedProfile != null) heading = J.S(selectedProfile, "name");
            var title = Text(heading, 30, "#17263C", true); title.Margin = new Thickness(0, 0, 0, 6); page.Children.Add(title);
        }

        private static Grid Columns(double first = 1, double second = 1)
        {
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(first, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(second, GridUnitType.Star) }); return grid;
        }
        private static StackPanel Column(Grid grid, int column)
        {
            var panel = new StackPanel { Margin = column == 0 ? new Thickness(0, 0, 12, 0) : new Thickness(12, 0, 0, 0) }; Grid.SetColumn(panel, column); grid.Children.Add(panel); return panel;
        }
        private Button RouteRow(string title, string subtitle, string route, Action action = null)
        {
            var b = LocalButton(title, action ?? delegate { Navigate(route); }); b.Style = (Style)FindResource("SettingsRowButton");
            var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            var icon = Glyph(RouteIcon(route), 20, "#274CDB"); icon.HorizontalAlignment = HorizontalAlignment.Left; content.Children.Add(icon);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var label = Text(title, 14, "#17263C", true); label.Margin = new Thickness(0); words.Children.Add(label);
            if (!String.IsNullOrEmpty(subtitle)) { var caption = Text(subtitle, 12, "#64738A"); caption.Margin = new Thickness(0, 4, 12, 0); words.Children.Add(caption); }
            Grid.SetColumn(words, 1); content.Children.Add(words); var chevron = Glyph("\uE76C", 12); Grid.SetColumn(chevron, 2); content.Children.Add(chevron); b.Content = content;
            System.Windows.Automation.AutomationProperties.SetName(b, title); return b;
        }
        private void NativeFeatureMenu(string parent, bool explanation)
        {
            if (explanation) Sub(parent == "Settings" ? "Manage your network, account, and desktop preferences." : "Choose a setting to view details and make changes.");
            var entries = FeatureCatalog.Children(parent).ToList();
            var board = Columns();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i % 2 == 0) board.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                FeatureEntry item = entries[i]; var button = RouteRow(item.Title, item.Description, item.Path);
                button.Margin = new Thickness(i % 2 == 0 ? 0 : 8, 0, i % 2 == 0 ? 8 : 0, 12); Grid.SetColumn(button, i % 2); Grid.SetRow(button, i / 2); board.Children.Add(button);
            }
            page.Children.Add(board);
        }

        private static bool IsOnline(object data)
        {
            string status = J.S(data, "health", "internet", "status").NullIfEmpty() ?? J.S(data, "status");
            return status == "connected" || status == "green" || status == "online";
        }
        private Border StatusBadge(string text, bool online)
        {
            var label = Text((online ? "●  " : "○  ") + text, 11, online ? "#0D7959" : "#64738A", true); label.Margin = new Thickness(0);
            return new Border { Background = Brush(online ? "#E4F5EE" : "#F3F3F3"), CornerRadius = new CornerRadius(4), Padding = new Thickness(9, 4, 9, 4), Child = label, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        }
        private void NativeOverview()
        {
            var heading = new Grid { Margin = new Thickness(0, 0, 0, 18) }; heading.ColumnDefinitions.Add(new ColumnDefinition());
            var summary = Text("Your network at a glance", 14, "#64738A"); summary.Margin = new Thickness(0); summary.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(summary);
            page.Children.Add(heading);
            var columns = Columns(1.45, 1); var left = Column(columns, 0); var right = Column(columns, 1); page.Children.Add(columns);
            var internet = new StackPanel(); var internetTop = new Grid(); internetTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) }); internetTop.ColumnDefinitions.Add(new ColumnDefinition()); internetTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); internetTop.Children.Add(Glyph("\uE774", 28, "#274CDB"));
            var internetTitle = Text("Internet", 20, "#17263C", true); internetTitle.Margin = new Thickness(4, 0, 0, 0); internetTitle.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(internetTitle, 1); internetTop.Children.Add(internetTitle);
            var badge = StatusBadge(IsOnline(network) ? "Online" : "Check connection", IsOnline(network)); Grid.SetColumn(badge, 2); internetTop.Children.Add(badge); internet.Children.Add(internetTop);
            internet.Children.Add(new Border { Height = 1, Background = Brush("#E5EAF3"), Margin = new Thickness(0, 16, 0, 16) });
            var speeds = Columns();
            foreach (var metric in new[] { new[] { "↓  Download", Speed("down") }, new[] { "↑  Upload", Speed("up") } }.Select((item, index) => new { item, index }))
            {
                var stack = new StackPanel(); stack.Children.Add(Text(metric.item[0], 12, "#64738A")); var value = Text(metric.item[1], 28, "#17263C", true); value.Margin = new Thickness(0); stack.Children.Add(value); Grid.SetColumn(stack, metric.index); speeds.Children.Add(stack);
            }
            internet.Children.Add(speeds); internet.Children.Add(Text("Latest measured internet speed", 11, "#64738A"));
            var speedActions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) }; speedActions.Children.Add(Button("Run speed test", StartSpeedTest, true)); speedActions.Children.Add(Section("Internet details", "Home/Internet")); internet.Children.Add(speedActions); left.Children.Add(Card(internet));
            var mesh = new StackPanel(); var meshHeading = new Grid { Margin = new Thickness(0, 0, 0, 16) }; meshHeading.ColumnDefinitions.Add(new ColumnDefinition()); meshHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); var meshTitle = Text("Your eeros", 18, "#17263C", true); meshTitle.Margin = new Thickness(0); meshHeading.Children.Add(meshTitle); var meshCount = Text(nodes.Count + " on this network", 12, "#64738A"); meshCount.VerticalAlignment = VerticalAlignment.Center; meshCount.Margin = new Thickness(0); Grid.SetColumn(meshCount, 1); meshHeading.Children.Add(meshCount); mesh.Children.Add(meshHeading);
            foreach (object node in nodes) mesh.Children.Add(CompactNode(node));
            if (nodes.Count == 0) mesh.Children.Add(Text(loadErrors.ContainsKey("eero nodes") ? "eero information could not be loaded. Refresh to try again." : "No eeros were returned."));
            left.Children.Add(Card(mesh, 18));
            left.Children.Add(RouteRow("Chat with eero", "Get help with your network", "Home/Chat"));
            var deviceSummary = new StackPanel(); deviceSummary.Children.Add(Text("Devices", 20, "#17263C", true)); var counts = Columns();
            var online = new StackPanel(); online.Children.Add(Text(loadErrors.ContainsKey("Devices") ? "—" : devices.Count(d => J.B(d, "connected")).ToString(), 32, "#17263C", true)); online.Children.Add(Text("Online", 12, "#0D7959")); counts.Children.Add(online);
            var offline = new StackPanel(); offline.Children.Add(Text(loadErrors.ContainsKey("Devices") ? "—" : devices.Count(d => !J.B(d, "connected")).ToString(), 32, "#17263C", true)); offline.Children.Add(Text("Offline", 12, "#64738A")); Grid.SetColumn(offline, 1); counts.Children.Add(offline); deviceSummary.Children.Add(counts); deviceSummary.Children.Add(Section("View all devices", "Devices")); right.Children.Add(Card(deviceSummary));
            var people = new StackPanel(); people.Children.Add(Text("Profiles", 20, "#17263C", true));
            foreach (object item in profiles.Take(3)) { object profile = item; var row = RouteRow(J.S(profile, "name"), ProfileSummary(profile), "Home/Profiles", delegate { selectedProfile = profile; Navigate("Home/Profiles/Profile"); }); row.BorderThickness = new Thickness(0); row.Padding = new Thickness(0, 10, 0, 10); row.MinHeight = 62; row.Margin = new Thickness(0, 0, 0, 4); people.Children.Add(row); }
            if (profiles.Count == 0) people.Children.Add(Text("Create profiles to manage devices and scheduled pauses.", 12, "#64738A"));
            var manageProfiles = Section("Manage profiles", "Home/Profiles"); manageProfiles.Style = (Style)FindResource("QuietButton"); manageProfiles.HorizontalAlignment = HorizontalAlignment.Left; manageProfiles.Margin = new Thickness(-8, 2, 0, -4); people.Children.Add(manageProfiles); right.Children.Add(Card(people));
            right.Children.Add(RouteRow("Security & privacy", "Protection, content filters, and privacy", "Home/Security & privacy"));
            right.Children.Add(RouteRow("Internet backup", "Keep your network connected", "Settings/Advanced networking/Internet backup"));
            foreach (string key in loadErrors.Keys) Availability(key);
        }

        private Border NativeNodeCard(object node)
        {
            bool online = IsOnline(node); var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(HardwareImage(node, 36)); var words = new StackPanel(); words.Children.Add(Text(J.First(node, "location", "model"), 15, "#17263C", true)); var caption = Text((J.B(node, "gateway") ? "Gateway · " : "") + J.S(node, "model"), 12, "#64738A"); caption.Margin = new Thickness(0); words.Children.Add(caption); Grid.SetColumn(words, 1); row.Children.Add(words);
            var badge = StatusBadge(online ? "Online" : (J.S(node, "status").NullIfEmpty() ?? "Unknown"), online); Grid.SetColumn(badge, 2); row.Children.Add(badge);
            var stack = new StackPanel(); stack.Children.Add(row);
            var details = Text((J.First(node, "ip_address", "ip").NullIfEmpty() ?? "IP unavailable") + "  ·  " + (J.S(node, "connected_clients_count").NullIfEmpty() ?? "—") + " connected devices", 12, "#64738A"); details.Margin = new Thickness(42, 12, 0, 12); stack.Children.Add(details);
            var actions = new WrapPanel { Margin = new Thickness(42, 0, 0, -6) }; var open = Button("View details", async delegate { selectedNode = preview ? node : await Read(J.S(node, "url")); currentPage = "Home/eeros/Details"; RenderPage(); }); open.Style = (Style)FindResource("QuietButton"); actions.Children.Add(open);
            var rename = Button("Rename", async delegate { string name = Prompt("eero location", J.S(node, "location")); if (name == null) return; ValidateName(name); await Change("PUT", J.S(node, "url"), J.Obj("location", name.Trim()), true, "eero location updated."); }); rename.Style = (Style)FindResource("QuietButton"); actions.Children.Add(rename);
            var menu = new ContextMenu(); var restart = new MenuItem { Header = "Restart eero" }; restart.Click += async delegate { await Run(async delegate { if (!Confirm("Restart " + J.First(node, "location", "model") + "? Connected devices may briefly lose internet access.")) return; if (preview) throw new InvalidOperationException("Preview mode is read only."); await api.Send("POST", J.S(node, "url") + "/reboot"); SetNotice("Restart requested. Wait a few minutes, then refresh."); }, "Restarting eero…"); }; menu.Items.Add(restart);
            var more = LocalButton("More", delegate { menu.IsOpen = true; }); more.Style = (Style)FindResource("QuietButton"); more.ContextMenu = menu; menu.PlacementTarget = more; actions.Children.Add(more); stack.Children.Add(actions); return Card(stack, 20);
        }

        private void NativeDevicePage()
        {
            Sub("Find devices and inspect their connection details."); Availability("Devices");
            var tools = new Grid { Margin = new Thickness(0, 0, 0, 16) }; tools.ColumnDefinitions.Add(new ColumnDefinition()); tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var search = new TextBox { Margin = new Thickness(0, 0, 12, 0), ToolTip = "Search by name, IP, MAC, or manufacturer", MaxLength = 254 }; System.Windows.Automation.AutomationProperties.SetName(search, "Search devices");
            var searchHost = new Grid(); searchHost.Children.Add(search); var hint = Text("Search devices by name, MAC or IP", 13, "#64738A"); hint.Margin = new Thickness(12, 0, 28, 0); hint.VerticalAlignment = VerticalAlignment.Center; hint.IsHitTestVisible = false; searchHost.Children.Add(hint); tools.Children.Add(searchHost);
            var statusFilter = new ComboBox { ItemsSource = new[] { "All devices", "Online", "Offline", "Paused", "Blocked" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 0) }; System.Windows.Automation.AutomationProperties.SetName(statusFilter, "Device status filter"); Grid.SetColumn(statusFilter, 1); tools.Children.Add(statusFilter);
            var blocked = Section("Blocked devices", "Devices/Blocked devices"); blocked.Margin = new Thickness(0); Grid.SetColumn(blocked, 2); tools.Children.Add(blocked); page.Children.Add(tools);
            var workspace = new Grid(); workspace.ColumnDefinitions.Add(new ColumnDefinition()); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            var grid = new DataGrid { Height = 440, SelectionMode = DataGridSelectionMode.Single, Margin = new Thickness(0), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
            foreach (string name in new[] { "Name", "Status", "IP", "Connection" }) grid.Columns.Add(new DataGridTextColumn { Header = name == "IP" ? "IP address" : name, Binding = new System.Windows.Data.Binding(name), Width = new DataGridLength(name == "Name" ? 1.6 : 1, DataGridLengthUnitType.Star), MinWidth = name == "Name" ? 120 : name == "IP" ? 110 : name == "Connection" ? 100 : 75 });
            var table = Card(grid, 0); table.Margin = new Thickness(0, 0, 20, 0); workspace.Children.Add(table);
            var details = new StackPanel(); var detailCard = Card(details, 20); detailCard.Margin = new Thickness(0); Grid.SetColumn(detailCard, 1); workspace.Children.Add(detailCard); page.Children.Add(workspace);
            var count = Text("", 12, "#64738A"); count.Margin = new Thickness(0, 12, 0, 0); page.Children.Add(count);
            Action updateDetails = delegate
            {
                details.Children.Clear(); var selected = grid.SelectedItem as DeviceRow;
                if (selected == null) { details.Children.Add(Glyph("\uE772", 32)); details.Children.Add(Text("Device details", 18, "#17263C", true)); details.Children.Add(Text("Select a device to view its connection and profile.", 13, "#64738A")); return; }
                details.Children.Add(Text(selected.Name, 20, "#17263C", true)); details.Children.Add(StatusBadge(selected.Status, selected.Status == "Online")); details.Children.Add(new Border { Height = 20 });
                Detail(details, "Connection", selected.Connection); Detail(details, "IP address", selected.IP); Detail(details, "MAC address", J.S(selected.Raw, "mac")); Detail(details, "Connected eero", J.S(selected.Raw, "source", "location")); Detail(details, "Profile", selected.Profile.NullIfEmpty() ?? "Unassigned");
                var manage = LocalButton("Manage device", delegate { DeviceDialog(selected.Raw); }); manage.Margin = new Thickness(0, 12, 0, 0); details.Children.Add(manage);
            };
            Action filter = delegate
            {
                string q = search.Text.Trim(); hint.Visibility = q.Length == 0 && !search.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
                var selection = grid.SelectedItem as DeviceRow; var rows = devices.Where(d => (statusFilter.SelectedIndex == 0 || Device(d).Status == (string)statusFilter.SelectedItem) && (q.Length == 0 || (Device(d).Name + " " + Device(d).IP + " " + J.S(d, "mac") + " " + J.S(d, "manufacturer")).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).Select(Device).OrderBy(d => d.Status != "Online").ThenBy(d => d.Name).ToList();
                grid.ItemsSource = rows; grid.SelectedItem = selection == null ? rows.FirstOrDefault() : rows.FirstOrDefault(d => d.Raw == selection.Raw) ?? rows.FirstOrDefault();
                count.Text = rows.Count + (rows.Count == 1 ? " device" : " devices") + " · " + devices.Count(d => J.B(d, "connected")) + " online" + (rows.Count == 0 ? " · No devices match your search." : ""); updateDetails();
            };
            search.TextChanged += delegate { filter(); }; search.GotKeyboardFocus += delegate { hint.Visibility = Visibility.Collapsed; }; search.LostKeyboardFocus += delegate { hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }; statusFilter.SelectionChanged += delegate { filter(); };
            grid.SelectionChanged += delegate { updateDetails(); }; grid.MouseDoubleClick += delegate { var selected = grid.SelectedItem as DeviceRow; if (!busy && selected != null) DeviceDialog(selected.Raw); }; filter();
        }
    }
}
