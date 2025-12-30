using XA_SESSIONLib;

namespace DumbTrader.Services
{
    public interface IXASessionService
    {
        // Properties
        bool IsConnected { get; }

        // Methods
        bool Connect(string serverName, int port);
        void Disconnect();

        bool Login(string id, string password, string certPassword, int type, bool showCertError);
        bool Logout();

        int GetAccountListCount();
        string GetAccountList(int index);
        string GetAccountName(string accountNumber);
        string GetAcctDetailName(string accountNumber);
        string GetAcctNickname(string accountNumber);

        string GetErrorMessage(int errorCode);
        int GetLastError();

        void AddLoginEventHandler(_IXASessionEvents_LoginEventHandler handler);
        void RemoveLoginEventHandler(_IXASessionEvents_LoginEventHandler handler);

        void AddLogoutEventHandler(_IXASessionEvents_LogoutEventHandler handler);
        void RemoveLogoutEventHandler(_IXASessionEvents_LogoutEventHandler handler);

        void AddDisconnectEventHandler(_IXASessionEvents_DisconnectEventHandler handler);
        void RemoveDisconnectEventHandler(_IXASessionEvents_DisconnectEventHandler handler);
    }
}
