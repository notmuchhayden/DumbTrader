using System.Windows.Controls;

namespace DumbTrader.Views
{
    public partial class SummaryView : UserControl
    {
        public SummaryView()
        {
            InitializeComponent();
            var sp = App.ServiceProvider;
            Services.AccountService? accountService = null;
            if (sp is not null)
            {
                accountService = sp.GetService(typeof(Services.AccountService)) as Services.AccountService;
            }
            if (accountService == null)
            {
                var dbContext = new Services.DumbTraderDbContext();
                accountService = new Services.AccountService(dbContext);
            }
            DataContext = new ViewModels.SummaryViewModel(accountService);
        }
    }
}
