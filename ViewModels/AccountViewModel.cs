using System.Collections.ObjectModel;
using DumbTrader.Models;
using DumbTrader.Core;

namespace DumbTrader.ViewModels
{
    public class AccountViewModel : ViewModelBase
    {
        public ObservableCollection<AccountInfo> Accounts { get; }

        public AccountViewModel()
        {
            Accounts = new ObservableCollection<AccountInfo>();
        }
    }
}
