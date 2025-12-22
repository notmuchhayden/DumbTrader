using System.Collections.ObjectModel;
using DumbTrader.Core;

namespace DumbTrader.ViewModels
{
    public class SidebarViewModel : ViewModelBase
    {
        public ObservableCollection<string> NavigationItems { get; } = new ObservableCollection<string>
        {
            "Dashboard",
            "Account",
            "Strategy Simulator",
            "Strategy Programming"
        };

        private int _selectedIndex;
        public int SelectedIndex
        {
            get => _selectedIndex;
            set => SetProperty(ref _selectedIndex, value);
        }
    }
}
