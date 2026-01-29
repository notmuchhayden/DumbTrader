using System.Windows.Controls;

namespace DumbTrader.Views
{
    public partial class LogView : UserControl
    {
        public LogView()
        {
            InitializeComponent();
            var sp = DumbTrader.App.ServiceProvider as global::System.IServiceProvider;
            DataContext = sp?.GetService(typeof(DumbTrader.ViewModels.LogViewModel)) as DumbTrader.ViewModels.LogViewModel;
        }
    }
}
