using System.Windows.Controls;

namespace DumbTrader.Views
{
    public partial class SummaryView : UserControl
    {
        public SummaryView()
        {
            InitializeComponent();
            var sp = DumbTrader.App.ServiceProvider as global::System.IServiceProvider;
            DumbTrader.Services.AccountService accountService = null;
            if (sp is not null)
            {
                accountService = sp.GetService(typeof(DumbTrader.Services.AccountService)) as DumbTrader.Services.AccountService;
            }
            if (accountService == null)
            {
                var dbContext = new DumbTrader.Services.DumbTraderDbContext();
                accountService = new DumbTrader.Services.AccountService(dbContext);
            }
            DataContext = new DumbTrader.ViewModels.SummaryViewModel(accountService);
        }
    }
}
