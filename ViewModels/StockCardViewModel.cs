using DumbTrader.Core;
using DumbTrader.Models;

namespace DumbTrader.ViewModels
{
    public class StockCardViewModel : ViewModelBase
    {
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
        }
    }
}
