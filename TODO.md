
# TODO

## Login 개발
- [x] Bug : 저장된 패스워드가 있으면 ***** 으로 표시되도록 수정

## 계좌 관리 개발 (AccountView, AccountViewModel)
- [ ] 마지막 선택된 계좌를 기억하는 기능 구현. Login 정보에 마지막 선택된 계좌를 저장
- [ ] 계좌 정보 출력 기능 구현 : 계좌 목록 하단에 선택한 계좌의 상세 정보 출력

## 요약정보 개발 (SummaryView, SummaryViewModel)
- [ ] 시작시 계좌 잔고 정보를 TR 로 읽어서 초기값 설정
- [ ] 매매가 일어날 때마다 계좌 잔고 정보가 갱신되도록 구현

## 관심 종목 관리 개발 (WatchlistView, WatchlistViewModel)

## 개별 종목 관리 개발 (StockDetailView.xaml, StockDetailViewModel.cs)
- [x] 과거 데이터로 시뮬레이션 기능 추가.
	- [x] 시뮬레이션 설정 UI 추가 (자본금)
	- [x] 시뮬레이션 시작 버튼 추가 
	- [x] Roslyn 스크립트에 메인 스레드의 UI 요소들에 접근 할 수 있는지 확인
	
- [ ] 과거데이터 검색 조건을 좀더 다양하게 추가 (예: 기간 설정 등)
- [ ] Bug : 그래프에서 최신 1주일이 출력되지 않고 있음.


## 대시보드 개발 (DashboardView, DashboardViewModel, StockCardControl)
- [x] DashboardViewModel 에 관심 종목의 실시간 데이터를 수신할 수 있도록 연결 
- [ ] 스크립트 동작 테스트
- [ ] 스크립트 매매 API 구현

## LogView 개발 (LogView, LogViewModel)
- [x] 로그 출력에 스크롤바 추가
- [x] 스레드 안전하게 UI 업데이트 되도록 구현

## 1차 배포
- [ ] 2026년 3월 8일

