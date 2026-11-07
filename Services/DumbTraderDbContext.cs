using Microsoft.EntityFrameworkCore;
using DumbTrader.Models;

namespace DumbTrader.Services
{
    public class DumbTraderDbContext : DbContext
    {
        // 계좌정보목록
        public DbSet<AccountInfo> Accounts { get; set; } // TODO : 계좌정보는 db 에 저장하지 않고 매번 API 호출로 가져오도록 변경 고려
        // 종목정보목록
        public DbSet<StockInfo> Stocks { get; set; }
        // 주식차트데이터목록
        public DbSet<StockChartData> StockChartDatas { get; set; }
        // 실시간 체결 데이터
        public DbSet<RealS3_K3_Data> RealS3K3Data { get; set; }
        // 해외선물 관심계약 목록
        public DbSet<OFutureContract> OFutureContracts { get; set; }
        public DbSet<OFutureMinuteBar> OFutureMinuteBars { get; set; }
        public DbSet<OFutureDailyBar> OFutureDailyBars { get; set; }
        public DbSet<OFutureLatestTradeSnapshot> OFutureLatestTradeSnapshots { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite("Data Source=dumbtrader.db");

        // 복합키 설정
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<StockChartData>()
                .HasKey(e => new { e.shcode, e.date });

            modelBuilder.Entity<RealS3_K3_Data>()
                .HasKey(e => new { e.shcode, e.chetime });

            modelBuilder.Entity<OFutureContract>()
                .HasKey(e => e.Symbol);

            modelBuilder.Entity<OFutureMinuteBar>()
                .HasKey(e => new { e.Symbol, e.IntervalMinutes, e.TradeDate, e.TradeTime });

            modelBuilder.Entity<OFutureDailyBar>()
                .HasKey(e => new { e.Symbol, e.TradeDate });

            modelBuilder.Entity<OFutureLatestTradeSnapshot>()
                .HasKey(e => e.Symbol);
        }

        /// <summary>기존 EnsureCreated DB에도 해외선물 관심계약 테이블을 보충한다.</summary>
        public void EnsureCreatedAndExtended()
        {
            Database.EnsureCreated();
            Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "OFutureContracts" (
                    "Symbol" TEXT NOT NULL CONSTRAINT "PK_OFutureContracts" PRIMARY KEY,
                    "Name" TEXT NOT NULL,
                    "BaseProductCode" TEXT NOT NULL,
                    "BaseProductName" TEXT NOT NULL,
                    "ExchangeCode" TEXT NOT NULL,
                    "ExchangeName" TEXT NOT NULL,
                    "CurrencyCode" TEXT NOT NULL,
                    "ProductCode" TEXT NOT NULL,
                    "MaturityYear" TEXT NOT NULL,
                    "MaturityMonth" TEXT NOT NULL,
                    "ApplicationDate" TEXT NOT NULL,
                    "TickSize" REAL NOT NULL,
                    "TickValue" REAL NOT NULL,
                    "ContractValue" REAL NOT NULL,
                    "OpeningMargin" REAL NOT NULL,
                    "MaintenanceMargin" REAL NOT NULL
                );
                """);
            Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "OFutureMinuteBars" (
                    "Symbol" TEXT NOT NULL,
                    "IntervalMinutes" INTEGER NOT NULL,
                    "TradeDate" TEXT NOT NULL,
                    "TradeTime" TEXT NOT NULL,
                    "Open" REAL NOT NULL,
                    "High" REAL NOT NULL,
                    "Low" REAL NOT NULL,
                    "Close" REAL NOT NULL,
                    "Volume" INTEGER NOT NULL,
                    "ReceivedAtUtc" TEXT NOT NULL,
                    CONSTRAINT "PK_OFutureMinuteBars" PRIMARY KEY ("Symbol", "IntervalMinutes", "TradeDate", "TradeTime")
                );
                CREATE INDEX IF NOT EXISTS "IX_OFutureMinuteBars_Symbol_IntervalMinutes_TradeDate"
                    ON "OFutureMinuteBars" ("Symbol", "IntervalMinutes", "TradeDate");
                CREATE TABLE IF NOT EXISTS "OFutureDailyBars" (
                    "Symbol" TEXT NOT NULL,
                    "TradeDate" TEXT NOT NULL,
                    "SourceTrCode" TEXT NOT NULL,
                    "QueryType" TEXT NOT NULL,
                    "Open" REAL NOT NULL,
                    "High" REAL NOT NULL,
                    "Low" REAL NOT NULL,
                    "Close" REAL NOT NULL,
                    "SourcePrice" REAL NOT NULL,
                    "Volume" INTEGER NOT NULL,
                    "Change" REAL NOT NULL,
                    "ChangeRate" REAL NOT NULL,
                    "Sign" TEXT NOT NULL,
                    "ReceivedAtUtc" TEXT NOT NULL,
                    CONSTRAINT "PK_OFutureDailyBars" PRIMARY KEY ("Symbol", "TradeDate")
                );
                CREATE TABLE IF NOT EXISTS "OFutureLatestTradeSnapshots" (
                    "Symbol" TEXT NOT NULL CONSTRAINT "PK_OFutureLatestTradeSnapshots" PRIMARY KEY,
                    "OverseasDate" TEXT NOT NULL,
                    "KoreaDate" TEXT NOT NULL,
                    "TradeTime" TEXT NOT NULL,
                    "KoreaTradeTime" TEXT NOT NULL,
                    "Price" REAL NOT NULL,
                    "Change" REAL NOT NULL,
                    "ChangeSign" TEXT NOT NULL,
                    "ChangeRate" REAL NOT NULL,
                    "Open" REAL NOT NULL,
                    "High" REAL NOT NULL,
                    "Low" REAL NOT NULL,
                    "TradeQuantity" INTEGER NOT NULL,
                    "TotalQuantity" INTEGER NOT NULL,
                    "TradeDirection" TEXT NOT NULL,
                    "SellTradeQuantity" INTEGER NOT NULL,
                    "BuyTradeQuantity" INTEGER NOT NULL,
                    "ReceivedAtUtc" TEXT NOT NULL,
                    "StoredAtUtc" TEXT NOT NULL
                );
                """);
        }
    }

    // NOTE : App.xaml.cs 에서 *.db 파일을 자동 생성하도록 설정함
}
