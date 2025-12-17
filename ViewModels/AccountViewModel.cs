using System.Collections.ObjectModel;
using DumbTrader.Models;

namespace DumbTrader.ViewModels
{
    public class AccountViewModel
    {
        public ObservableCollection<AccountInfo> Accounts { get; }

        public AccountViewModel()
        {
            Accounts = new ObservableCollection<AccountInfo>();
        }
    }
}
