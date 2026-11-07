using DumbTrader.Views;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;

namespace DumbTrader
{
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;
        private ViewModels.OFutureDashboardViewModel? _dashboardViewModel;
        public static IServiceProvider? ServiceProvider { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure strategy folders exist: strategy/main, strategy/sell, strategy/buy
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
                var strategyRoot = Path.Combine(baseDir, "strategy");
                Directory.CreateDirectory(Path.Combine(strategyRoot, "main"));
                Directory.CreateDirectory(Path.Combine(strategyRoot, "sell"));
                Directory.CreateDirectory(Path.Combine(strategyRoot, "buy"));
                Directory.CreateDirectory(Path.Combine(strategyRoot, "ofuture", "main"));
            }
            catch (Exception ex)
            {
                // Fail-safe: if we cannot prepare required folders, stop the application
                MessageBox.Show($"초기화 중 오류가 발생했습니다: {ex.Message}", "초기화 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // Build DI container
            var services = new ServiceCollection();
            

            services.AddDbContextFactory<Services.DumbTraderDbContext>();

            // 서비스 생성 ========================================
            // NOTE : 서비스는 생성 순서의 의존성이 있으므로, 아래 순서를 변경할 때는 의존성 관계를 반드시 확인해야 합니다.
            // DB 생성
            services.AddSingleton<Services.DumbTraderDbContext>();
            // 로그 서비스 생성
            services.AddSingleton<Services.LoggingService>();
            // 계정 서비스 생성
            services.AddSingleton(sp => new Services.AccountService(
                sp.GetRequiredService<Services.DumbTraderDbContext>(),
                sp.GetRequiredService<Services.LoggingService>(),
                sp.GetRequiredService<Services.XingTrQuerySchedulerService>()));
            // 로그인 서비스 생성
            services.AddSingleton<Services.LoginService>();
            // Xing 접속 세션 서비스 생성
            services.AddSingleton<Services.IXASessionService, Services.XASessionService>();
            // 주식 데이터 조회 서비스 생성
            services.AddSingleton(sp => new Services.StockDataService(
                sp.GetRequiredService<Services.DumbTraderDbContext>(),
                sp.GetRequiredService<Services.XingTrQuerySchedulerService>()));
            services.AddSingleton<Services.IStockOrderService>(sp => new Services.StockOrderService(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.LoggingService>(),
                sp.GetRequiredService<Services.XingTrQuerySchedulerService>()));
            

            // 주식 실시간 데이터 서비스 생성
            services.AddSingleton<Services.StockRealDataService>();
            // 해외선물 조회/실시간/주문 서비스 생성
            services.AddSingleton<Services.XingTrQuerySchedulerService>(sp =>
            {
                var scheduler = new Services.XingTrQuerySchedulerService();
                scheduler.RequestRejected += (_, message) =>
                    sp.GetRequiredService<Services.LoggingService>().Log($"공통 TR 스케줄러 요청 실패: {message}");
                scheduler.ServerRateCountersRead += (_, counters) =>
                    sp.GetRequiredService<Services.LoggingService>().Log(
                        $"Xing TR 서버 카운터 {counters.TrCode}: GetTRCountPerSec={counters.PerSec}, " +
                        $"GetTRCountBaseSec={counters.BaseSec}, GetTRCountRequest={counters.Requests}, " +
                        $"GetTRCountLimit={counters.Limit}");
                return scheduler;
            });
            services.AddSingleton<Services.OFutureChartStoreService>();
            services.AddSingleton(sp => new Services.OFutureDataService(
                sp.GetRequiredService<Services.DumbTraderDbContext>(),
                sp.GetRequiredService<Services.XingTrQuerySchedulerService>()));
            services.AddSingleton<Services.OFutureStrategyService>();
            services.AddSingleton<Services.OFutureSimulationService>();
            services.AddSingleton<Services.OFutureRealDataService>();
            services.AddSingleton(sp => new Services.OFutureOrderService(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.LoggingService>(),
                sp.GetRequiredService<Services.XingTrQuerySchedulerService>()));
            // 전략 서비스 생성
            services.AddSingleton(sp => new Services.StrategyService(
                sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Services.DumbTraderDbContext>>(),
                sp.GetRequiredService<Services.LoggingService>(),
                sp.GetRequiredService<Services.StockDataService>()));
            

            // ViewModel 생성 ========================================
            // 로그인 ViewModel 등록
            services.AddTransient(sp => new ViewModels.LoginViewModel(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.LoginService>()));

            // 메인 ViewModel 등록
            services.AddTransient(sp => new ViewModels.MainViewModel(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.AccountService>(), sp));
            // 사이드바 ViewModel 등록
            services.AddTransient<ViewModels.SidebarViewModel>();
            // 요약 ViewModel 등록
            services.AddTransient(sp => new ViewModels.SummaryViewModel(
                sp.GetRequiredService<Services.AccountService>(),
                sp.GetRequiredService<Services.LoggingService>()));
            // 로그 ViewModel 등록
            services.AddTransient(sp => new ViewModels.LogViewModel(
                sp.GetRequiredService<Services.LoggingService>()
            ));
            // 대시보드 ViewModel 등록
            services.AddSingleton(sp => new ViewModels.StockDashboardViewModel(
                sp.GetRequiredService<Services.StrategyService>(),
                sp.GetRequiredService<Services.DumbTraderDbContext>(),
                sp.GetRequiredService<Services.StockRealDataService>(),
                sp.GetRequiredService<Services.LoggingService>()
            ));
            // 해외선물 대시보드 ViewModel 등록
            services.AddSingleton<ViewModels.OFutureDashboardViewModel>(sp => _dashboardViewModel = new ViewModels.OFutureDashboardViewModel(
                sp.GetRequiredService<Services.OFutureDataService>(),
                sp.GetRequiredService<Services.OFutureRealDataService>(),
                sp.GetRequiredService<Services.LoggingService>(),
                sp.GetRequiredService<Services.OFutureChartStoreService>(),
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.OFutureStrategyService>()
            ));
            // 계정관리 ViewModel 등록
            services.AddTransient(sp => new ViewModels.AccountViewModel(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.AccountService>(),
                sp.GetRequiredService<Services.LoggingService>()));
            // 관심종목관리 ViewModel 등록
            services.AddTransient(sp => new ViewModels.StockWatchlistViewModel(
                sp.GetRequiredService<Services.StockDataService>(),
                sp.GetRequiredService<Services.StrategyService>()));
            // 해외선물 관심종목 ViewModel 등록
            services.AddTransient(sp => new ViewModels.OFutureWatchlistViewModel(
                sp.GetRequiredService<Services.OFutureDataService>()));
            // 개별종목관리 ViewModel 등록
            services.AddTransient(sp => new ViewModels.StockDetailViewModel(
                sp.GetRequiredService<Services.StockDataService>(),
                sp.GetRequiredService<Services.StrategyService>(),
                sp.GetRequiredService<Services.LoggingService>(),
                sp.GetRequiredService<Services.DumbTraderDbContext>()
            ));
            // 해외선물 개별 계약 ViewModel 등록
            services.AddTransient(sp => new ViewModels.OFutureDetailViewModel(
                sp.GetRequiredService<Services.OFutureDataService>(),
                sp.GetRequiredService<Services.LoggingService>(),
                sp.GetRequiredService<ViewModels.OFutureStrategyViewModel>()
            ));
            services.AddTransient<ViewModels.OFutureStrategyViewModel>();
            // Splash ViewModel 등록
            services.AddTransient(sp => new ViewModels.SplashViewModel());

            // View 등록 ========================================
            // SummaryView 등록
            services.AddTransient<Views.SummaryView>();

            _serviceProvider = services.BuildServiceProvider();
            ServiceProvider = _serviceProvider;

            // 데이터베이스와 테이블 자동 생성
            _serviceProvider.GetRequiredService<Services.DumbTraderDbContext>().EnsureCreatedAndExtended();

            // Prevent app from shutting down when the login dialog closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Resolve and show login
            var loginView = new LoginView();
            loginView.DataContext = _serviceProvider.GetRequiredService<ViewModels.LoginViewModel>();

            var result = loginView.ShowDialog();
            if (result == true)
            {
                // 로그인 성공 시 스플래시 화면을 먼저 띄움
                var splashView = new SplashView();
                splashView.DataContext = _serviceProvider.GetRequiredService<ViewModels.SplashViewModel>();

                // 스플래시 화면이 끝날 때까지 대기
                var splashResult = splashView.ShowDialog();

                if (splashResult == true)
                {
                    // Ensure we don't create a second MainWindow if one already exists
                    var existing = Current?.Windows.OfType<MainWindow>().FirstOrDefault();
                    if (existing != null)
                    {
                        existing.Activate();
                        existing.Focus();
                        MainWindow = existing;
                        ShutdownMode = ShutdownMode.OnMainWindowClose;
                    }
                    else
                    {
                        var mainWindow = new MainWindow();
                        mainWindow.DataContext = _serviceProvider.GetRequiredService<ViewModels.MainViewModel>();
                        // Designate as main window and resume normal shutdown behavior
                        MainWindow = mainWindow;
                        ShutdownMode = ShutdownMode.OnMainWindowClose;
                        mainWindow.Show();
                    }
                }
                else
                {
                    Shutdown();
                }
            }
            else
            {
                // Login cancelled/failed -> exit
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _dashboardViewModel?.ShutdownAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                try { (_serviceProvider?.GetService(typeof(Services.LoggingService)) as Services.LoggingService)?.Log($"종료 시 해외선물 스냅샷 저장 실패: {ex.Message}"); }
                catch { }
            }
            finally
            {
                _serviceProvider?.Dispose();
                ServiceProvider = null;
            }
            base.OnExit(e);
        }
    }
}
