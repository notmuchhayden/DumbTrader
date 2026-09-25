using System.Collections.ObjectModel;
using DumbTrader.Core;

namespace DumbTrader.ViewModels
{
    public class SidebarViewModel : ViewModelBase
    {
        public ObservableCollection<string> NavigationItems { get; } = new ObservableCollection<string>
        {
            "계정관리",
            "주식종목모니터",
            "주식관심종목관리",
            "주식개별종목관리",
            "해외선물종목모니터",
            "해외선물관심종목관리",
            "해외선물개별종목관리"
        };

        private int _selectedIndex;
        public int SelectedIndex
        {
            get => _selectedIndex;
            set => SetProperty(ref _selectedIndex, value);
        }
    }
}
