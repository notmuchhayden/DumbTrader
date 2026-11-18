using DumbTrader.Models;
using ScottPlot;

namespace DumbTrader.ViewModels;

internal static class OFuturePriceGuidePlot
{
    public static double[] Draw(Plot plot, OFuturePriceGuide? guide)
    {
        plot.HideLegend();
        if (guide is null) return [];

        var levels = new List<double>(2);
        if (guide.Position == 0)
        {
            Add(guide.Levels.LongEntry, "Long entry >=", Colors.DarkGreen);
            Add(guide.Levels.ShortEntry, "Short entry <=", Colors.DarkMagenta);
        }
        else
        {
            Add(guide.Levels.ExitBoundary, guide.Position > 0 ? "Long exit <" : "Short exit >", Colors.DarkOrange);
        }
        if (levels.Count > 0)
        {
            plot.Legend.FontName = ScottPlot.Fonts.Sans;
            plot.Legend.BackgroundColor = Colors.White.WithOpacity(0.55);
            plot.Legend.ShadowColor = Colors.Transparent;
            plot.ShowLegend();
        }
        return levels.ToArray();

        void Add(double price, string label, Color color)
        {
            if (!double.IsFinite(price) || price <= 0) return;
            var line = plot.Add.HorizontalLine(price, color: color);
            line.LinePattern = LinePattern.Dashed;
            line.LegendText = $"{label} {price:G10}";
            levels.Add(price);
        }
    }

}
