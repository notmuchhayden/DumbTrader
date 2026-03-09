using DumbTrader.Views;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;

namespace DumbTrader
{
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;
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
                sp.GetRequiredService<Services.LoggingService>()));
            // 로그인 서비스 생성
            services.AddSingleton<Services.LoginService>();
            // Xing 접속 세션 서비스 생성
            services.AddSingleton<Services.IXASessionService, Services.XASessionService>();
            // 주식 데이터 조회 서비스 생성
            services.AddSingleton(sp => new Services.StockDataService(sp.GetRequiredService<Services.DumbTraderDbContext>()));
            
            // 주식 실시간 데이터 서비스 생성
            services.AddSingleton<Services.StockRealDataService>();
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
            services.AddTransient(sp => new ViewModels.SummaryViewModel(sp.GetRequiredService<Services.AccountService>()));
            // 로그 ViewModel 등록
            services.AddTransient(sp => new ViewModels.LogViewModel(
                sp.GetRequiredService<Services.LoggingService>()
            ));
            // 대시보드 ViewModel 등록
            services.AddTransient(sp => new ViewModels.DashboardViewModel(
                sp.GetRequiredService<Services.StrategyService>(),
                sp.GetRequiredService<Services.DumbTraderDbContext>(),
                sp.GetRequiredService<Services.StockRealDataService>(),
                sp.GetRequiredService<Services.LoggingService>()
            ));
            // 계정관리 ViewModel 등록
            services.AddTransient(sp => new ViewModels.AccountViewModel(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.AccountService>()));
            // 관심종목관리 ViewModel 등록
            services.AddTransient(sp => new ViewModels.WatchlistViewModel( 
                sp.GetRequiredService<Services.StockDataService>(),
                sp.GetRequiredService<Services.StrategyService>()));
            // 개별종목관리 ViewModel 등록
            services.AddTransient(sp => new ViewModels.StockDetailViewModel(
                sp.GetRequiredService<Services.StockDataService>(),
                sp.GetRequiredService<Services.StrategyService>(),
                sp.GetRequiredService<Services.LoggingService>(),
                sp.GetRequiredService<Services.DumbTraderDbContext>()
            ));
            // Splash ViewModel 등록
            services.AddTransient(sp => new ViewModels.SplashViewModel());

            _serviceProvider = services.BuildServiceProvider();
            ServiceProvider = _serviceProvider;

            // 데이터베이스와 테이블 자동 생성
            _serviceProvider.GetRequiredService<Services.DumbTraderDbContext>().Database.EnsureCreated();

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
    }
}
