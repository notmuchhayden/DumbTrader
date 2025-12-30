using System.Diagnostics;
using XA_SESSIONLib;

namespace DumbTrader.Services
{
    public class XASessionService : IXASessionService
    {
        private XASession? _session;

        public XASessionService()
        {
            try
            {
                _session = new XASession();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create XASession: {ex.Message}");
            }
        }

        public bool Connect(string serverName, int port)
        {
            if (_session == null) return false;

            try
            {
                _session.DisconnectServer();
            }
            catch { /* Ignore */ }

            return _session.ConnectServer(serverName, port);
        }

        public void Disconnect()
        {
            if (_session == null) return;
            try
            {
                _session.DisconnectServer();
            }
            catch { /* Ignore */ }
        }

        public bool IsConnected
        {
            get
            {
                return _session != null && _session.IsConnected();
            }
        }

        public bool Login(string id, string password, string certPassword, int type, bool showCertError)
        {
            if (_session == null) return false;
            return _session.Login(id, password, certPassword, type, showCertError);
        }

        public bool Logout()
        {
            if (_session == null) return false;
            try
            {
                return _session.Logout();
            }
            catch
            {
                return false;
            }
        }

        public int GetAccountListCount()
        {
            if (_session == null) return 0;
            return _session.GetAccountListCount();
        }

        public string GetAccountList(int index)
        {
            if (_session == null) return string.Empty;
            return _session.GetAccountList(index);
        }

        public string GetAccountName(string accountNumber)
        {
            if (_session == null) return string.Empty;
            return _session.GetAccountName(accountNumber);
        }

        public string GetAcctDetailName(string accountNumber)
        {
            if (_session == null) return string.Empty;
            return _session.GetAcctDetailName(accountNumber);
        }

        public string GetAcctNickname(string accountNumber)
        {
            if (_session == null) return string.Empty;
            return _session.GetAcctNickname(accountNumber);
        }

        public string GetErrorMessage(int errorCode)
        {
            if (_session == null) return "Session not initialized";
            return _session.GetErrorMessage(errorCode);
        }

        public int GetLastError()
        {
            if (_session == null) return -1;
            return _session.GetLastError();
        }

        public void AddLoginEventHandler(_IXASessionEvents_LoginEventHandler handler)
        {
            _IXASessionEvents_Event? xaSessionEvent = _session;
            if (xaSessionEvent != null)
            {
                xaSessionEvent.Login += handler;
            }
        }

        public void RemoveLoginEventHandler(_IXASessionEvents_LoginEventHandler handler)
        {
            _IXASessionEvents_Event? xaSessionEvent = _session;
            if (xaSessionEvent != null)
            {
                xaSessionEvent.Login -= handler;
            }
        }

        public void AddLogoutEventHandler(_IXASessionEvents_LogoutEventHandler handler)
        {
            _IXASessionEvents_Event? xaSessionEvent = _session;
            if (xaSessionEvent != null)
            {
                xaSessionEvent.Logout += handler;
            }
        }

        public void RemoveLogoutEventHandler(_IXASessionEvents_LogoutEventHandler handler)
        {
            _IXASessionEvents_Event? xaSessionEvent = _session;
            if (xaSessionEvent != null)
            {
                xaSessionEvent.Logout -= handler;
            }
        }

        public void AddDisconnectEventHandler(_IXASessionEvents_DisconnectEventHandler handler)
        {
            _IXASessionEvents_Event? xaSessionEvent = _session;
            if (xaSessionEvent != null)
            {
                xaSessionEvent.Disconnect += handler;
            }
        }

        public void RemoveDisconnectEventHandler(_IXASessionEvents_DisconnectEventHandler handler)
        {
            _IXASessionEvents_Event? xaSessionEvent = _session;
            if (xaSessionEvent != null)
            {
                xaSessionEvent.Disconnect -= handler;
            }
        }
    }
}
