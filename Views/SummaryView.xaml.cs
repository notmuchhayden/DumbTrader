using System.Windows.Controls;

namespace DumbTrader.Views
{
    public partial class SummaryView : UserControl
    {
        public SummaryView()
        {
            InitializeComponent();
            var sp = App.ServiceProvider;
            if (sp != null)
            {
                DataContext = sp.GetService(typeof(ViewModels.SummaryViewModel)) as ViewModels.SummaryViewModel;
            }
        }

        public SummaryView(ViewModels.SummaryViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
