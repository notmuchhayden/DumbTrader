# 프로젝트의 개요
* 현재 진행 중인 프로젝트는 주식을 자동매매 하는 프로그램이다.
* 이 프로그램은 특정 알고리즘을 기반으로 주식 시장 데이터를 분석하고, 매수 및 매도 신호를 자동으로 생성하여 거래를 수행한다.
* 증권사와 XingAPI를 활용하여 실시간으로 주식 거래를 처리한다.
	* XingAPI는 Services/IXAQueryService.cs, Services/IXARealService.cs 를 통하여 접근 할수 있다.
	* XingAPI 는 COM 객체로 제공되며, C#에서 COM 인터페이스를 통해 접근할 수 있다.
	* XingAPI 는 멀티스레드가 지원되지 않는 STA 모델이므로, 별도의 스레드에서 COM 객체를 생성하고 관리해야 한다.
	* XingAPI 는 증권사에 무제한의 요청을 보낼 수 있는 것이 아니며, 일정량의 요청 제한이 존재한다. (예: 1초에 5회 요청 등) 따라서 요청 제한을 준수하기 위한 큐잉 메커니즘이 필요하다.
* Dashboard 는 내가 관심있는 종목들을 모니터링하고, 실시간으로 주가 변동을 확인할 수 있는 기능을 제공한다.

# Task
* XAQuery 은 STA 모델의 COM 객체이기 때문에, 멀티스레드 환경에서 안전하게 접근할 수 있도록 래핑하는 것이 필요하다.
* Servies 폴더에 XAQueryWorker.cs 를 추가 한다.
* XAQueryWorker 는 생산자/소비자 모델로서 사용자가 Request() 함수로 요청을 하면, 내부적으로 IXAQeuryService 인터페이스를 통하여 작업을 처리 한다.
* XAQueryWorker 에서 작업이 완료되면, XAQueryWorker.Result 에 작업 결과를 저장한다.
* XAQueryWorker 는 싱글톤 패턴으로 구현하여, 애플리케이션 전체에서 하나의 인스턴스만 존재하도록 한다.

