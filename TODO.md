
# TODO

## 계좌 관리 개발 (AccountView, AccountViewModel)
- [ ] 마지막 선택된 계좌를 기억하는 기능 구현. Login 정보에 마지막 선택된 계좌를 저장
- [ ] 계좌 정보 출력 기능 구현 : 계좌 목록 하단에 선택한 계좌의 상세 정보 출력

## 요약정보 개발 (SummaryView, SummaryViewModel)
- [ ] 시작시 계좌 잔고 정보를 TR 로 읽어서 초기값 설정
- [ ] 매매가 일어날 때마다 계좌 잔고 정보가 갱신되도록 구현

## 관심 종목 관리 개발 (WatchlistView, WatchlistViewModel)

## 개별 종목 관리 개발 (StockDetailView.xaml, StockDetailViewModel.cs)
- [ ] 추후 종목 리스트에서 더블클릭시 개별 종목 정보 화면으로 이동하는 기능 추가
- [ ] 매매 전략 화면은 개별 종목 관리 화면에 통합
- [ ] 현재 과거데이터 버튼을 데이터 수집으로 변경. 과거데이터 검색은 다른 버튼 추가하기
- [ ] 과거데이터 검색 조건을 좀더 다양하게 추가 (예: 기간 설정 등)

## 대시보드 개발 (DashboardView, DashboardViewModel, StockCardControl)
- [ ] DashboardViewModel 에 관심 종목의 실시간 데이터를 수신할 수 있도록 연결  ==> 여기 작업중
- [ ] 스크립트 작성 테스트. 실제 "Hello world" 출력 스크립트를 추가하여 작동하는지 검증 
- [ ] 스크립트 매매 API 구현

## 1차 배포
- [ ] 2026년 1월 17일 => 2026년 3월 2일

---
# DONE

## MainWindow 개발
- [x] menu 에 View 메뉴 추가. View 메뉴에는 Summary, Watchlist, Dashboard 메뉴 아이템 추가
- [x] 메뉴에 dock view 보이기/숨기기 기능 구현. 현재 닫은 dock view 는 다시 열 수 없음

## 계좌 관리 시스템 개발
- [x] 계좌번호 불러오기 테스트
	- [x] GetAccountListCount() 함수 테스트
	- [x] GetAccountList() 함수 테스트
	- [x] GetAccountName() 함수 테스트
- [x] 계좌 번호를 통해 계좌 정보 불러오기
- [x] AccountInfo 클래스 구현
- [x] 계좌 정보를 Entity Framework 모델로 변환
- [x] 조회후 데이터를 데이터베이스에 저장

## 요약정보 화면 개발 (SummaryView, SummaryViewModel))
- [x] 요약정보 화면 UI 기획
- [x] 계좌 정보 표시 기능 구현
	- [x] 계좌 선택시 계좌 정보 갱신

## 관심 종목 관리화면 개발 (WatchlistView, WatchlistViewModel)
- [x] 주식 종목 리스트 화면 - t8430
	- [x] UI 기획
	- [x] WatchlistView, WatchlistViewModel 구현
	- [x] UI 기획 이미지로 부터 실제 UI 구현
- [x] SidebarView 에서 '관심종목관리' 버튼 클릭시 관심 종목 관리화면으로 이동 기능 구현
- [x] DBContext 에 종목 정보 저장
- [x] 조회 버튼 추가. 버튼 클릭시 주식 t8430 을 통해서 주식 종목 리스트 조회
- [x] DB 에 종목 리스트 저장 기능 구현
- [x] 종목 리스트 검색 및 필터링 기능 추가
- [x] 관심 종목 추가/삭제 기능 추가. 관심 종목은 종목 리스트의 왼쪽에 종목 이름만 나열
- [x] 관심 종목을 config.json 에 저장하는 기능 구현

