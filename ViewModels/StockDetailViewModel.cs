using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;
using ScottPlot;
using ScottPlot.WPF;
using System.IO;

namespace DumbTrader.ViewModels
{
    public enum ChartQueryPeriod
    {
        OneDay,
        AWeek,
        AMonth,
        ThreeMonths,
        SixMonths,
        AYear,
        ThreeYears,
        FiveYears,
        All
    }


    public class StockDetailViewModel : ViewModelBase
    {
        // 서버, DB 에 접근하는 서비스들
        private readonly StockDataService _stockDataService;
        // 전략 서비스
        private readonly StrategyService _strategyService;
        private readonly LoggingService _loggingService;
        private readonly DumbTraderDbContext _dbContext;

        // 관심 종목 리스트 바인딩
        public ObservableCollection<StrategyStockInfo> Watchlist { get; }

        // 현재 선택된 관심종목 바인딩
        private StrategyStockInfo? _selectedWatchlist;
        public StrategyStockInfo? SelectedWatchlist
        {
            get => _selectedWatchlist;
            set
            {
                if (SetProperty(ref _selectedWatchlist, value))
                {
                    // 선택이 되면 해당 종목의 차트 데이터를 조회하여 그래프 갱신
                    if (value != null)
                    {
                        QueryChartData(ChartQueryPeriod.AYear);
                    }
                    else
                    {
                        ChartData = new ObservableCollection<StockChartData>();
                    }

                    // 전략 파일 선택도 갱신 (파일명만 보여주고, 실제 경로는 StrategyStockInfo.Strategy에 저장)
                    SelectedMainStrategyFile = GetSelectedStrategyOrEmpty(value?.Strategy?.MainStrategyPath, _mainStrategyFiles);
                    SelectedBuyStrategyFile = GetSelectedStrategyOrEmpty(value?.Strategy?.BuyStrategyPath, _buyStrategyFiles);
                    SelectedSellStrategyFile = GetSelectedStrategyOrEmpty(value?.Strategy?.SellStrategyPath, _sellStrategyFiles);
                }
            }
        }

        // 차트 데이터 바인딩. ChartData 는 대량으로 변경될 수 있으므로 SetProperty 사용
        private ObservableCollection<StockChartData> _chartData = new ObservableCollection<StockChartData>();
        public ObservableCollection<StockChartData> ChartData
        {
            get => _chartData;
            set
            {
                if (SetProperty(ref _chartData, value))
                {
                    UpdatePlot();
                }
            }
        }

        // 주전략 파일 경로
        private ObservableCollection<string> _mainStrategyFiles = new ObservableCollection<string>();
        public ObservableCollection<string> MainStrategyFiles
        {
            get => _mainStrategyFiles;
        }

        // 선택된 주전략 파일 (바인딩)
        private string? _selectedMainStrategyFile;
        public string? SelectedMainStrategyFile
        {
            get => _selectedMainStrategyFile;
            set
            {
                if (SetProperty(ref _selectedMainStrategyFile, value))
                {
                    if (SelectedWatchlist?.Strategy != null)
                    {
                        SelectedWatchlist.Strategy.MainStrategyPath = value ?? string.Empty;
                        _strategyService.SaveConfig();
                    }
                }
            }
        }

        // 매수 전략 파일 경로
        private ObservableCollection<string> _buyStrategyFiles = new ObservableCollection<string>();
        public ObservableCollection<string> BuyStrategyFiles
        {
            get => _buyStrategyFiles;
        }

        // 선택된 매수 전략 파일 (바인딩)
        private string? _selectedBuyStrategyFile;
        public string? SelectedBuyStrategyFile
        {
            get => _selectedBuyStrategyFile;
            set
            {
                if (SetProperty(ref _selectedBuyStrategyFile, value))
                {
                    if (SelectedWatchlist?.Strategy != null)
                    {
                        SelectedWatchlist.Strategy.BuyStrategyPath = value ?? string.Empty;
                        _strategyService.SaveConfig();
                    }
                }
            }
        }

        // 매도 전략 파일 경로
        private ObservableCollection<string> _sellStrategyFiles = new ObservableCollection<string>();
        public ObservableCollection<string> SellStrategyFiles
        {
            get => _sellStrategyFiles;
        }

