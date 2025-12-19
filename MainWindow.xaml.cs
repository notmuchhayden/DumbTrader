using System.Windows;
using DumbTrader.Views;

namespace DumbTrader;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow
        {
            Owner = this
        };
        settings.ShowDialog();
    }

    private void OpenAbout_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow
        {
            Owner = this
        };
        about.ShowDialog();
    }
}
