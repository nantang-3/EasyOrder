using System.Windows;
using EasyOrder.Services;

namespace EasyOrder;

public partial class App : Application
{
    /// <summary>내장 로컬 웹서버 (포트 6703)</summary>
    public static KioskWebServer WebServer { get; } = new();

    /// <summary>Cloudflare Quick Tunnel (휴대폰 HTTPS 접속용)</summary>
    public static CloudflareTunnel Tunnel { get; } = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            await WebServer.StartAsync();
            Tunnel.Start(KioskWebServer.LocalUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"내장 웹서버를 시작하지 못했습니다 (포트 {KioskWebServer.Port}).\n{ex.Message}",
                "EasyOrder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tunnel.Dispose();
        WebServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