        // 선택된 매도 전략 파일 (바인딩)
        private string? _selectedSellStrategyFile;
        public string? SelectedSellStrategyFile
        {
            get => _selectedSellStrategyFile;
            set
            {
                if (SetProperty(ref _selectedSellStrategyFile, value))
                {
                    if (SelectedWatchlist?.Strategy != null)
                    {
                        SelectedWatchlist.Strategy.SellStrategyPath = value ?? string.Empty;
                        _strategyService.SaveConfig();
                    }
                }
            }
        }

        // 시뮬레이션 자본금 바인딩
        private int _simulationSeedMoney;
        public int SimulationSeedMoney
        {
            get => _simulationSeedMoney;
            set => SetProperty(ref _simulationSeedMoney, value);
        }

        // 시뮬레이션 기간 시작일 바인딩
        private DateTime _simulationStartDate;
        public DateTime SimulationStartDate
        {
            get => _simulationStartDate;
            set => SetProperty(ref _simulationStartDate, value);
        }


        // QueryChartDataCommand
        public ICommand QueryChartDataCommand { get; }
        public ICommand StartSimulationCommand { get; }

        // ScottPlot WPF 컨트롤
        public WpfPlot PlotControl { get; } = new WpfPlot();
        private ScottPlot.Plottables.Annotation Annotation { get; set; }

        // 데이터 X축 한계값 (OADate)
        private double _dataMinOADate = double.NaN;
        private double _dataMaxOADate = double.NaN;

        // 생성자
        public StockDetailViewModel(StockDataService stockDataService,
                                    StrategyService strategyService,
                                    LoggingService loggingService,
                                    DumbTraderDbContext dbContext)
        {
            // 서비스 주입
            _stockDataService = stockDataService;
            _strategyService = strategyService;
            _loggingService = loggingService; // keep consistent name used elsewhere
            _dbContext = dbContext;

            // 주식 차트 데이터 업데이트 이벤트 구독
            _stockDataService.StockChartDataInfoUpdated += OnStockChartDataInfoUpdated;

            // ViewModel 이 생성될 때 관심 종목 불러오기 (서비스 컬렉션 인스턴스를 그대로 사용)
            Watchlist = _strategyService.StrategyStocks;

            // 전략 스크립트 파일 목록 읽기
            ReadStrategyFiles();

            // 시뮬레이션 자본금 초기값
            SimulationSeedMoney = 50000000;

            // 시뮬레이션 기간 시작일 초기값
            SimulationStartDate = new DateTime(2010, 1, 1);

            // 차트 데이터 조회
            QueryChartDataCommand = new RelayCommand(ExecuteQueryChartData);

            // 시뮬레이션 시작 명령 (구현 필요)
            StartSimulationCommand = new AsyncRelayCommand(ExecuteStartSimulationAsync);

            // 마우스 호버시 가장 가까운 캔들 데이터 Annotation 표시
            // Annotation 생성 후 바로 추가
            Annotation = PlotControl.Plot.Add.Annotation("Hello");
            SetAnnotation(Annotation);

            PlotControl.MouseMove += (s, e) =>
            {
                var mouse = e.GetPosition(PlotControl);
                var plt = PlotControl.Plot;
                double xMin = plt.Axes.Bottom.Min;
                double xMax = plt.Axes.Bottom.Max;
                double width = PlotControl.ActualWidth;
                double xCoord = xMin + (mouse.X / width) * (xMax - xMin);

                // OADate를 DateTime으로 변환
                DateTime mouseDate = DateTime.FromOADate(xCoord);

                // ScottPlot5.x의 CandlestickPlot에서 데이터 접근
                var candlePlots = plt.GetPlottables().OfType<ScottPlot.Plottables.CandlestickPlot>();

                // Data 접근 방식이 버전에 따라 다르므로, 만약 p.Data.GetOHLCs()가 안 되면 p.GetOHLCs() 시도 등
                // 여기선 이전에 수정한 p.Data.GetOHLCs()를 유지
                var ohlcData = candlePlots.SelectMany(p => p.Data.GetOHLCs()).ToList();

                if (ohlcData.Any())
                {
                    var nearest = ohlcData.OrderBy(x => Math.Abs((x.DateTime - mouseDate).TotalDays)).First();
                    string text = $"Date: {nearest.DateTime:yy/MM/dd}\nOpen: {nearest.Open}\nHigh: {nearest.High}\nLow: {nearest.Low}\nClose: {nearest.Close}";
                    Annotation.Text = text;
                    Annotation.IsVisible = true;
                    Annotation.Alignment = Alignment.UpperLeft;

                    // compute offsets using extracted helper
                    ComputeAnnotationOffsets(mouse.X, mouse.Y, Annotation.Text ?? string.Empty, out var offX, out var offY);
                    Annotation.OffsetX = offX - 50;
                    Annotation.OffsetY = offY - 5;

                    //Annotation.OffsetX = (float)mouse.X;
                    //Annotation.OffsetY = (float)mouse.Y;
                    PlotControl.Refresh();
                }
                else
                {
                    if (Annotation.IsVisible)
                    {
                        Annotation.IsVisible = false;
                        PlotControl.Refresh();
                    }
                }
            };

            // 마우스 상호작용(끌기/휠) 이후 X축을 데이터 범위로 강제 클램프
            PlotControl.MouseUp += (s, e) => ClampXAxis();
            PlotControl.MouseWheel += (s, e) => ClampXAxis();
        }

