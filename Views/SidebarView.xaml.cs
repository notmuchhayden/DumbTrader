using System.Windows.Controls;

namespace DumbTrader.Views
{
    public partial class SidebarView : UserControl
    {
        public SidebarView()
        {
            InitializeComponent();
            // Resolve via DI
            var sp = DumbTrader.App.ServiceProvider as global::System.IServiceProvider;
            if (sp is not null)
            {
                // use non-generic GetService(Type)
                DataContext = sp.GetService(typeof(DumbTrader.ViewModels.SidebarViewModel)) as DumbTrader.ViewModels.SidebarViewModel;
            }
            if (DataContext == null)
            {
                DataContext = new DumbTrader.ViewModels.SidebarViewModel();
            }
        }
    }
}
