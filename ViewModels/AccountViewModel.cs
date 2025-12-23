using System.Collections.ObjectModel;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
    public class AccountViewModel : ViewModelBase
    {
        private readonly IXingSessionService _sessionService;
        public ObservableCollection<AccountInfo> Accounts { get; }

        public AccountViewModel(IXingSessionService sessionService)
        {
            _sessionService = sessionService;
            Accounts = new ObservableCollection<AccountInfo>();
        }
    }
}
