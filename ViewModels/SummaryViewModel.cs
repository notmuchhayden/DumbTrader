using DumbTrader.Core;
using System.ComponentModel;

namespace DumbTrader.ViewModels
{
    public class SummaryViewModel : ViewModelBase
    {
        private string _accountId = "123-456-789";
        public string AccountId
        {
            get => _accountId;
            set => SetProperty(ref _accountId, value);
        }

        private string _totalBalanceDisplay = "$10,250.00 +$325.50";
        public string TotalBalanceDisplay
        {
            get => _totalBalanceDisplay;
            set => SetProperty(ref _totalBalanceDisplay, value);
        }
    }
}
