namespace DumbTrader.Services
{
    public interface IXingSessionService
    {
        bool Connect(string serverName, int port);
        void Disconnect();
        bool Login(string id, string password, string certPassword, int type, bool showCertError);
        string GetErrorMessage(int errorCode);
        int GetLastError();
        bool IsConnected { get; }
        bool Logout();
    }
}
