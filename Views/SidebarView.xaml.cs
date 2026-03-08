using System.Windows.Controls;

namespace DumbTrader.Views
{
    public partial class SidebarView : UserControl
    {
        public SidebarView()
        {
            InitializeComponent();
            // Resolve via DI
            var sp = App.ServiceProvider;
            if (sp is not null)
            {
                // use non-generic GetService(Type)
                DataContext = sp.GetService(typeof(ViewModels.SidebarViewModel)) as ViewModels.SidebarViewModel;
            }
            if (DataContext == null)
            {
                DataContext = new ViewModels.SidebarViewModel();
            }
        }
    }
}
