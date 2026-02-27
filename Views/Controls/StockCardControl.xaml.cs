using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DumbTrader.Views
{
    public partial class StockCardControl : UserControl
    {
        public StockCardControl()
        {
            InitializeComponent();
        }

        public string Symbol
        {
            get => (string)GetValue(SymbolProperty);
            set => SetValue(SymbolProperty, value);
        }
        public static readonly DependencyProperty SymbolProperty =
            DependencyProperty.Register(nameof(Symbol), typeof(string), typeof(StockCardControl), new PropertyMetadata(string.Empty));

        public string Price
        {
            get => (string)GetValue(PriceProperty);
            set => SetValue(PriceProperty, value);
        }
        public static readonly DependencyProperty PriceProperty =
            DependencyProperty.Register(nameof(Price), typeof(string), typeof(StockCardControl), new PropertyMetadata(string.Empty));

        public string Change
        {
            get => (string)GetValue(ChangeProperty);
            set => SetValue(ChangeProperty, value);
        }
        public static readonly DependencyProperty ChangeProperty =
            DependencyProperty.Register(nameof(Change), typeof(string), typeof(StockCardControl), new PropertyMetadata(string.Empty));

        public string ChangeText
        {
            get => (string)GetValue(ChangeTextProperty);
            set => SetValue(ChangeTextProperty, value);
        }
        public static readonly DependencyProperty ChangeTextProperty =
            DependencyProperty.Register(nameof(ChangeText), typeof(string), typeof(StockCardControl), new PropertyMetadata(string.Empty, OnChangeTextChanged));

        public string Rate
        {
            get => (string)GetValue(RateProperty);
            set => SetValue(RateProperty, value);
        }
        public static readonly DependencyProperty RateProperty =
            DependencyProperty.Register(nameof(Rate), typeof(string), typeof(StockCardControl), new PropertyMetadata(string.Empty));

        public Style? ChangeStyle
        {
            get => (Style?)GetValue(ChangeStyleProperty);
            set => SetValue(ChangeStyleProperty, value);
        }
        public static readonly DependencyProperty ChangeStyleProperty =
            DependencyProperty.Register(nameof(ChangeStyle), typeof(Style), typeof(StockCardControl), new PropertyMetadata(null));

        public Style? SparklineStyle
        {
            get => (Style?)GetValue(SparklineStyleProperty);
            set => SetValue(SparklineStyleProperty, value);
        }
        public static readonly DependencyProperty SparklineStyleProperty =
            DependencyProperty.Register(nameof(SparklineStyle), typeof(Style), typeof(StockCardControl), new PropertyMetadata(null));

        public string Bid
        {
            get => (string)GetValue(BidProperty);
            set => SetValue(BidProperty, value);
        }
        public static readonly DependencyProperty BidProperty =
            DependencyProperty.Register(nameof(Bid), typeof(string), typeof(StockCardControl), new PropertyMetadata(string.Empty));

        public string Ask
        {
            get => (string)GetValue(AskProperty);
            set => SetValue(AskProperty, value);
        }
        public static readonly DependencyProperty AskProperty =
            DependencyProperty.Register(nameof(Ask), typeof(string), typeof(StockCardControl), new PropertyMetadata(string.Empty));

        public string Open
        {
            get => (string)GetValue(OpenProperty);
            set => SetValue(OpenProperty, value);
        }
        public static readonly DependencyProperty OpenProperty =
            DependencyProperty.Register(nameof(Open), typeof(string), typeof(StockCardControl), new PropertyMetadata(string.Empty));

        public string High
        {
            get => (string)GetValue(HighProperty);
            set => SetValue(HighProperty, value);
        }
        public static readonly DependencyProperty HighProperty =
            DependencyProperty.Register(nameof(High), typeof(string), typeof(StockCardControl), new PropertyMetadata(string.Empty));

        public string Low
        {
            get => (string)GetValue(LowProperty);
            set => SetValue(LowProperty, value);
        }
        public static readonly DependencyProperty LowProperty =
            DependencyProperty.Register(nameof(Low), typeof(string), typeof(StockCardControl), new PropertyMetadata(string.Empty));

        public Geometry? SparklineData
        {
            get => (Geometry?)GetValue(SparklineDataProperty);
            set => SetValue(SparklineDataProperty, value);
        }
        public static readonly DependencyProperty SparklineDataProperty =
            DependencyProperty.Register(nameof(SparklineData), typeof(Geometry), typeof(StockCardControl), new PropertyMetadata(null));

        /// <summary>
        /// Input values for a lightweight sparkline.
        /// When set, SparklineData is generated automatically with Min/Max auto scaling.
        /// </summary>
        public IEnumerable<double>? SparklineValues
        {
            get => (IEnumerable<double>?)GetValue(SparklineValuesProperty);
            set => SetValue(SparklineValuesProperty, value);
        }
        public static readonly DependencyProperty SparklineValuesProperty =
            DependencyProperty.Register(nameof(SparklineValues), typeof(IEnumerable<double>), typeof(StockCardControl), new PropertyMetadata(null, OnSparklineValuesChanged));

        /// <summary>
        /// Maximum number of points to draw. If input has more values, they are downsampled.
        /// </summary>
        public int MaxSparklinePoints
        {
            get => (int)GetValue(MaxSparklinePointsProperty);
            set => SetValue(MaxSparklinePointsProperty, value);
        }
        public static readonly DependencyProperty MaxSparklinePointsProperty =
            DependencyProperty.Register(nameof(MaxSparklinePoints), typeof(int), typeof(StockCardControl), new PropertyMetadata(100, OnSparklineValuesChanged));

        private static void OnSparklineValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StockCardControl control)
            {
                control.SparklineData = control.BuildSparklineGeometry(control.SparklineValues, 100, 40, control.MaxSparklinePoints);
            }
        }

        private static void OnChangeTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StockCardControl control)
            {
                var text = e.NewValue as string ?? string.Empty;
                if (string.IsNullOrWhiteSpace(control.Change))
                {
                    control.Change = text;
                }
                if (string.IsNullOrWhiteSpace(control.Rate))
                {
                    control.Rate = text;
                }
            }
        }

        private Geometry BuildSparklineGeometry(IEnumerable<double>? source, double width, double height, int maxPoints)
        {
            if (source is null)
            {
                return Geometry.Empty;
            }

            var values = source.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
            if (values.Count == 0)
            {
                return Geometry.Empty;
            }

            if (maxPoints > 1 && values.Count > maxPoints)
            {
                values = Downsample(values, maxPoints);
            }

            if (values.Count == 1)
            {
                var y = height / 2d;
                return Geometry.Parse($"M0,{y.ToString(CultureInfo.InvariantCulture)} L{width.ToString(CultureInfo.InvariantCulture)},{y.ToString(CultureInfo.InvariantCulture)}");
            }

            double min = values.Min();
            double max = values.Max();
            double range = max - min;
            bool flat = range <= double.Epsilon;

            var figure = new PathFigure();
            var segments = new PathSegmentCollection();
            double xStep = width / (values.Count - 1d);

            for (int i = 0; i < values.Count; i++)
            {
                double x = i * xStep;
                double y;

                if (flat)
                {
                    y = height / 2d;
                }
                else
                {
                    double normalized = (values[i] - min) / range; // 0~1
                    y = height - (normalized * height); // invert for UI coords
                }

                var point = new Point(x, y);
                if (i == 0)
                {
                    figure.StartPoint = point;
                }
                else
                {
                    segments.Add(new LineSegment(point, true));
                }
            }

            figure.Segments = segments;
            figure.IsClosed = false;
            figure.IsFilled = false;

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            return geometry;
        }

        private static List<double> Downsample(IReadOnlyList<double> values, int targetCount)
        {
            if (values.Count <= targetCount)
            {
                return values.ToList();
            }

            var sampled = new List<double>(targetCount);
            double step = (values.Count - 1d) / (targetCount - 1d);

            for (int i = 0; i < targetCount; i++)
            {
                int index = (int)Math.Round(i * step);
                index = Math.Clamp(index, 0, values.Count - 1);
                sampled.Add(values[index]);
            }

            return sampled;
        }
    }
}
