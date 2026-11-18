using System.Collections.ObjectModel;
using DumbTrader.Core;

namespace DumbTrader.ViewModels
{
    public class SidebarViewModel : ViewModelBase
    {
        public ObservableCollection<string> NavigationItems { get; } = new ObservableCollection<string>
        {
            "계좌 관리",
            "주식 모니터",
            "주식 관심종목",
            "주식 상세",
            "해외선물 모니터",
            "해외선물 관심계약",
            "해외선물 상세"
        };

        private int _selectedIndex;
        public int SelectedIndex
        {
            get => _selectedIndex;
            set => SetProperty(ref _selectedIndex, value);
        }
    }
}
