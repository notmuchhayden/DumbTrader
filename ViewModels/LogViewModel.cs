using System.Collections.ObjectModel;
using System.ComponentModel;

namespace DumbTrader.ViewModels
{
    public class LogViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>
        {
            "15:23 Error: Connection timeout",
            "15:21 Connecting to exchange server..."
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