## 개별 종목 관리 화면 개발 (StockDetailView.xaml, StockDetailViewModel.cs)
- [x] 관심종목 클릭시 db 에 저장된 과거 데이터 조회 및 출력 기능 구현
	- [x] 조회 버튼을 눌렀을 때 과거 데이터가 갱신되도록 구현
- [x] Roslyn 설치
- [x] 과거데이터 조회 기능 추가. 과거 8년 (t8410)
	- [x] StockDetailViewModel 에서 관심종목을 불러오는 부분 작업중
	- [x] DBContext 에 과거 데이터 저장 기능 구현 
	- [x] 과거 데이터 조회 기능 구현
- [x] 관심종목 목록과 개별종목 사이에 크기를 조절할 수 있는 GridSplitter 추가
- [x] '설정' 그룹의 아이템 사이 간격을 촘촘하게 변경
- [x] '과거데이터' 그룹에서 CartesianChart 와 DataGrid 를 좌우로 배치. 이 때 DataGrid 가 왼쪽, CartesianChart 가 오른쪽에 위치하도록 변경. 또한 DataGrid 와 CartesianChart 사이에 GridSplitter 추가.
- [x] ~~LiveCharts2 를 이용해서 과거 데이터 차트 출력. 시가, 고가, 저가, 종가로는 캔들차트 출력. 거래량은 막대차트로 출력~~
- [x] ~~LiveCharts2 를 ScottPlot 으로 변경~~
- [x] ScottPlot 의 데이터가 화면 크기에 맞게 자동으로 조절되지 않는 문제 해결
	- [x] 이 문제는 `Views/StockDetailView.xaml.cs`에서 `Axes.AutoScale()` 호출로 해결됨
- [x] ScottPlot의 그래프를 캔들차트로 변경
	- [x] 구현: `Views/StockDetailView.xaml.cs`에서 `plt.Add.Candlestick(...)` 및 거래량 막대 추가로 처리함
- [x] ScottPlot 에서 마우스 호버시 툴팁으로 데이터 값 표시 기능 추가
- [x] 매매 전략은 Roslyn 설정 추가
- [x] 메인전략, 매수전략, 매도전략 옵션 추가. 컨트롤은 콤보박스
- [x] 프로그램 시작할 때 strategy/main, strategy/sell, strategy/buy 폴더 없으면 생성
- [x] strategy/main, strategy/sell, strategy/buy 폴 *.cs 파일 목록을 자동으로 읽고 전략 설정 콤보박스에 설정
- [x] StrategyService._strategyStocks 에 전략파일명 저장/불러오기 기능 추가.
- [x] StockDetailViewModel 시작시 StrategyService 에 전략이 이미 저장되어있으면 해당 전략을 불러와서 설정하도록 구현
- [x] Roslyn API 로 Runner 클래스 구현


## 대시보드 개발 (DashboardView, DashboardViewModel, StockCardControl)
- [x] 관심종목의 요약 정보를 볼 수 있는 개별 카드 디자인 개발
- [x] StockRealService 클래스 구현. 이 클래스는 XARealService 를 상속받아서 실시간 데이터를 수신하는 역할을 담당
	- [x] S3_ TR 구현
	- [x] database 저장
- [x] 실시간 데이터가 수신 되었을 때 EventHandler 를 통해 알림 구현
- [x] 시작시 관심 종목의 수대로 대시보드 카드가 생성되도록 구현
- [x] 실시간 데이터를 수신하면 모델을 변경하고 자동으로 대시보드가 갱신 구현

## Login 개발
- [x] 로그인 화면 UI 구현
- [x] 로그인 정보 저장/불러오기 기능 리팩토링
- [x] 기존에 Login 과 Account 정보가 섞여있던 부분을 분리

## 조회 TR 개발
- [x] t1101 (개별종목 현재가) 개발
- [x] t8407 주식 현재가 조회 
- [x] t8410 주식 차트 조회 
- [x] 실시간 TR 클래스 구현

