// This file/class was renamed from AccountStoreService to AccountService
using System;
using System.Collections.Generic;
using System.Linq;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services
{
    public class AccountService
    {
        private readonly DumbTraderDbContext _dbContext;

        public AccountService(DumbTraderDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public void AddAccount(AccountInfo account)
        {
            _dbContext.Accounts.Add(account);
            _dbContext.SaveChanges();
        }

        public List<AccountInfo> GetAccounts()
        {
            return _dbContext.Accounts.AsNoTracking().ToList();
        }

        public void RemoveAccount(int id)
        {
            var acc = _dbContext.Accounts.Find(id);
            if (acc != null)
            {
                _dbContext.Accounts.Remove(acc);
                _dbContext.SaveChanges();
            }
        }
    }
}
