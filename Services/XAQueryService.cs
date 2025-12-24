using XA_DATASETLib;

namespace DumbTrader.Services
{
    public class XAQueryService : IXAQueryService
    {
        private IXAQuery? _xaQuery;

        public XAQueryService()
        {
            try 
            {
                _xaQuery = new XAQuery();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create XAQuery: {ex.Message}");
                throw;
            }
        }

        // Properties
        public bool IsNext => _xaQuery != null && _xaQuery.IsNext;
        public string ResFileName { get => _xaQuery?.ResFileName ?? string.Empty; set { if (_xaQuery != null) _xaQuery.ResFileName = value; } }
        public string ContinueKey => _xaQuery?.ContinueKey ?? string.Empty;

        // Methods
        public string GetFieldData(string szBlockName, string szFieldName, int nRecordIndex) => _xaQuery?.GetFieldData(szBlockName, szFieldName, nRecordIndex) ?? string.Empty;
        public int Request(bool bNext) => _xaQuery?.Request(bNext) ?? -1;
        public bool LoadFromResFile(string szFileName) => _xaQuery?.LoadFromResFile(szFileName) ?? false;
        public string GetTrCode() => _xaQuery?.GetTrCode() ?? string.Empty;
        public string GetTrDesc() => _xaQuery?.GetTrDesc() ?? string.Empty;
        public void GetBlockInfo(string szFieldName, ref string szNameK, ref string szNameE, ref int nRecordType)
        {
            if (_xaQuery != null) _xaQuery.GetBlockInfo(szFieldName, ref szNameK, ref szNameE, ref nRecordType);
        }
        public void SetFieldData(string szBlockName, string szFieldName, int nOccursIndex, string szData)
        {
            if (_xaQuery != null) _xaQuery.SetFieldData(szBlockName, szFieldName, nOccursIndex, szData);
        }
        public void GetFieldInfo(string szFieldName, string szItemName, ref int nItemType, ref int nDataSize, ref int nDotPoint, ref int nOffSet)
        {
            if (_xaQuery != null) _xaQuery.GetFieldInfo(szFieldName, szItemName, ref nItemType, ref nDataSize, ref nDotPoint, ref nOffSet);
        }
        public int GetBlockType(string szBlockName) => _xaQuery?.GetBlockType(szBlockName) ?? -1;
        public string GetResData() => _xaQuery?.GetResData() ?? string.Empty;
        public int GetBlockSize(string szBlockName) => _xaQuery?.GetBlockSize(szBlockName) ?? -1;
        public string GetFieldDescList(string szBlockName) => _xaQuery?.GetFieldDescList(szBlockName) ?? string.Empty;
        public int GetBlockCount(string szBlockName) => _xaQuery?.GetBlockCount(szBlockName) ?? -1;
        public void SetBlockCount(string szBlockName, int nCount)
        {
            if (_xaQuery != null) _xaQuery.SetBlockCount(szBlockName, nCount);
        }
        public void ClearBlockdata(string szFieldName)
        {
            if (_xaQuery != null) _xaQuery.ClearBlockdata(szFieldName);
        }
        public int GetLastError() => _xaQuery?.GetLastError() ?? -1;
        public string GetErrorMessage(int nErrorCode) => _xaQuery?.GetErrorMessage(nErrorCode) ?? "Query not initialized";
        public string GetAccountList(int nIndex) => _xaQuery?.GetAccountList(nIndex) ?? string.Empty;
        public int GetAccountListCount() => _xaQuery?.GetAccountListCount() ??0;
        public string GetBlockData(string szBlockName) => _xaQuery?.GetBlockData(szBlockName) ?? string.Empty;
        public int RequestService(string szCode, string szData) => _xaQuery?.RequestService(szCode, szData) ?? -1;
        public int RemoveService(string szCode, string szData) => _xaQuery?.RemoveService(szCode, szData) ?? -1;
        public bool RequestLinkToHTS(string szLinkName, string szData, string szFiller) => _xaQuery?.RequestLinkToHTS(szLinkName, szData, szFiller) ?? false;
        public int Decompress(string szBlockName) => _xaQuery?.Decompress(szBlockName) ?? -1;
        public int GetTRCountPerSec(string szCode) => _xaQuery?.GetTRCountPerSec(szCode) ?? -1;
        public string GetAccountName(string szAcc) => _xaQuery?.GetAccountName(szAcc) ?? string.Empty;
        public string GetAcctDetailName(string szAcc) => _xaQuery?.GetAcctDetailName(szAcc) ?? string.Empty;
        public string GetAcctNickname(string szAcc) => _xaQuery?.GetAcctNickname(szAcc) ?? string.Empty;
        public string GetFieldChartRealData(string szBlockName, string szFieldName) => _xaQuery?.GetFieldChartRealData(szBlockName, szFieldName) ?? string.Empty;
        public string GetAttribute(string szBlockName, string szFieldName, string szAttribute, int nRecordIndex) => _xaQuery?.GetAttribute(szBlockName, szFieldName, szAttribute, nRecordIndex) ?? string.Empty;
        public int GetTRCountBaseSec(string szCode) => _xaQuery?.GetTRCountBaseSec(szCode) ?? -1;
        public int GetTRCountRequest(string szCode) => _xaQuery?.GetTRCountRequest(szCode) ?? -1;
        public int GetTRCountLimit(string szCode) => _xaQuery?.GetTRCountLimit(szCode) ?? -1;
        public string GetFieldSearchRealData(string szBlockName, string szFieldName) => _xaQuery?.GetFieldSearchRealData(szBlockName, szFieldName) ?? string.Empty;
        public void SetProgramOrder(bool bProgramOrder)
        {
            if (_xaQuery != null) _xaQuery.SetProgramOrder(bProgramOrder);
        }
        public bool GetProgramOrder() => _xaQuery?.GetProgramOrder() ?? false;
    }
}
