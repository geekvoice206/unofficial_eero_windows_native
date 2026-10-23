using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EeroDesktop
{
    public static class ProductImages
    {
        // Prefixes cover regional/channel variants documented by eero. Match the
        // hardware code first; a display label must never override a known model.
        private static readonly Dictionary<string, string> Codes = new Dictionary<string, string> {
            { "A01", "eero-gen1" }, { "B01", "eero-pro" }, { "D01", "eero-beacon" }, { "J01", "eero-gen2" },
            { "K01", "eero-pro-6" }, { "N01", "eero-6" }, { "Q01", "eero-6-extender" }, { "R01", "eero-6-plus" },
            { "S01", "eero-pro-6e" }, { "T01", "eero-poe-6" }, { "U01", "eero-poe-gateway" }, { "V01", "eero-max-7" },
            { "PA1", "eero-7" }, { "ME1", "eero-pro-7" }, { "SN1", "eero-outdoor-7" }, { "NV01", "eero-poe-7" },
            { "RG4", "eero-signal-4g" }, { "RG5", "eero-signal-5g" }, { "FH01", "eero-telephone-adapter" }
        };
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string> {
            { "eeromax7", "eero-max-7" }, { "eeropro7", "eero-pro-7" }, { "eero7", "eero-7" }, { "eerooutdoor7", "eero-outdoor-7" },
            { "eeropoe7", "eero-poe-7" }, { "eeropoe6", "eero-poe-6" }, { "eeropoegateway", "eero-poe-gateway" },
            { "eeropro6e", "eero-pro-6e" }, { "eeropro6", "eero-pro-6" }, { "eero6+", "eero-6-plus" }, { "eero6plus", "eero-6-plus" },
            { "eero6+extender", "eero-6-plus-extender" }, { "eero6plusextender", "eero-6-plus-extender" },
            { "eero6extender", "eero-6-extender" }, { "eero6", "eero-6" }, { "eerobeacon", "eero-beacon" },
            { "eeropro", "eero-pro" }, { "eeropro2ndgen", "eero-pro" }, { "eero1stgen", "eero-gen1" }, { "eero1stgeneration", "eero-gen1" },
            { "eero2ndgen", "eero-gen2" }, { "eero2ndgeneration", "eero-gen2" }, { "eerosignal4glte", "eero-signal-4g" },
            { "eerosignal4g", "eero-signal-4g" }, { "eerosignal5g", "eero-signal-5g" },
            { "eerotelephoneadapter", "eero-telephone-adapter" }, { "eerofh01", "eero-telephone-adapter" }
        };
        private static readonly Dictionary<string, BitmapSource> Cache = new Dictionary<string, BitmapSource>();
        public static string Resolve(string model, string code)
        {
            string normalized = Regex.Replace(code ?? "", "[^a-zA-Z0-9]", "").ToUpperInvariant();
            foreach (var pair in Codes) if (normalized.StartsWith(pair.Key, StringComparison.Ordinal) && normalized.Length >= pair.Key.Length + (pair.Key.Length == 4 ? 0 : 1)) return pair.Value;
            normalized = Regex.Replace(model ?? "", "[\\s_()\\-]", "").ToLowerInvariant();
            string result; if (Names.TryGetValue(normalized, out result)) return result;
            // Some API versions put the hardware number in the model field.
            string modelCode = Regex.Replace(model ?? "", "[^a-zA-Z0-9]", "").ToUpperInvariant();
            foreach (var pair in Codes) if (modelCode.StartsWith(pair.Key, StringComparison.Ordinal) && modelCode.Length >= pair.Key.Length + (pair.Key.Length == 4 ? 0 : 1) && Regex.IsMatch(modelCode, "^[A-Z]{1,2}[0-9][A-Z0-9]{1,6}$")) return pair.Value;
            return null;
        }
        public static string Resolve(object node) { return Resolve(J.First(node, "model", "model_name", "product_name"), J.First(node, "model_number", "model_code", "hardware_model")); }
        public static BitmapSource Load(string id)
        {
            if (String.IsNullOrEmpty(id)) return null; BitmapSource image;
            if (Cache.TryGetValue(id, out image)) return image;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("EeroDesktop.Products." + id + ".png"))
            {
                if (stream == null) return null;
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); Cache[id] = bitmap; return bitmap;
            }
        }
        public static string[] Ids { get { return Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n => n.StartsWith("EeroDesktop.Products.") && n.EndsWith(".png")).Select(n => n.Substring(21, n.Length - 25)).OrderBy(n => n).ToArray(); } }
    }

    public sealed partial class MainWindow
    {
        private static FrameworkElement HardwareImage(object node, double size = 54)
        {
            var source = ProductImages.Load(ProductImages.Resolve(node));
            if (source == null) return Glyph("\uE968", size * 0.5);
            var image = new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, ToolTip = J.First(node, "model", "model_name", "product_name") };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            System.Windows.Automation.AutomationProperties.SetName(image, "Product image: " + J.First(node, "model", "model_name", "product_name")); return image;
        }
        private static string ProfileSummary(object profile)
        {
            string count = J.Get(profile, "devices") != null ? J.List(J.Get(profile, "devices")).Count + " devices · " : J.Get(profile, "device_count") != null ? J.S(profile, "device_count") + " devices · " : "";
            return count + (J.B(profile, "paused") ? "Paused" : "Internet enabled");
        }
        private Border CompactNode(object node)
        {
            var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) }); content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(HardwareImage(node, 52));
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            text.Children.Add(Text(J.First(node, "location", "model"), 14, "#17263C", true)); var subtitle = Text((J.B(node, "gateway") ? "Gateway · " : "") + J.S(node, "model"), 12, "#64738A"); subtitle.Margin = new Thickness(0, 0, 0, 4); text.Children.Add(subtitle);
            bool online = IsOnline(node); string count = J.S(node, "connected_clients_count");
            var state = Text((online ? "● Online" : "○ " + (J.S(node, "status").NullIfEmpty() ?? "Unknown")) + (count.Length > 0 ? " · " + count + " devices" : ""), 11, online ? "#0D7959" : "#64738A"); state.Margin = new Thickness(0); text.Children.Add(state); Grid.SetColumn(text, 1); content.Children.Add(text);
            var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Orientation = Orientation.Horizontal };
            var open = Button("View details", async delegate { selectedNode = preview ? node : await Read(J.S(node, "url")); currentPage = "Home/eeros/Details"; RenderPage(); }); open.Style = (Style)FindResource("QuietButton"); open.Margin = new Thickness(0); open.Padding = new Thickness(6, 6, 6, 6); actions.Children.Add(open);
            var rename = Button("Rename", async delegate { string name = Prompt("eero location", J.S(node, "location")); if (name == null) return; ValidateName(name); await Change("PUT", J.S(node, "url"), J.Obj("location", name.Trim()), true, "eero location updated."); }); rename.Style = (Style)FindResource("QuietButton"); rename.Margin = new Thickness(0); rename.Padding = new Thickness(6); actions.Children.Add(rename);
            Grid.SetColumn(actions, 2); content.Children.Add(actions);
            return new Border { Child = content, Background = Surface, BorderBrush = Brush("#E5EAF3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Margin = new Thickness(-6, 0, -6, 8) };
        }
        private void ProductGalleryPage()
        {
            Sub("Bundled manufacturer imagery · matched by hardware number or model name.");
            var grid = new UniformGrid { Columns = 4 };
            foreach (string id in ProductImages.Ids)
            {
                var sample = new StackPanel(); sample.Children.Add(new Image { Source = ProductImages.Load(id), Width = 110, Height = 90, Stretch = Stretch.Uniform }); sample.Children.Add(Text(id, 12)); var card = Card(sample, 16); card.Margin = new Thickness(6); grid.Children.Add(card);
            }
            page.Children.Add(grid);
        }
    }
}
