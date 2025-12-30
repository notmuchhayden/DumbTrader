using XA_DATASETLib;

namespace DumbTrader.Services
{
    public interface IXARealService
    {
        // Properties
        string ResFileName { get; set; }

        // Methods
        string GetTrCode();
        bool LoadFromResFile(string szFileName);
        void SetFieldData(string szBlockName, string szFieldName, string szData);
        string GetFieldData(string szBlockName, string szFieldName);
        void AdviseRealData();
        void UnadviseRealData();
        void UnadviseRealDataWithKey(string szCode);
        void AdviseLinkFromHTS();
        void UnAdviseLinkFromHTS();
        string GetBlockData(string szBlockName);

        // Event Handlers
        void AddReceiveRealDataEventHandler(_IXARealEvents_ReceiveRealDataEventHandler handler);
        void RemoveReceiveRealDataEventHandler(_IXARealEvents_ReceiveRealDataEventHandler handler);

        void AddRecieveLinkDataEventHandler(_IXARealEvents_RecieveLinkDataEventHandler handler);
        void RemoveRecieveLinkDataEventHandler(_IXARealEvents_RecieveLinkDataEventHandler handler);
    }
}
