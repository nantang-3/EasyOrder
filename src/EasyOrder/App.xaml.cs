using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using EasyOrder.Services;

namespace EasyOrder;

public partial class App : Application
{
    private const string Title = "EasyOrder";

    /// <summary>키오스크 앱은 한 번에 하나만 실행 (포트 6703 충돌 방지)</summary>
    private static Mutex? _singleInstance;
    private static bool _ownsSingleInstance;

    /// <summary>내장 로컬 웹서버 (포트 6703)</summary>
    public static KioskWebServer WebServer { get; } = new();

    /// <summary>Cloudflare Quick Tunnel (휴대폰 HTTPS 접속용)</summary>
    public static CloudflareTunnel Tunnel { get; } = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\EasyOrderKiosk", out _ownsSingleInstance);
        if (!_ownsSingleInstance)
        {
            ShowError("EasyOrder가 이미 실행 중입니다.\n실행 중인 키오스크 창을 닫은(Alt+F4) 뒤 다시 실행해 주세요.");
            Shutdown();
            return;
        }

        // 처리되지 않은 오류가 나면 조용히 꺼지지 않고 내용을 알린다
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowError($"예상하지 못한 오류로 종료됩니다.\n{(args.ExceptionObject as Exception)?.Message}");
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            ShowError($"화면을 시작하지 못했습니다.\n{ex.Message}");
            Shutdown();
            return;
        }

        try
        {
            await WebServer.StartAsync();
            Tunnel.Start(KioskWebServer.LocalUrl);
        }
        catch (Exception ex)
        {
            ShowError(
                $"내장 웹서버를 시작하지 못했습니다 (포트 {KioskWebServer.Port}).\n" +
                $"다른 프로그램이 포트 {KioskWebServer.Port}을 사용 중일 수 있습니다. 휴대폰 음성 주문은 동작하지 않고 터치 주문만 가능합니다.\n{ex.Message}");
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError($"오류가 발생했습니다.\n{e.Exception.Message}");
        e.Handled = true;
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, Title, MessageBoxButton.OK, MessageBoxImage.Error);

    protected override void OnExit(ExitEventArgs e)
    {
        Tunnel.Dispose();
        WebServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (_ownsSingleInstance)
        {
            _singleInstance?.ReleaseMutex();
        }

        _singleInstance?.Dispose();

        base.OnExit(e);
    }
}
