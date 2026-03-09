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
                var loggingService = sp?.GetService(typeof(Services.LoggingService)) as Services.LoggingService;
                if (loggingService == null)
                {
                    // Handle the case where loggingService is null, e.g., throw or create a default instance
                    throw new System.InvalidOperationException("LoggingService is not available from the service provider.");
                }
                accountService = new Services.AccountService(dbContext, loggingService);
            }
            DataContext = new ViewModels.SummaryViewModel(accountService);
        }
    }
}
