using DumbTrader.Core;
using DumbTrader.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace DumbTrader.ViewModels
{
    public class StockCardViewModel : ViewModelBase
    {
        private const int MarketMinuteCapacity = 420;

        public StockCardViewModel()
        {
            minutePrices.CollectionChanged += OnMinutePricesChanged;
        }

        public ObservableCollection<long> minutePrices { get; } = new();

        private Geometry _pricePath = Geometry.Empty;
        public Geometry pricePath
        {
            get => _pricePath;
            private set => SetProperty(ref _pricePath, value);
        }

        private int? _lastMinuteKey;

        // 종목명
        private string _hname = string.Empty;
        public string hname
        {
            get => _hname;
            set => SetProperty(ref _hname, value);
        }

        // 종목코드
        private string _shcode = string.Empty;
        public string shcode
        {
            get => _shcode;
            set => SetProperty(ref _shcode, value);
        }

        // 현재가
        private long _price;
        public long price
        {
            get => _price;
            set => SetProperty(ref _price, value);
        }

        // 전일대비
        private long _change;
        public long change
        {
            get => _change;
            set => SetProperty(ref _change, value);
        }

        // 전일대비구분
        private string _sign = string.Empty;
        public string sign
        {
            get => _sign;
            set => SetProperty(ref _sign, value);
        }

        // 등락율
        private float _drate;
        public float drate
        {
            get => _drate;
            set => SetProperty(ref _drate, value);
        }

        // 시가
        private long _open;
        public long open
        {
            get => _open;
            set => SetProperty(ref _open, value);
        }

        // 고가
        private long _high;
        public long high
        {
            get => _high;
            set => SetProperty(ref _high, value);
        }

        // 저가
        private long _low;
        public long low
        {
            get => _low;
            set => SetProperty(ref _low, value);
        }

        // 매수호가
        private long _bidho;
        public long bidho
        {
            get => _bidho;
            set => SetProperty(ref _bidho, value);
        }

        // 매도호가
        private long _offerho;
        public long offerho
        {
            get => _offerho;
            set => SetProperty(ref _offerho, value);
        }

        public void AddMinutePrice(long currentPrice)
        {
            if (minutePrices.Count >= MarketMinuteCapacity)
            {
                minutePrices.RemoveAt(0);
            }

            minutePrices.Add(currentPrice);
        }

        private void OnMinutePricesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdatePricePath();
        }

        // 체결시간을 기반으로 분봉 가격 업데이트
        private void UpdateMinutePriceFromTime(string chetime, long currentPrice)
        {
            if (!TryGetMinuteKey(chetime, out var minuteKey))
            {
                return;
            }

            if (_lastMinuteKey is null || minuteKey > _lastMinuteKey)
            {
                AddMinutePrice(currentPrice);
                _lastMinuteKey = minuteKey;
                return;
            }

            if (minuteKey == _lastMinuteKey && minutePrices.Count > 0)
            {
                minutePrices[minutePrices.Count - 1] = currentPrice;
            }
        }

        private static bool TryGetMinuteKey(string chetime, out int minuteKey)
        {
            minuteKey = 0;

            if (string.IsNullOrWhiteSpace(chetime))
            {
                return false;
            }

            var normalized = chetime.PadLeft(6, '0');

            if (normalized.Length < 4)
            {
                return false;
            }

            if (!int.TryParse(normalized.Substring(0, 2), out var hour))
            {
                return false;
            }

            if (!int.TryParse(normalized.Substring(2, 2), out var minute))
            {
                return false;
            }

            if (hour is < 0 or > 23 || minute is < 0 or > 59)
            {
                return false;
            }

            minuteKey = (hour * 60) + minute;
            return true;
        }

        private void UpdatePricePath()
        {
            if (minutePrices.Count < 2)
            {
                pricePath = Geometry.Empty;
                return;
            }

            long min = long.MaxValue;
            long max = long.MinValue;

            foreach (var value in minutePrices)
            {
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            var range = Math.Max(1, max - min);
            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                for (var i = 0; i < minutePrices.Count; i++)
                {
                    var normalized = (max - minutePrices[i]) / (double)range;
                    var point = new Point(i, normalized * 100d);

                    if (i == 0)
                    {
                        context.BeginFigure(point, false, false);
                    }
                    else
                    {
                        context.LineTo(point, true, false);
                    }
                }
            }

            geometry.Freeze();
            pricePath = geometry;
        }

        /// <summary>
        /// RealS3_K3_Data로부터 카드 데이터를 업데이트합니다.
        /// </summary>
        public void UpdateFromRealData(RealS3_K3_Data data)
        {
            price = data.price;
            change = data.change;
            sign = data.sign;
            drate = data.drate;
            open = data.open;
            high = data.high;
            low = data.low;
            bidho = data.bidho;
            offerho = data.offerho;
            UpdateMinutePriceFromTime(data.chetime, data.price);
        }
    }
}
