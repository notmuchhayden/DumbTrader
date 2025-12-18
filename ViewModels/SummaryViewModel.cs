using System.ComponentModel;

namespace DumbTrader.ViewModels
{
    public class SummaryViewModel : INotifyPropertyChanged
    {
        private string _accountId = "123-456-789";
        public string AccountId
        {
            get => _accountId;
            set
            {
                if (_accountId == value)
                {
                    return;
                }

                _accountId = value;
                OnPropertyChanged(nameof(AccountId));
            }
        }

        private string _totalBalanceDisplay = "$10,250.00 +$325.50";
        public string TotalBalanceDisplay
        {
            get => _totalBalanceDisplay;
            set
            {
                if (_totalBalanceDisplay == value)
                {
                    return;
                }

                _totalBalanceDisplay = value;
                OnPropertyChanged(nameof(TotalBalanceDisplay));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
