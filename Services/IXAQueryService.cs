using XA_DATASETLib;

namespace DumbTrader.Services
{
    public interface IXAQueryService
    {
        // Properties
        bool IsNext { get; }
        string ResFileName { get; set; }
        string ContinueKey { get; }

        // Methods
        string  GetFieldData(string szBlockName, string szFieldName, int nRecordIndex);
        int     Request(bool bNext);
        bool    LoadFromResFile(string szFileName);

        string  GetTrCode();
        string  GetTrDesc();

        void    GetBlockInfo(string szFieldName, ref string szNameK, ref string szNameE, ref int nRecordType);

        void    SetFieldData(string szBlockName, string szFieldName, int nOccursIndex, string szData);
        void    GetFieldInfo(string szFieldName, string szItemName, ref int nItemType, ref int nDataSize, ref int nDotPoint, ref int nOffSet);

        int     GetBlockType(string szBlockName);
        string  GetResData();
        int     GetBlockSize(string szBlockName);

        string  GetFieldDescList(string szBlockName);

        int     GetBlockCount(string szBlockName);
        void    SetBlockCount(string szBlockName, int nCount);

        void    ClearBlockdata(string szFieldName);

        int     GetLastError();
        string  GetErrorMessage(int nErrorCode);

        string  GetAccountList(int nIndex);
        int     GetAccountListCount();

        string  GetBlockData(string szBlockName);

        int     RequestService(string szCode, string szData);
        int     RemoveService(string szCode, string szData);

        bool    RequestLinkToHTS(string szLinkName, string szData, string szFiller);
        int     Decompress(string szBlockName);
        int     GetTRCountPerSec(string szCode);

        string  GetAccountName(string szAcc);
        string  GetAcctDetailName(string szAcc);
        string  GetAcctNickname(string szAcc);

        string  GetFieldChartRealData(string szBlockName, string szFieldName);
        string  GetAttribute(string szBlockName, string szFieldName, string szAttribute, int nRecordIndex);
        int     GetTRCountBaseSec(string szCode);
        int     GetTRCountRequest(string szCode);
        int     GetTRCountLimit(string szCode);

        string  GetFieldSearchRealData(string szBlockName, string szFieldName);
        void    SetProgramOrder(bool bProgramOrder);
        bool    GetProgramOrder();

        // Event Handlers
        void    AddReceiveDataEventHandler(_IXAQueryEvents_ReceiveDataEventHandler handler);
        void    RemoveReceiveDataEventHandler(_IXAQueryEvents_ReceiveDataEventHandler handler);

        void    AddReceiveMessageEventHandler(_IXAQueryEvents_ReceiveMessageEventHandler handler);
        void    RemoveReceiveMessageEventHandler(_IXAQueryEvents_ReceiveMessageEventHandler handler);

        void    AddReceiveChartRealDataEventHandler(_IXAQueryEvents_ReceiveChartRealDataEventHandler handler);
        void    RemoveReceiveChartRealDataEventHandler(_IXAQueryEvents_ReceiveChartRealDataEventHandler handler);

        void    AddReceiveSearchRealDataEventHandler(_IXAQueryEvents_ReceiveSearchRealDataEventHandler handler);
        void    RemoveReceiveSearchRealDataEventHandler(_IXAQueryEvents_ReceiveSearchRealDataEventHandler handler);
    }
}
