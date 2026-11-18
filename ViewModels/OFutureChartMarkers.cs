using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ScottPlot;
using ScottPlot.WPF;
using DumbTrader.Models;

namespace DumbTrader.ViewModels;

internal sealed class OFutureChartMarkers
{
    internal static DateTime BucketStart(DateTime timestamp, int intervalMinutes) =>
        intervalMinutes == 0 ? timestamp.Date :
        timestamp.Date.AddMinutes((int)timestamp.TimeOfDay.TotalMinutes / intervalMinutes * intervalMinutes);

    private readonly WpfPlot _control;
    private (Coordinates Position, string[] Details)[] _points = [];
    private Point _press;

    public OFutureChartMarkers(WpfPlot control)
    {
        _control = control;
        control.MouseMove += (_, e) =>
        {
            var entries = HitTest(e.GetPosition(control));
            var detail = entries is null ? null : entries.Length == 1 ? entries[0]
                : $"{entries.Length}개 이벤트 · 클릭하여 상세 보기";
            if (!Equals(control.ToolTip, detail)) control.ToolTip = detail;
        };
        control.MouseLeave += (_, _) => control.ToolTip = null;
        control.PreviewMouseLeftButtonDown += (_, e) => _press = e.GetPosition(control);
        control.PreviewMouseLeftButtonUp += (_, e) =>
        {
            var mouse = e.GetPosition(control);
            if ((mouse - _press).Length > 4 || HitTest(mouse) is not { } entries) return;
            var menu = new ContextMenu { PlacementTarget = control, Placement = PlacementMode.MousePoint };
            menu.Items.Add(new MenuItem
            {
                StaysOpenOnClick = true,
                Header = new ScrollViewer
                {
                    MaxHeight = 350, MaxWidth = 680,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock { Text = string.Join(Environment.NewLine, entries), TextWrapping = TextWrapping.Wrap }
                }
            });
            menu.IsOpen = true;
        };
    }

    private string[]? HitTest(Point mouse)
    {
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(_control);
        var best = 100d;
        string[]? entries = null;
        foreach (var point in _points)
        {
            var pixel = _control.Plot.GetPixel(point.Position);
            var dx = pixel.X - mouse.X * dpi.DpiScaleX;
            var dy = pixel.Y - mouse.Y * dpi.DpiScaleY;
            var distance = dx * dx + dy * dy;
            if (distance > best) continue;
            best = distance;
            entries = point.Details;
        }
        return entries;
    }

    public (double Min, double Max) IncludeVisiblePrices(double left, double right, double min, double max)
    {
        foreach (var point in _points)
        {
            if (point.Position.X < left || point.Position.X > right) continue;
            min = Math.Min(min, point.Position.Y);
            max = Math.Max(max, point.Position.Y);
        }
        return (min, max);
    }

    public void Draw(IEnumerable<OFutureChartEvent> events, Func<DateTime, double?> resolveX, bool daily)
    {
        _control.ToolTip = null;
        var points = new List<(Coordinates, string[])>();
        var groups = events.Select(e => (Event: e, X: resolveX(e.ProjectionTimestamp ?? e.Timestamp)))
            .Where(item => item.X.HasValue && double.IsFinite(item.Event.Price))
            .GroupBy(item => (X: item.X!.Value, item.Event.Price));
        foreach (var group in groups)
        {
            var items = group.Select(item => item.Event).OrderBy(e => e.Timestamp).ToArray();
            var position = new Coordinates(group.Key.X, group.Key.Price);
            var glyph = string.Concat(items.Select(Glyph).Distinct());
            var marker = _control.Plot.Add.Text(items.Length == 1 ? glyph : $"{glyph} {items.Length}",
                position.X, position.Y);
            marker.Alignment = Alignment.MiddleCenter;
            marker.LabelFontSize = 16;
            marker.LabelFontColor = items[0].Action == "BUY" ? Colors.DarkGreen : Colors.DarkMagenta;
            points.Add((position, items.Select(e =>
                $"{e.Timestamp.ToString(daily || e.IntervalMinutes == 0 ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss")} · {e.Symbol} · {e.Strategy} · {e.Source} · {e.Action} {e.Quantity}계약 · {e.Price:G} · {KindLabel(e.Kind)} {e.Detail}").ToArray()));
        }
        _points = points.ToArray();
    }

    private static string Glyph(OFutureChartEvent item) => item.Kind switch
    {
        OFutureChartEventKind.Entry => "●",
        OFutureChartEventKind.Exit => "◆",
        _ => item.Action == "BUY" ? "△" : "▽"
    };

    private static string KindLabel(OFutureChartEventKind kind) => kind switch
    {
        OFutureChartEventKind.Entry => "진입 체결",
        OFutureChartEventKind.Exit => "청산 체결",
        _ => "신호"
    };
}
