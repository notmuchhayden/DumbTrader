# TODO List

# 서비스 개발

## TR 개발 (StockDataService)
- [x] 동기화 문제를 해결하기 위해 IStockDataService 인터페이스를 정의하고, TR 호출을 담당하는 StockDataService 클래스에서 해당 인터페이스를 구현하도록 구조 변경
- [x] 주식 차트 조회시 조회수 제한 문제 해결 : TR 호출 시점에 조회수 제한이 걸리는 경우, 일정 시간 대기 후 재시도하는 로직 구현
- [ ] 주식 매수 요청 TR 개발
- [ ] 주식 매도 요청 TR 개발

## 관심종목 및 전략 개발 (StrategyService)
- [x] 프로그램 시작시 관심종목의 최신 데이터를 TR 로 읽어서 초기값 설정


---

# View/ViewModel 개발

## Login 개발 (LoginView, LoginViewModel)
- [x] Bug : 저장된 패스워드가 있으면 ***** 으로 표시되도록 수정

## 스플래시 화면 개발 (SplashScreenView, SplashScreenViewModel)
- [x] 로그인과 MainView 사이에 스플래시 화면 추가

## 계좌 관리 개발 (AccountView, AccountViewModel)
- [ ] 마지막 선택된 계좌를 기억하는 기능 구현. Login 정보에 마지막 선택된 계좌를 저장
- [ ] 계좌 정보 출력 기능 구현 : 계좌 목록 하단에 선택한 계좌의 상세 정보 출력

## 요약정보 개발 (SummaryView, SummaryViewModel)
- [ ] 시작시 계좌 잔고 정보를 TR 로 읽어서 초기값 설정
- [ ] 매매가 일어날 때마다 계좌 잔고 정보가 갱신되도록 구현

## 관심 종목 관리 개발 (WatchlistView, WatchlistViewModel)

## 개별 종목 관리 개발 (StockDetailView.xaml, StockDetailViewModel.cs)
- [x] DataGrid 에서 각 열이 데이터의 길이에 맞게 자동으로 크기가 조절되도록 구현
- [ ] 시뮬레이션 결과 표시
- [ ] 그래프에서 Annotation 정보가 Candlestick 차트의 캔들에 정확히 매칭되도록 구현
- [ ] DataGrid 에서 기본적으로 날짜순으로 정렬되도록 구현
	- [ ] Bug: 날짜순으로 정렬되도록 설정해도 Binding 이 되는 순간 다시 원래 순서로 돌아감. => DataGrid 의 ItemsSource 가 변경될 때마다 정렬이 초기화 되는 문제 발생
- [ ] 과거데이터 검색시 최신 DB 데이터의 날짜를 기준으로 오늘의 날짜까지 검색하도록 구현


## 대시보드 개발 (DashboardView, DashboardViewModel, StockCardControl)
- [x] DashboardViewModel 에 관심 종목의 실시간 데이터를 수신할 수 있도록 연결
- [x] 시작시 관심 종목의 최신 데이터를 TR 로 읽어서 초기값 설정
- [ ] 초기값을 차트 데이터 말고 t1101 데이터로 변경. 차트 데이터는 호출 제한이 있기 때문
- [ ] 스크립트 동작 테스트
- [ ] 스크립트 매매 API 구현

## LogView 개발 (LogView, LogViewModel)
- [x] 로그 출력에 스크롤바 추가
- [x] 스레드 안전하게 UI 업데이트 되도록 구현


---

## 1차 배포
- [ ] 2026년 3월 8일

