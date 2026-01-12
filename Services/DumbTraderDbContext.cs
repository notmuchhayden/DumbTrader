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

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite("Data Source=dumbtrader.db");

        // 복합키 설정. StockChartData 의 shcode + date 를 복합키로 설정
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<StockChartData>()
                .HasKey(e => new { e.shcode, e.date });
        }
    }

    // NOTE : App.xaml.cs 에서 *.db 파일을 자동 생성하도록 설정함
}
