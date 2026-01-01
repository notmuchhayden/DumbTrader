using System.Collections.ObjectModel;
using DumbTrader.Core;

namespace DumbTrader.ViewModels
{
    public class SidebarViewModel : ViewModelBase
    {
        public ObservableCollection<string> NavigationItems { get; } = new ObservableCollection<string>
        {
            "종목모니터",
            "계정관리",
            "관심종목관리",
            "개별종목관리"
        };

        private int _selectedIndex;
        public int SelectedIndex
        {
            get => _selectedIndex;
            set => SetProperty(ref _selectedIndex, value);
        }
    }
}
