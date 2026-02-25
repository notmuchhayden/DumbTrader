using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DumbTrader.Views
{
    public partial class StockCardControl : UserControl
    {
        public static readonly DependencyProperty SymbolProperty =
            DependencyProperty.Register(nameof(Symbol), typeof(string), typeof(StockCardControl));

        public static readonly DependencyProperty ChangeTextProperty =
            DependencyProperty.Register(nameof(ChangeText), typeof(string), typeof(StockCardControl));

        public static readonly DependencyProperty ChangeStyleProperty =
            DependencyProperty.Register(nameof(ChangeStyle), typeof(Style), typeof(StockCardControl));

        public static readonly DependencyProperty SparklineDataProperty =
            DependencyProperty.Register(nameof(SparklineData), typeof(Geometry), typeof(StockCardControl));

        public static readonly DependencyProperty SparklineStyleProperty =
            DependencyProperty.Register(nameof(SparklineStyle), typeof(Style), typeof(StockCardControl));

        public string? Symbol
        {
            get => (string?)GetValue(SymbolProperty);
            set => SetValue(SymbolProperty, value);
        }

        public string? ChangeText
        {
            get => (string?)GetValue(ChangeTextProperty);
            set => SetValue(ChangeTextProperty, value);
        }

        public Style? ChangeStyle
        {
            get => (Style?)GetValue(ChangeStyleProperty);
            set => SetValue(ChangeStyleProperty, value);
        }

        public Geometry? SparklineData
        {
            get => (Geometry?)GetValue(SparklineDataProperty);
            set => SetValue(SparklineDataProperty, value);
        }

        public Style? SparklineStyle
        {
            get => (Style?)GetValue(SparklineStyleProperty);
            set => SetValue(SparklineStyleProperty, value);
        }

        public StockCardControl()
        {
            InitializeComponent();
        }
    }
}
