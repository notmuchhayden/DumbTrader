// This file/class was renamed from AccountStoreService to AccountService
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services
{
    public class AccountService : INotifyPropertyChanged
    {
        private readonly DumbTraderDbContext _dbContext;
        private readonly LoggingService _loggingService;

        private readonly Dictionary<string, IXAQueryService> _xaQueryServices;

        // 설정 파일 경로
        private readonly string _accountPath = "account.json";

        private AccountInfo? _currentAccount; // 현재 선택된 계좌 정보를 저장하는 필드
        public AccountInfo? CurrentAccount
        { // CurrentAccount 가 변경되면 자동으로 구독자에게 알림
            get => _currentAccount;
            set
            {
                if (_currentAccount != value)
                {
                    _currentAccount = value;
                    OnPropertyChanged(nameof(CurrentAccount));
                    SaveConfig();
                }
            }
        }

        public AccountService(DumbTraderDbContext dbContext, LoggingService loggingService)
        {
            _dbContext = dbContext;
            _loggingService = loggingService;

            _xaQueryServices = new Dictionary<string, IXAQueryService>();

            // 주식 계좌 기간별 수익률 상세 조회 TR 등록
            var FOCCQ33600 = new XAQueryService();
            FOCCQ33600.LoadFromResFile("Res\\FOCCQ33600.res");
            FOCCQ33600.AddReceiveDataEventHandler(FOCCQ33600ReceiveData);
            _xaQueryServices.Add("FOCCQ33600", FOCCQ33600);

            // 애플리케이션 시작 시 설정 파일에서 계좌 정보 로드
            LoadConfig();
        }

        //tcode로 IXAQueryService 가져오기
        public IXAQueryService? GetQueryService(string tcode)
        {
            _xaQueryServices.TryGetValue(tcode, out var service);
            return service;
        }

        public void SaveConfig()
        {
            try
            {
                if (CurrentAccount != null)
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string jsonString = JsonSerializer.Serialize(CurrentAccount, options);
                    File.WriteAllText(_accountPath, jsonString);
                }
            }
            catch
            {
                // 저장 중 오류 발생 시 예외 처리 (필요시 로깅)
                _loggingService.Log("계정 정보 저장중 오류 발생.");
            }
        }

        public void LoadConfig()
        {
            try
            {
                if (File.Exists(_accountPath))
                {
                    string jsonString = File.ReadAllText(_accountPath);
                    CurrentAccount = JsonSerializer.Deserialize<AccountInfo>(jsonString);
                }
            }
            catch
            {
                // 로드 중 오류 발생 시 예외 처리 (필요시 로깅)
                _loggingService.Log("계정 정보 로드중 오류 발생.");
            }
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

        // 수익률 상세 조회 요청 메서드
        public bool RequestReturnDetails(string accountNumber, string fromDate, string toDate)
        {
            // TODO : 수익률 상세 요청
            var queryService = GetQueryService("FOCCQ33600");
            if (queryService == null)
            {
                _loggingService.Log("FOCCQ33600 TR 서비스가 등록되어 있지 않습니다.");
                return false;
            }
            // FOCCQ33600 TR의 입력값 설정
            queryService.SetFieldData("FOCCQ33600InBlock1", "accno", 0, accountNumber);
            queryService.SetFieldData("FOCCQ33600InBlock1", "fromdt", 0, fromDate);
            queryService.SetFieldData("FOCCQ33600InBlock1", "todt", 0, toDate);
            // TR 요청
            return queryService.Request();
        }

        private void FOCCQ33600ReceiveData(string trcode)
        {
            // TODO: FOCCQ33600 TR의 수신 데이터를 처리하는 로직 구현
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
