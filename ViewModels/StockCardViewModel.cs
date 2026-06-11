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
        private const int MarketSecondCapacity = 840 * 60;
        private const int GraphSampleCount = 140;

        public StockCardViewModel()
        {
            minutePrices.CollectionChanged += OnMinutePricesChanged;
        }

        public ObservableCollection<long> minutePrices { get; } = new();

        private PointCollection _pricePoints = new();
        public PointCollection pricePoints
        {
            get => _pricePoints;
            private set => SetProperty(ref _pricePoints, value);
        }

        private Brush _pricePathBrush = Brushes.Blue;
        public Brush pricePathBrush
        {
            get => _pricePathBrush;
            private set => SetProperty(ref _pricePathBrush, value);
        }

        private int? _lastSecondKey;

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
            set
            {
                if (SetProperty(ref _open, value))
                {
                    UpdatePricePathBrush();
                }
            }
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
            if (minutePrices.Count >= MarketSecondCapacity)
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
        private void UpdateSecondPriceFromTime(string chetime, long currentPrice)
        {
            if (!TryGetSecondKey(chetime, out var secondKey))
            {
                return;
            }

            if (_lastSecondKey is null || secondKey > _lastSecondKey)
            {
                AddMinutePrice(currentPrice);
                _lastSecondKey = secondKey;
                return;
            }

            if (secondKey == _lastSecondKey && minutePrices.Count > 0)
            {
                minutePrices[minutePrices.Count - 1] = currentPrice;
            }
        }

        private static bool TryGetSecondKey(string chetime, out int secondKey)
        {
            secondKey = 0;

            if (string.IsNullOrWhiteSpace(chetime))
            {
                return false;
            }

            var normalized = chetime.PadLeft(6, '0');

            if (normalized.Length < 6)
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

            if (!int.TryParse(normalized.Substring(4, 2), out var second))
            {
                return false;
            }

            if (hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59)
            {
                return false;
            }

            secondKey = (hour * 3600) + (minute * 60) + second;
            return true;
        }

        private void UpdatePricePath()
        {
            if (minutePrices.Count < 2)
            {
                pricePoints = new PointCollection();
                return;
            }

            var bucketCount = Math.Min(GraphSampleCount, minutePrices.Count);
            var bucketSize = minutePrices.Count / (double)bucketCount;

            long min = long.MaxValue;
            long max = long.MinValue;

            var averagedValues = new double[bucketCount];

            for (var i = 0; i < bucketCount; i++)
            {
                var startIndex = (int)Math.Floor(i * bucketSize);
                var endIndex = (int)Math.Floor((i + 1) * bucketSize);
                endIndex = Math.Clamp(endIndex, startIndex + 1, minutePrices.Count);

                long sum = 0;
                var count = 0;

                for (var j = startIndex; j < endIndex; j++)
                {
                    sum += minutePrices[j];
                    count++;
                }

                var average = count > 0 ? sum / (double)count : minutePrices[startIndex];
                averagedValues[i] = average;
                min = Math.Min(min, (long)Math.Round(average));
                max = Math.Max(max, (long)Math.Round(average));
            }

            var range = Math.Max(1, max - min);
            var points = new PointCollection(bucketCount);

            for (var i = 0; i < bucketCount; i++)
            {
                var normalized = (max - averagedValues[i]) / (double)range;
                points.Add(new Point(i, normalized * 100d));
            }

            points.Freeze();
            pricePoints = points;
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
            UpdateSecondPriceFromTime(data.chetime, data.price);
        }

        private void UpdatePricePathBrush()
        {
            pricePathBrush = price >= open ? Brushes.Red : Brushes.Blue;
        }
    }
}
