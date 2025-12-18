using System.Windows.Controls;

namespace DumbTrader.Views
{
    public partial class SummaryView : UserControl
    {
        public SummaryView()
        {
            InitializeComponent();
            var sp = DumbTrader.App.ServiceProvider as global::System.IServiceProvider;
            if (sp is not null)
            {
                DataContext = sp.GetService(typeof(DumbTrader.ViewModels.SummaryViewModel)) as DumbTrader.ViewModels.SummaryViewModel;
            }
            if (DataContext == null)
            {
                DataContext = new DumbTrader.ViewModels.SummaryViewModel();
            }
        }
    }
}
