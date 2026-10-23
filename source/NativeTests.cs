using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EeroDesktop
{
    internal static partial class Tests
    {
        private static void NativeChecks()
        {
            var cases = new[] {
                new[] { "eero Pro 6E", "S010001", "eero-pro-6e" }, new[] { "eero Max 7", "V010001", "eero-max-7" },
                new[] { "eero", "A010001", "eero-gen1" }, new[] { "eero", "J010011", "eero-gen2" },
                new[] { "eero Pro", "B010011", "eero-pro" }, new[] { "eero Beacon", "D010001", "eero-beacon" },
                new[] { "eero Pro 6", "K010011", "eero-pro-6" }, new[] { "eero 6", "N010001", "eero-6" },
                new[] { "eero 6 Extender", "Q010011", "eero-6-extender" }, new[] { "eero 6+", "R010001", "eero-6-plus" },
                new[] { "eero PoE 6", "T010001", "eero-poe-6" }, new[] { "eero PoE Gateway", "U010001", "eero-poe-gateway" },
                new[] { "eero 7", "PA10001", "eero-7" }, new[] { "eero Pro 7", "ME10001", "eero-pro-7" },
                new[] { "eero Outdoor 7", "SN10001", "eero-outdoor-7" }, new[] { "eero PoE 7", "NV01", "eero-poe-7" },
                new[] { "eero Signal", "RG4U", "eero-signal-4g" }, new[] { "eero Signal", "RG5W", "eero-signal-5g" },
                new[] { "eero 6+ Extender", "", "eero-6-plus-extender" }, new[] { "eero Telephone Adapter", "FH01", "eero-telephone-adapter" }
            };
            foreach (var item in cases)
            {
                Assert(ProductImages.Resolve(item[0], item[1]) == item[2], "hardware photograph matches " + item[0] + " " + item[1]);
                var bitmap = ProductImages.Load(item[2]); Assert(bitmap != null && bitmap.PixelWidth >= 100 && bitmap.IsFrozen, "bundled image loads without network: " + item[2]);
            }
            Assert(ProductImages.Resolve("eero Pro 6E", "V010001") == "eero-max-7", "known hardware code takes priority over conflicting label");
            Assert(ProductImages.Resolve("eero_pro_6e", "") == "eero-pro-6e", "API model-name formatting is normalized");
            Assert(ProductImages.Resolve("Unrecognized model", "UNKNOWN") == null && ProductImages.Resolve("eero", "") == null, "unknown or ambiguous models do not display a different product");
            Assert(ProductImages.Resolve("eero Pro 6E", "") != ProductImages.Resolve("eero Pro 6", ""), "Pro 6 and Pro 6E remain distinct");
            Assert(ProductImages.Ids.Length == 20 && ProductImages.Ids.All(id => ProductImages.Load(id) != null), "complete 20-variant image catalog is embedded");

            foreach (string theme in new[] { "Light", "Dark" })
            {
                UiPreferences.Theme = theme; Program.ApplyTheme(); var window = new MainWindow(true);
                foreach (string route in new[] { "Home", "Devices", "Activity", "Settings", "Home/Profiles", "Home/Profiles/Profile", "Home/eeros/Details", "Settings/Appearance", "Product gallery" })
                {
                    window.PreviewPage(route); var root = (FrameworkElement)window.Content;
                    root.Measure(new Size(1040, 720)); root.Arrange(new Rect(0, 0, 1040, 720)); root.UpdateLayout();
                    Assert(root.ActualWidth == 1040 && root.ActualHeight == 720, "minimum Windows layout renders " + theme + " " + route);
                }
                window.Close();
            }
            UiPreferences.Theme = "Light"; Program.ApplyTheme(); var liveWindow = new MainWindow(true); liveWindow.PreviewPage("Home");
            UiPreferences.Theme = "Dark"; Program.ApplyTheme(); var liveRoot = (FrameworkElement)liveWindow.Content;
            liveRoot.Measure(new Size(1360, 880)); liveRoot.Arrange(new Rect(0, 0, 1360, 880)); liveRoot.UpdateLayout();
            Assert(!ThemeVerification.HasWhiteCardSurface(liveRoot), "existing cards recolor when Windows theme changes without rebuilding the page");
            liveWindow.Close();
        }
    }
}
