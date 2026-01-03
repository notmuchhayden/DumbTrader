using System.Collections.Generic;

namespace DumbTrader.Services
{
    /// <summary>
    /// 증권(주식) 실시간 데이터 처리를 담당하는 서비스. IXARealService를 래핑하여 더 직관적인 API 제공
    /// </summary>
    public class StockRealDataService
    {
        private readonly Dictionary<string, IXARealService> _xaRealServices;

        public StockRealDataService()
        {
            _xaRealServices = new Dictionary<string, IXARealService>();
            // 예시: 실시간 시세(t1102) 등록
            // var t1102 = new XARealService();
            // t1102.ResFileName = "Res\\t1102.res";
            // t1102.AddReceiveRealDataEventHandler(OnT1102RcvHandler);
            // _xaRealServices.Add("t1102", t1102);
        }

        // tcode로 IXARealService 가져오기
        public IXARealService? GetRealService(string tcode)
        {
            _xaRealServices.TryGetValue(tcode, out var service);
            return service;
        }

        // 실시간 데이터 수신 핸들러 예시
        // private void OnT1102RcvHandler(string trCode)
        // {
        // // t1102 실시간 데이터 처리 로직 작성
        // }
    }
}
