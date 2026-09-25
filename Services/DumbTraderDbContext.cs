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
        }
    }

    // NOTE : App.xaml.cs 에서 *.db 파일을 자동 생성하도록 설정함
}
