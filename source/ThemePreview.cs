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
        // Development-only route, available through --theme-preview / --render.
        private void ThemePreviewPage()
        {
            Sub("Sample controls for visual inspection. This screen does not make network requests.");
            var p = new StackPanel(); p.Children.Add(Text("Fields & choices", 20, "#17263C", true));
            p.Children.Add(new TextBox { Text = "Sample network name" });
            p.Children.Add(new PasswordBox { Password = "sample-password" });
            p.Children.Add(new ComboBox { ItemsSource = new[] { "Automatic", "Manual", "Bridge", "Unavailable option" }, SelectedIndex = 0 });
            var row = new WrapPanel(); row.Children.Add(new CheckBox { Content = "Enabled option", IsChecked = true, Margin = new Thickness(0, 5, 24, 16) }); row.Children.Add(new CheckBox { Content = "Disabled option", IsEnabled = false, IsChecked = true }); p.Children.Add(row);
            var table = new DataGrid { Height = 144, ItemsSource = new[] { new ThemeSampleRow { Name = "Selected device", Status = "Online" }, new ThemeSampleRow { Name = "Alternating row", Status = "Offline" } }, SelectedIndex = 0 };
            foreach (string name in new[] { "Name", "Status" }) table.Columns.Add(new DataGridTextColumn { Header = name, Binding = new System.Windows.Data.Binding(name), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            p.Children.Add(table); page.Children.Add(Card(p));
            ActionRow(LocalButton("Open sample settings form", delegate { Edit("Sample settings", new[] { new Field("name", "Network name", "Sample network"), new Field("secret", "Password", null, "password"), new Field("mode", "Connection mode", "Automatic", "choice", "Automatic", "Manual", "Bridge"), new Field("enabled", "Enable sample option", true, "bool"), new Field("note", "Notes", "Sample text", "multiline") }, async v => { await Task.FromResult(0); }, "Close sample", "Preview controls only; no settings are saved."); }), LocalButton("Open sample confirmation", delegate { Confirm("This sample confirmation performs no network change."); }));
            ActionRow(LocalButton("Open sample backup priority", delegate { BackupPriority(new List<object> { J.Obj("ssid", "Sample phone hotspot", "uuid", "sample-1"), J.Obj("ssid", "Sample backup wifi", "uuid", "sample-2") }); }), LocalButton("Open sample credential display", delegate { ShowCredentials("Sample credentials", new Dictionary<string, string> { { "Sample username", "sample-user" }, { "Sample password", "sample-password" } }); }));
        }
        private sealed class ThemeSampleRow { public string Name { get; set; } public string Status { get; set; } }
    }
    public static class ThemeVerification
    {
        private static IEnumerable<DependencyObject> Children(DependencyObject node)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var child = VisualTreeHelper.GetChild(node, i); yield return child; foreach (var item in Children(child)) yield return item; }
        }
        public static bool HasWhiteControlSurface(DependencyObject root)
        {
            return Children(root).OfType<Control>().Where(c => c is TextBox || c is PasswordBox || c is ComboBox || c is ComboBoxItem || c is DataGrid || c is DataGridRow || c is DataGridCell || c is ScrollBar).Select(c => c.Background as SolidColorBrush).Any(b => b != null && b.Color.A > 0 && b.Color.R > 235 && b.Color.G > 235 && b.Color.B > 235);
        }
        public static bool HasWhiteCardSurface(DependencyObject root)
        {
            return Children(root).OfType<Border>().Select(c => c.Background as SolidColorBrush).Any(b => b != null && b.Color.A > 0 && b.Color.R > 235 && b.Color.G > 235 && b.Color.B > 235);
        }
        public static double Contrast(Brush foreground, Brush background)
        {
            var a = foreground as SolidColorBrush; var b = background as SolidColorBrush; if (a == null || b == null) return 0;
            Func<Color, double> luminance = c => new[] { c.R, c.G, c.B }.Select(v => v / 255.0).Select(v => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4)).Select((v, i) => v * new[] { 0.2126, 0.7152, 0.0722 }[i]).Sum();
            double x = luminance(a.Color), y = luminance(b.Color); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
        }
    }
}
