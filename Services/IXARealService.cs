using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
    }
}
