using Microsoft.EntityFrameworkCore;
using DumbTrader.Models;

namespace DumbTrader.Services
{
 public class DumbTraderDbContext : DbContext
 {
 public DbSet<AccountInfo> Accounts { get; set; }

 protected override void OnConfiguring(DbContextOptionsBuilder options)
 => options.UseSqlite("Data Source=dumbtrader.db");
 }
}
