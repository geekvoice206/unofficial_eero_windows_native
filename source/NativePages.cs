using System;
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
        private void NativeProfiles()
        {
            Sub("Organize devices, manage internet access, and set scheduled pauses."); Availability("Profiles");
            if (J.S(network, "connection", "mode") == "bridged") { page.Children.Add(Card(Text("Family profiles are unavailable while eero is in bridge mode."))); return; }
            var create = LocalButton("Add a profile", delegate { Edit("Create profile", new[] { new Field("name", "Profile name") }, async v => { ValidateName(V(v, "name")); await Change("POST", NetResource("profiles"), J.Obj("name", V(v, "name").Trim(), "paused", false, "devices", new object[0]), false, "Profile created."); }, "Create"); }); create.Style = (Style)FindResource("AccentButton"); page.Children.Add(create);
            var board = Columns(); int index = 0;
            foreach (object raw in profiles)
            {
                object item = raw; if (index % 2 == 0) board.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var body = new StackPanel(); var header = new StackPanel { Orientation = Orientation.Horizontal };
                var initial = Text(J.S(item, "name").Length > 0 ? J.S(item, "name").Substring(0, 1).ToUpperInvariant() : "P", 20, "#274CDB", true); initial.Margin = new Thickness(0); initial.HorizontalAlignment = HorizontalAlignment.Center; initial.VerticalAlignment = VerticalAlignment.Center;
                header.Children.Add(new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Brush(UiPreferences.Dark ? "#233654" : "#EEF3FF"), Child = initial, Margin = new Thickness(0, 0, 14, 0) });
                var words = new StackPanel(); words.Children.Add(Text(J.S(item, "name"), 20, "#17263C", true)); var caption = Text(ProfileSummary(item), 12, "#64738A"); caption.Margin = new Thickness(0); words.Children.Add(caption); header.Children.Add(words); body.Children.Add(header);
                var open = Button("Manage profile", async delegate { selectedProfile = preview ? item : await Read(J.S(item, "url")); currentPage = "Home/Profiles/Profile"; RenderPage(); }); open.Margin = new Thickness(58, 20, 0, 0); open.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(open);
                var card = Card(body); card.Margin = new Thickness(index % 2 == 0 ? 0 : 8, 12, index % 2 == 0 ? 8 : 0, 4); Grid.SetColumn(card, index % 2); Grid.SetRow(card, index / 2); board.Children.Add(card); index++;
            }
            page.Children.Add(board);
            if (profiles.Count == 0) page.Children.Add(Card(Text("No profiles yet. Add a profile to group devices and manage their internet access.", 14, "#64738A")));
        }

        private void NativeProfileDetail(object profile)
        {
            if (profile == null) { page.Children.Add(Text("Select a profile from the Profiles page.")); return; }
            string url = J.S(profile, "url"); Sub("Manage devices, internet access, and scheduled pauses.");
            var access = Button(J.B(profile, "paused") ? "Resume internet" : "Pause internet", async delegate { if (!Confirm("Change internet access for all devices in this profile?")) return; await Change("PUT", url, J.Obj("paused", !J.B(profile, "paused")), false, "Profile access updated."); }, true);
            ActionRow(access, LocalButton("Rename", delegate { Edit("Profile name", new[] { new Field("name", "Name", J.S(profile, "name")) }, async v => { ValidateName(V(v, "name")); await Change("PUT", url, J.Obj("name", V(v, "name").Trim()), false, "Profile renamed."); }); }), LocalButton("Assign devices", delegate { ProfileDevices(profile); }));
            var columns = Columns(); var left = Column(columns, 0); var right = Column(columns, 1); page.Children.Add(columns);
            var memberDevices = J.List(J.Get(profile, "devices")); var members = new StackPanel(); members.Children.Add(Text("Devices", 20, "#17263C", true));
            if (memberDevices.Count == 0) members.Children.Add(Text("No devices assigned yet.", 13, "#64738A"));
            foreach (object raw in memberDevices) { object device = raw; var row = RouteRow(Device(device).Name, Device(device).Status, "Devices", delegate { DeviceDialog(device); }); row.BorderThickness = new Thickness(0); row.MinHeight = 60; row.Padding = new Thickness(0, 8, 0, 8); row.Margin = new Thickness(0); members.Children.Add(row); }
            left.Children.Add(Card(members));
            var pauses = new StackPanel(); pauses.Children.Add(Text("Scheduled pauses", 20, "#17263C", true));
            string schedulesUrl = Resource(profile, "schedules", url + "/schedules");
            var schedules = J.List(J.Get(profile, "schedule"));
            foreach (object raw in schedules)
            {
                object item = raw; pauses.Children.Add(Text(J.S(item, "name"), 15, "#17263C", true));
                pauses.Children.Add(Text(String.Join(" · ", J.List(J.Get(item, "days")).Select(d => Human(J.S(d).ToLowerInvariant())).ToArray()), 12, "#64738A"));
                pauses.Children.Add(Text(J.S(item, "start") + " – " + J.S(item, "end") + " · " + (J.B(item, "enabled") ? "Enabled" : "Disabled"), 14));
                var actions = new WrapPanel(); actions.Children.Add(LocalButton("Edit", delegate { ScheduleEdit(item, J.S(item, "url")); })); actions.Children.Add(Button("Delete", async delegate { if (!Confirm("Delete this scheduled pause?")) return; await Change("DELETE", J.S(item, "url"), null, false, "Scheduled pause deleted."); })); pauses.Children.Add(actions);
            }
            if (schedules.Count == 0) pauses.Children.Add(Text("No scheduled pauses.", 13, "#64738A"));
            var add = LocalButton("Add scheduled pause", delegate { ScheduleEdit(null, schedulesUrl); }); add.Style = (Style)FindResource("QuietButton"); add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(-8, 8, 0, 0); pauses.Children.Add(add); left.Children.Add(Card(pauses));
            var filters = new StackPanel(); filters.Children.Add(Text("Content filters & app controls", 20, "#17263C", true));
            filters.Children.Add(Text("Choose what devices in this profile can access.", 13, "#64738A"));
            filters.Children.Add(Button("Content filters", async delegate { string policyUrl = ProfilePolicyUrl(profile); object data = await Read(policyUrl); EditPolicies(data, policyUrl); }));
            filters.Children.Add(Button("Block & allow websites", async delegate { object domains = await Read(FeatureUrl("dns_policies/advanced_content_filter")); DomainDialog(domains, profile); }));
            filters.Children.Add(Button("Block apps", async delegate { await BlockApps(profile); }));
            filters.Children.Add(LocalButton("Ad blocking", delegate { Edit("Profile ad blocking", new[] { new Field("enable", "Enable ad blocking", J.B(profile, "premium_dns", "ad_block_settings", "enabled"), "bool") }, async v => { await Change("POST", ProfilePolicyUrl(profile) + "/adblock", J.Obj("enable", Flag(v, "enable")), false, "Profile ad blocking updated."); }); }));
            var details = LocalButton("View filter details", delegate { Info("Profile filter details", J.Get(profile, "premium_dns")); }); details.Style = (Style)FindResource("QuietButton"); details.HorizontalAlignment = HorizontalAlignment.Left; details.Margin = new Thickness(-8, 8, 0, 0); filters.Children.Add(details); right.Children.Add(Card(filters));
            var manage = new StackPanel(); manage.Children.Add(Text("Manage profile", 20, "#17263C", true)); manage.Children.Add(Text("Deleting a profile keeps its devices on your network.", 13, "#64738A"));
            var remove = LocalButton("Delete profile", delegate { Edit("Delete profile", new[] { new Field("confirm", "Type the profile name to confirm") }, async v => { if (V(v, "confirm") != J.S(profile, "name")) throw new ArgumentException("The profile name does not match."); currentPage = "Home/Profiles"; selectedProfile = null; await Change("DELETE", url, null, false, "Profile deleted. Devices remain on your network."); }, "Delete profile"); }); remove.Foreground = Brush("#B5333D"); manage.Children.Add(remove); right.Children.Add(Card(manage));
        }

        private void NativeActivity()
        {
            Sub("Internet performance, data usage, and recent network activity.");
            var columns = Columns(1.2, 1); var left = Column(columns, 0); var right = Column(columns, 1); page.Children.Add(columns);
            var summary = new StackPanel(); summary.Children.Add(Text("Latest internet speed", 20, "#17263C", true));
            var speeds = Columns(); var download = new StackPanel(); download.Children.Add(Text("Download", 12, "#64738A")); download.Children.Add(Text("↓ " + Speed("down"), 28, "#17263C", true)); speeds.Children.Add(download);
            var upload = new StackPanel(); upload.Children.Add(Text("Upload", 12, "#64738A")); upload.Children.Add(Text("↑ " + Speed("up"), 28, "#17263C", true)); Grid.SetColumn(upload, 1); speeds.Children.Add(upload); summary.Children.Add(speeds);
            var actions = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) }; actions.Children.Add(Button("Run speed test", StartSpeedTest, true)); actions.Children.Add(Section("Speed test history", "Home/Internet")); summary.Children.Add(actions); left.Children.Add(Card(summary));
            right.Children.Add(RouteRow("Data usage", "Daily, weekly, and monthly usage by device", "Activity/Data usage"));
            right.Children.Add(RouteRow("Security activity", "Protection and content-filter insights", "Activity/Security activity"));
            right.Children.Add(RouteRow("Network events", "Review recent changes and connection events", "Activity/Network events"));
            object data = null; if (preview) data = PreviewFeature("Activity/Data usage"); else featureData.TryGetValue("Activity/Data usage", out data);
            if (data != null && !(data is Exception))
            {
                page.Children.Add(Text("Data usage", 20, "#17263C", true)); AddSeriesCharts(page, data, true);
            }
        }
    }
}
