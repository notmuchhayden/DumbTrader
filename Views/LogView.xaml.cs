using System.Windows.Controls;

namespace DumbTrader.Views
{
    public partial class LogView : UserControl
    {
        public LogView()
        {
            InitializeComponent();
            var sp = DumbTrader.App.ServiceProvider as global::System.IServiceProvider;
            if (sp is not null)
            {
                DataContext = sp.GetService(typeof(DumbTrader.ViewModels.LogViewModel)) as DumbTrader.ViewModels.LogViewModel;
            }
            if (DataContext == null)
            {
                DataContext = new DumbTrader.ViewModels.LogViewModel();
            }
        }
    }
}
