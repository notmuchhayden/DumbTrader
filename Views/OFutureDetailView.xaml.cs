using System.Windows.Controls;

namespace DumbTrader.Views
{
    public partial class OFutureDetailView : UserControl
    {
        public OFutureDetailView()
        {
            InitializeComponent();
            Unloaded += (_, _) => (DataContext as System.IDisposable)?.Dispose();
        }
    }
}
