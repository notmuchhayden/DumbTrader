using System.Collections.ObjectModel;
using System.ComponentModel;

namespace DumbTrader.ViewModels
{
public class WatchlistViewModel : INotifyPropertyChanged
    {
  public ObservableCollection<string> Items { get; } = new();

        public WatchlistViewModel()
    {
  // Example data
  Items.Add("AAPL");
        Items.Add("MSFT");
   Items.Add("GOOG");
   }

   public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
       PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
