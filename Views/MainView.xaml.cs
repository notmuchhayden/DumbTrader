using System.Windows.Controls;
using DumbTrader.Services;

namespace DumbTrader.Views
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();
            var sp = DumbTrader.App.ServiceProvider as global::System.IServiceProvider;
            if (sp is not null)
            {
                DataContext = sp.GetService(typeof(DumbTrader.ViewModels.MainViewModel)) as DumbTrader.ViewModels.MainViewModel;
            }
            if (DataContext == null)
            {
                throw new System.InvalidOperationException("MainViewModel not available from ServiceProvider");
            }
        }
    }
}