        private void ExecuteQueryChartData(object? parameter)
        {
            if (SelectedWatchlist != null)
            {
                _stockDataService.RequestStockChartData(
                    SelectedWatchlist.Stock.shcode,
                    DateTime.Today.AddYears(-8),
                    DateTime.Today
                );
            }
        }

        private async Task ExecuteStartSimulationAsync(object? parameter)
        {
            // 시뮬레이션 시작 로직 구현 예정
            if (SelectedWatchlist != null) {
                string shcode = SelectedWatchlist.Stock.shcode;
                _loggingService.Log($"시뮬레이션 시작: 종목코드={shcode}, 자본금={SimulationSeedMoney}, 시작일={SimulationStartDate:yyyy-MM-dd}");

                try
                {
                    // 백그라운드 스레드에서 시뮬레이션 실행 (UI 프리징 방지)
                    bool isSuccess = await Task.Run(() => 
                    {
                        // TODO: _strategyService.Run(shcode) 등 실제 로직 호출
                        // return _strategyService.Run(shcode);
                        return true; // 임시 성공 반환
                    });

                    if (isSuccess)
                    {
                        _loggingService.Log($"시뮬레이션 완료: {shcode}");
                    }
                    else
                    {
                        _loggingService.Log($"시뮬레이션 실패 혹은 중단: {shcode}");
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.Log($"시뮬레이션 중 오류 발생: {ex.Message}");
                }
            }
        }

        private void OnStockChartDataInfoUpdated(object? sender, StockChartDataInfo e)
        {
            // 차트 데이터가 업데이트 되었을 때 주요 정보 로그 기록
            _loggingService.Log($"차트 데이터 업데이트: 종목코드={e.shcode}, 날짜={e.cts_date}, 종가={e.diclose}");
            QueryChartData(ChartQueryPeriod.AYear);
        }

        private void QueryChartData(ChartQueryPeriod period)
        {
            if (SelectedWatchlist == null)
            {
                ChartData = new ObservableCollection<StockChartData>();
                return;
            }

            DateTime endDate = DateTime.Today;
            DateTime startDate = endDate;
            switch (period)
            {
                case ChartQueryPeriod.OneDay:
                    startDate = endDate.AddDays(-1);
                    break;
                case ChartQueryPeriod.AWeek:
                    startDate = endDate.AddDays(-7);
                    break;
                case ChartQueryPeriod.AMonth:
                    startDate = endDate.AddMonths(-1);
                    break;
                case ChartQueryPeriod.ThreeMonths:
                    startDate = endDate.AddMonths(-3);
                    break;
                case ChartQueryPeriod.SixMonths:
                    startDate = endDate.AddMonths(-6);
                    break;
                case ChartQueryPeriod.AYear:
                    startDate = endDate.AddYears(-1);
                    break;
                case ChartQueryPeriod.ThreeYears:
                    startDate = endDate.AddYears(-3);
                    break;
                case ChartQueryPeriod.FiveYears:
                    startDate = endDate.AddYears(-5);
                    break;
                case ChartQueryPeriod.All:
                    startDate = DateTime.MinValue;
                    break;
            }

            // StockChartData.date는 string이므로, 날짜 비교를 위해 yyyyMMdd로 변환
            string startDateStr = startDate.ToString("yyyyMMdd");
            string endDateStr = endDate.ToString("yyyyMMdd");

            var data = _dbContext.StockChartDatas
                .Where(x => x.shcode == SelectedWatchlist.Stock.shcode && string.Compare(x.date, startDateStr) >= 0 && string.Compare(x.date, endDateStr) <= 0)
                .OrderBy(x => x.date)
                .ToList();

            ChartData = new ObservableCollection<StockChartData>(data);
        }

        private void UpdatePlot()
        {
            var data = ChartData;

            var plt = PlotControl.Plot;
            plt.Clear();

            // Annotation 생성 후 바로 추가
            Annotation = plt.Add.Annotation("Hello");
            SetAnnotation(Annotation);

            if (data == null || data.Count == 0)
            {
                PlotControl.Refresh();
                return;
            }

            List<OHLC> ohlcs = new();
            for (int i = 0; i < data.Count; i++)
            {
                var dateTime = DateTime.ParseExact(data[i].date, "yyyyMMdd", CultureInfo.InvariantCulture);
                ohlcs.Add(new OHLC(
                    data[i].open,
                    data[i].high,
                    data[i].low,
                    data[i].close,
                    dateTime,
                    TimeSpan.FromDays(1)));
            }

            // determine data X limits (OADate)
            _dataMinOADate = ohlcs.First().DateTime.ToOADate();
            _dataMaxOADate = ohlcs.Last().DateTime.ToOADate();

            // add candlesticks
            var candlePlot = plt.Add.Candlestick(ohlcs);
            candlePlot.FallingColor = new ScottPlot.Color(0, 0, 255); // 파란색
            candlePlot.RisingColor = new ScottPlot.Color(255, 0, 0); // 빨간색
            plt.Axes.DateTimeTicksBottom();

            // 초기 보기: show last N points (or full range if fewer)
            double fullRange = _dataMaxOADate - _dataMinOADate;
            if (fullRange <= 0)
            {
                plt.Axes.SetLimitsX(_dataMinOADate - 1, _dataMaxOADate + 1);
            }
            else
            {
                // show last ~60 points (or full) — adjust as desired
                double desiredWidth = Math.Min(fullRange, 60); //60 days
                plt.Axes.SetLimitsX(Math.Max(_dataMinOADate, _dataMaxOADate - desiredWidth), _dataMaxOADate);
            }

            // 자동 Y축 스케일링 설정
            plt.Axes.ContinuouslyAutoscale = true;
            plt.Axes.ContinuousAutoscaleAction = (RenderPack rp) =>
            {
                // 현재 보이는 날짜 범위
                DateTime start = DateTime.FromOADate(rp.Plot.Axes.Bottom.Min);
                DateTime end = DateTime.FromOADate(rp.Plot.Axes.Bottom.Max);

                // 해당 범위에 속하는 데이터만 필터링
                var visibleData = ohlcs
                    .Where(d => d.DateTime >= start && d.DateTime <= end)
                    .ToList();

                int min = 0;
                int max = 0;
                foreach (var ohlc in visibleData)
                {
                    if (ohlc.Low < min || min == 0)
                        min = (int)ohlc.Low;
                    if (ohlc.High > max)
                        max = (int)ohlc.High;
                }
                min = (int)(min * 0.999);
                max = (int)(max * 1.001);
                // set vertical axis limits to that range
                rp.Plot.Axes.SetLimitsY(min, max);
            };


            // refresh view
            PlotControl.Refresh();

            // Ensure current X axis is within data bounds (in case autoscale or user action moved it)
            ClampXAxis();
        }

        /// <summary>
        /// Clamp X axis to data range so user can't pan beyond left/right data edges.
        /// Called after user interactions (MouseUp / MouseWheel) and after plot creation.
        /// </summary>
        private void ClampXAxis()
        {
            var plt = PlotControl.Plot;
            double min = plt.Axes.Bottom.Min;
            double max = plt.Axes.Bottom.Max;

            double dataMin = _dataMinOADate;
            double dataMax = _dataMaxOADate;

            if (double.IsNaN(dataMin) || double.IsNaN(dataMax))
                return;

            double width = max - min;
            double dataRange = dataMax - dataMin;

            if (width >= dataRange)
            {
                // If viewport wider than data, show entire data range
                plt.Axes.SetLimitsX(dataMin, dataMax);
            }
            else
            {
                if (min < dataMin)
                {
                    // shifted too far left
                    plt.Axes.SetLimitsX(dataMin, dataMin + width);
                }
                else if (max > dataMax)
                {
                    // shifted too far right
                    plt.Axes.SetLimitsX(dataMax - width, dataMax);
                }
                // otherwise within bounds -> nothing to do
            }

            PlotControl.Refresh();
        }

        /// <summary>
        /// Compute annotation offsets (OffsetX, OffsetY) such that the annotation
        /// remains visible within the PlotControl. Uses a simple text-size heuristic.
        /// </summary>
        private void ComputeAnnotationOffsets(double mouseX, double mouseY, string text, out float offsetX, out float offsetY)
        {
            // Use fixed annotation size to simplify positioning
            const double margin = 8.0; // padding from cursor or from edges
            const double estimatedWidth = 90.0; // fixed annotation width
            const double estimatedHeight = 95.0; // fixed annotation height

            double x = mouseX;
            double y = mouseY;

            double finalOffsetX;
            double finalOffsetY;

            // If placing to the right would overflow the control bounds, place to the left of the cursor
            if (PlotControl.ActualWidth > 0 && x + estimatedWidth + margin > PlotControl.ActualWidth)
            {
                finalOffsetX = x - estimatedWidth - margin;
            }
            else
            {
                finalOffsetX = x + margin;
            }

            // If placing below would overflow the control bounds, place above the cursor
            if (PlotControl.ActualHeight > 0 && y + estimatedHeight + margin > PlotControl.ActualHeight)
            {
                finalOffsetY = y - estimatedHeight - margin;
            }
            else
            {
                finalOffsetY = y + margin;
            }

            // clamp to visible area
            if (finalOffsetX < margin) finalOffsetX = margin;
            if (finalOffsetY < margin) finalOffsetY = margin;

            offsetX = (float)finalOffsetX;
            offsetY = (float)finalOffsetY;
        }

        private void SetAnnotation(ScottPlot.Plottables.Annotation anno)
        {
            anno.IsVisible = false; // 초기에는 표시 안 함
            anno.LabelBackgroundColor = ScottPlot.Colors.Yellow.WithAlpha(0.7);
            anno.LabelFontColor = ScottPlot.Colors.Black;
            anno.LabelBorderColor = ScottPlot.Colors.Black;
            anno.LabelBorderWidth = 1;
            anno.LabelShadowColor = ScottPlot.Colors.Transparent;
            anno.LabelFontName = Fonts.Detect("한국어");
        }

        private static string GetSelectedStrategyOrEmpty(string? strategyPath, ObservableCollection<string> availableFiles)
        {
            if (string.IsNullOrWhiteSpace(strategyPath))
            {
                return string.Empty;
            }

            var fileName = Path.GetFileName(strategyPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            return availableFiles.Contains(fileName) ? fileName : string.Empty;
        }

        private void ReadStrategyFiles()
        {
            _mainStrategyFiles.Clear();
            _buyStrategyFiles.Clear();
            _sellStrategyFiles.Clear();

            // 첫번재 아이템은 선택안함으로 추가
            _mainStrategyFiles.Add(string.Empty);
            _buyStrategyFiles.Add(string.Empty);
            _sellStrategyFiles.Add(string.Empty);

            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
            var strategyRoot = Path.Combine(baseDir, "strategy");

            LoadStrategyFiles(Path.Combine(strategyRoot, "main"), _mainStrategyFiles, baseDir);
            LoadStrategyFiles(Path.Combine(strategyRoot, "buy"), _buyStrategyFiles, baseDir);
            LoadStrategyFiles(Path.Combine(strategyRoot, "sell"), _sellStrategyFiles, baseDir);
        }

        private static void LoadStrategyFiles(string folderPath, ObservableCollection<string> target, string baseDir)
        {
            if (!Directory.Exists(folderPath))
            {
                return;
            }

            var files = Directory.GetFiles(folderPath, "*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName);

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    target.Add(fileName);
                }
            }
        }
    }
}
