using System.Collections.ObjectModel;
using DumbTrader.Core;

namespace DumbTrader.ViewModels
{
    public class LogViewModel : ViewModelBase
    {
        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>
        {
            "15:23 Error: Connection timeout",
            "15:21 Connecting to exchange server..."
        };
    }
}
