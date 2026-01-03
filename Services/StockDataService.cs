using System.Collections.Generic;
using System.Threading.Tasks;

namespace DumbTrader.Services
{
    /// <summary>
    /// 증권(주식) 데이터 요청을 담당하는 서비스. IXAQueryService를 래핑하여 더 직관적인 API 제공
    /// </summary>
    public class StockDataService
    {
        private readonly Dictionary<string, IXAQueryService> _xaQueryServices;

        public StockDataService()
        {
            // t8430 초기화
            _xaQueryServices = new Dictionary<string, IXAQueryService>();

            // 주식 현재가 호가 조회
            var t1101 = new XAQueryService();
            t1101.ResFileName = "Res\\t1101.res";
            _xaQueryServices.Add("t1101", t1101);

            // 주식종목조회
            var t8430 = new XAQueryService();
            t8430.ResFileName = "Res\\t8430.res";
            _xaQueryServices.Add("t8430", t8430);
        }

        //tcode로 IXAQueryService 가져오기
        public IXAQueryService? GetQueryService(string tcode)
        {
            _xaQueryServices.TryGetValue(tcode, out var service);
            return service;
        }
    }
}
