using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using XA_SESSIONLib;
using XA_DATASETLib;

namespace DumbTrader;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private XASession? xa = null;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void BtnTestXing_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            xa = new XASession();

            // VBA 샘플과 동일하게 먼저 Disconnect 시도 (안되어 있으면 무시)
            try
            {
                xa.DisconnectServer();
            }
            catch
            {
                // 무시: DisconnectServer 호출이 실패해도 계속 진행
            }

            // 서버 접속 시도 (VBA 샘플의 호스트/포트 사용)
            bool isConnected;
            try
            {
                //isConnected = xa.ConnectServer("hts.ebestsec.co.kr", 20001);
                isConnected = xa.ConnectServer("demo.ebestsec.co.kr", 20001);
            }
            catch (COMException comEx)
            {
                MessageBox.Show($"COM 예외: {comEx.Message}", "COM 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!isConnected)
            {
                int errCode = 0;
                string errMsg = "알 수 없는 오류";
                try
                {
                    errCode = xa.GetLastError();
                    errMsg = xa.GetErrorMessage(errCode);
                }
                catch
                {
                    // GetLastError / GetErrorMessage 호출 실패 시 무시
                }

                MessageBox.Show($"서버 접속 실패\r\n에러코드: {errCode}\r\n메시지: {errMsg}", "접속 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show("서버 접속 성공", "접속 성공", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            // 테스트 목적으로 연결을 끊고 종료 (원하면 주석 처리해서 연결 유지 가능)
            try
            {
                xa.DisconnectServer();
            }
            catch
            {
                // 무시
            }
        }
        catch (COMException comEx)
        {
            MessageBox.Show($"COM 예외: {comEx.Message}", "COM 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"예외 발생: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (xa is not null)
            {
                try
                {
                    Marshal.ReleaseComObject(xa);
                }
                catch
                {
                    // 해제 실패 시 무시
                }

                xa = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }

    private void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        bool bLogin = xa.Login("seaeast2", "sde34rfg", "", 0, false);
        if (bLogin)
        {
            MessageBox.Show("로그인 성공", "로그인", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            int errCode = xa.GetLastError();
            string errMsg = xa.GetErrorMessage(errCode);
            MessageBox.Show($"로그인 실패\r\n에러코드: {errCode}\r\n메시지: {errMsg}", "로그인 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}


