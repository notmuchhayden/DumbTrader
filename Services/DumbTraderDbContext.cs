using Microsoft.EntityFrameworkCore;
using DumbTrader.Models;

namespace DumbTrader.Services
{
    public class DumbTraderDbContext : DbContext
    {
        public DbSet<AccountInfo> Accounts { get; set; }
        public DbSet<StockInfo> Stocks { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite("Data Source=dumbtrader.db");
    }

    // NOTE : App.xaml.cs 에서 *.db 파일을 자동 생성하도록 설정함
}
