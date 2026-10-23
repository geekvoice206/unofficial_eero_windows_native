using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private static double Number(object value) { double n; return Double.TryParse(J.S(value), NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? Math.Max(0, n) : 0; }
        private static string Amount(double value, bool bytes)
        {
            if (!bytes) return value.ToString("N0");
            if (value >= 1000000000) return (value / 1000000000).ToString("0.##") + " GB";
            if (value >= 1000000) return (value / 1000000).ToString("0.##") + " MB";
            if (value >= 1000) return (value / 1000).ToString("0.##") + " kB";
            return value.ToString("0") + " B";
        }
        private void AddSeriesCharts(Panel target, object data, bool bytes)
        {
            var series = J.List(J.Get(data, "series"));
            if (series.Count == 0) { var p = new StackPanel(); Details(p, data, 0); target.Children.Add(Card(p)); return; }
            foreach (object item in series.Take(12))
            {
                var values = J.List(J.Get(item, "values")).Take(60).ToList();
                var panel = new StackPanel(); panel.Children.Add(Text(Human(J.First(item, "insight_type_name", "type", "insight_type")), 20, "#17263C", true));
                double total = J.Get(item, "sum") == null ? values.Sum(v => Number(J.Get(v, "value"))) : Number(J.Get(item, "sum"));
                panel.Children.Add(Text(Amount(total, bytes), 27, "#274CDB", true));
                double maximum = values.Count == 0 ? 1 : Math.Max(1, values.Max(v => Number(J.Get(v, "value"))));
                var chart = new UniformGrid { Columns = Math.Max(1, values.Count), Height = 180 };
                foreach (object value in values)
                {
                    double n = Number(J.Get(value, "value")); string date = J.S(value, "time"); DateTime parsed; if (DateTime.TryParse(date, out parsed)) date = parsed.ToString(values.Count > 14 ? "dd" : "MMM d");
                    var column = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(3, 0, 3, 0), ToolTip = J.S(value, "time") + " · " + Amount(n, bytes) };
                    column.Children.Add(new Rectangle { Fill = Brush("#345BEB"), Height = 140 * n / maximum, MinWidth = 3, MaxWidth = 72, HorizontalAlignment = HorizontalAlignment.Stretch, RadiusX = 3, RadiusY = 3 });
                    column.Children.Add(new TextBlock { Text = date, FontSize = 10, Foreground = Brush("#64738A"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }); chart.Children.Add(column);
                }
                panel.Children.Add(chart); target.Children.Add(Card(panel));
            }
        }
    }
}
