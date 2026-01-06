using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DumbTrader.Models;

namespace DumbTrader.Services
{
    public class WatchlistService
    {
        private ObservableCollection<StockInfo> _watchlist;

        public WatchlistService()
        {
            _watchlist = new ObservableCollection<StockInfo>();
        }

        public ObservableCollection<StockInfo> Watchlist => _watchlist;

        public void AddToWatchlist(StockInfo stock)
        {
            if (!_watchlist.Contains(stock))
            {
                _watchlist.Add(stock);
            }
        }

        public void RemoveFromWatchlist(StockInfo stock)
        {
            if (_watchlist.Contains(stock))
            {
                _watchlist.Remove(stock);
            }
        }
    }
}
