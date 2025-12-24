using XA_DATASETLib;

namespace DumbTrader.Services
{
    public class XARealService : IXARealService
    {
        private IXAReal? _xaReal;

        public XARealService()
        {
            try 
            {
                _xaReal = new XAReal();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create XAReal: {ex.Message}");
                throw;
            }
        }

        // Property
        public string ResFileName
        {
            get => _xaReal?.ResFileName ?? string.Empty;
            set { if (_xaReal != null) _xaReal.ResFileName = value; }
        }

        // Methods
        public string GetTrCode() => _xaReal?.GetTrCode() ?? string.Empty;
        public bool LoadFromResFile(string szFileName) => _xaReal?.LoadFromResFile(szFileName) ?? false;
        public void SetFieldData(string szBlockName, string szFieldName, string szData)
        {
            if (_xaReal != null) _xaReal.SetFieldData(szBlockName, szFieldName, szData);
        }
        public string GetFieldData(string szBlockName, string szFieldName) => _xaReal?.GetFieldData(szBlockName, szFieldName) ?? string.Empty;
        public void AdviseRealData()
        {
            if (_xaReal != null) _xaReal.AdviseRealData();
        }
        public void UnadviseRealData()
        {
            if (_xaReal != null) _xaReal.UnadviseRealData();
        }
        public void UnadviseRealDataWithKey(string szCode)
        {
            if (_xaReal != null) _xaReal.UnadviseRealDataWithKey(szCode);
        }
        public void AdviseLinkFromHTS()
        {
            if (_xaReal != null) _xaReal.AdviseLinkFromHTS();
        }
        public void UnAdviseLinkFromHTS()
        {
            if (_xaReal != null) _xaReal.UnAdviseLinkFromHTS();
        }
        public string GetBlockData(string szBlockName) => _xaReal?.GetBlockData(szBlockName) ?? string.Empty;
    }
}
