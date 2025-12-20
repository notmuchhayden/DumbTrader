using System.ComponentModel;
using System.Windows.Controls;
using DumbTrader.Services;

namespace DumbTrader.Views
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();

            // Don't attempt runtime DI resolution while in the XAML designer
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                // Optionally provide design-time DataContext here
                return;
            }

            var sp = DumbTrader.App.ServiceProvider as global::System.IServiceProvider;
            if (sp is not null)
            {
                DataContext = sp.GetService(typeof(DumbTrader.ViewModels.MainViewModel)) as DumbTrader.ViewModels.MainViewModel;
            }

            // Do not throw here. If resolution failed, App.OnStartup will set the correct DataContext when creating the MainWindow.
        }
    }
}
