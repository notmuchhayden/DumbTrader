using System.Collections.ObjectModel;
using System.ComponentModel;

namespace DumbTrader.ViewModels
{
    public class SidebarViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<string> NavigationItems { get; } = new ObservableCollection<string>
        {
            "Dashboard",
            "Trade History",
            "Strategy Simulator",
            "Strategy Programming"
        };

        private int _selectedIndex;
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex == value)
                {
                    return;
                }

                _selectedIndex = value;
                OnPropertyChanged(nameof(SelectedIndex));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
