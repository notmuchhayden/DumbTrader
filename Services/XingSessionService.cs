using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using XA_SESSIONLib;

namespace DumbTrader.Services
{
    public class XingSessionService : IXingSessionService
    {
        private XASession? _session;

        public XingSessionService()
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

        public bool IsConnected
        {
            get
            {
                 // XASession doesn't have a direct IsConnected property, 
                 // managing state manually or relying on return values.
                 // For now, assuming if ConnectServer returned true safely.
                 return _session != null && _session.IsConnected();
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

        public bool Login(string id, string password, string certPassword, int type, bool showCertError)
        {
            if (_session == null) return false;
            return _session.Login(id, password, certPassword, type, showCertError);
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

        public bool Logout()
        {
            if (_session == null) return false;
            try
            {
                return _session.Logout();
            }
            catch {
                return false;
            }
        }
    }
}
