namespace DumbTrader.Services
{
    public interface IXingSessionService
    {
        bool Connect(string serverName, int port);
        void Disconnect();
        bool IsConnected { get; }

        bool Login(string id, string password, string certPassword, int type, bool showCertError);
        bool Logout();

        int GetAccountListCount();
        string GetAccountList(int index);
        string GetAccountName(string accountNumber);

        string GetErrorMessage(int errorCode);
        int GetLastError();
    }
}
