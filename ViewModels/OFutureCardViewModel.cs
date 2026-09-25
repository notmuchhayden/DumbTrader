using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using DumbTrader.Core;
using DumbTrader.Models;

namespace DumbTrader.ViewModels
{
    public sealed class OFutureCardViewModel : ViewModelBase
    {
        private const int MaxPriceSamples = 40;
        private readonly ObservableCollection<double> _priceSamples = new();

        public string Name { get; }
        public string Symbol { get; }
        public PointCollection PricePoints { get; private set; } = new();

        private double _price;
        public double Price
        {
            get => _price;
            private set
            {
                if (SetProperty(ref _price, value))
                    UpdatePricePathBrush();
            }
        }

        private double _change;
        public double Change
        {
            get => _change;
            private set => SetProperty(ref _change, value);
        }

        private string _changeSign = string.Empty;
        public string ChangeSign
        {
            get => _changeSign;
            private set => SetProperty(ref _changeSign, value);
        }

        private double _changeRate;
        public double ChangeRate
        {
            get => _changeRate;
            private set => SetProperty(ref _changeRate, value);
        }

        private double _open;
        public double Open
        {
            get => _open;
            private set
            {
                if (SetProperty(ref _open, value))
                    UpdatePricePathBrush();
            }
        }

        private double _high;
        public double High
        {
            get => _high;
            private set => SetProperty(ref _high, value);
        }

        private double _low;
        public double Low
        {
            get => _low;
            private set => SetProperty(ref _low, value);
        }

        private double _bidPrice;
        public double BidPrice
        {
            get => _bidPrice;
            private set => SetProperty(ref _bidPrice, value);
        }

        private double _askPrice;
        public double AskPrice
        {
            get => _askPrice;
            private set => SetProperty(ref _askPrice, value);
        }

        private long _volume;
        public long Volume
        {
            get => _volume;
            private set => SetProperty(ref _volume, value);
        }

        private string _tradeTime = string.Empty;
        public string TradeTime
        {
            get => _tradeTime;
            private set => SetProperty(ref _tradeTime, value);
        }

        private Brush _pricePathBrush = Brushes.DodgerBlue;
        public Brush PricePathBrush
        {
            get => _pricePathBrush;
            private set => SetProperty(ref _pricePathBrush, value);
        }

        public OFutureCardViewModel(OFutureContract contract)
        {
            Name = contract.Name;
            Symbol = contract.Symbol;
        }

        public void Update(OFutureRealData? trade, OFutureOrderBook? orderBook)
        {
            if (trade is not null)
            {
                Price = trade.Price;
                Change = trade.Change;
                ChangeSign = trade.ChangeSign;
                ChangeRate = trade.ChangeRate;
                Open = trade.Open;
                High = trade.High;
                Low = trade.Low;
                Volume = trade.TotalQuantity;
                TradeTime = string.IsNullOrWhiteSpace(trade.KoreaTradeTime) ? trade.TradeTime : trade.KoreaTradeTime;
                AddPriceSample(trade.Price);
            }

            if (orderBook is not null)
            {
                BidPrice = orderBook.BidPrices[0];
                AskPrice = orderBook.AskPrices[0];
            }
        }

        private void AddPriceSample(double price)
        {
            if (!double.IsFinite(price)) return;
            _priceSamples.Add(price);
            while (_priceSamples.Count > MaxPriceSamples)
                _priceSamples.RemoveAt(0);

            var min = _priceSamples.Min();
            var max = _priceSamples.Max();
            var range = Math.Max(max - min, Math.Max(Math.Abs(max) * 0.0001, 0.0001));
            var points = new PointCollection(_priceSamples.Count);
            for (var index = 0; index < _priceSamples.Count; index++)
            {
                var x = _priceSamples.Count <= 1 ? 0 : index * 100d / (_priceSamples.Count - 1);
                var y = (max - _priceSamples[index]) / range * 100d;
                points.Add(new Point(x, y));
            }
            points.Freeze();
            PricePoints = points;
            OnPropertyChanged(nameof(PricePoints));
        }

        private void UpdatePricePathBrush()
            => PricePathBrush = Price >= Open ? Brushes.Red : Brushes.Blue;
    }
}
