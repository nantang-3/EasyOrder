using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using EasyOrder.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyOrder.Services;

/// <summary>
/// WPF 앱 내장 로컬 웹서버 (IR-08).
/// http://localhost:6703 에서 휴대폰 페이지(wwwroot)를 제공하고,
/// /ws WebSocket으로 재생 명령(PC→휴대폰)과 휴대폰 메시지(휴대폰→PC)를 주고받는다.
/// 여러 페이지가 동시에 연결될 수 있으며, 재생 명령은 마지막으로 "음성주문활성화"를 보낸 연결에만 보낸다.
/// </summary>
public sealed class KioskWebServer : IAsyncDisposable
{
    public const int Port = 6703;
    public static string LocalUrl => $"http://localhost:{Port}";

    private const string VoiceActivatedType = "voice-activated";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private WebApplication? _app;
    private WebSocket? _activeClient;

    /// <summary>앱 종료 시 열린 WebSocket 수신을 즉시 끊기 위한 신호</summary>
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>휴대폰 메시지 수신 (백그라운드 스레드에서 발생)</summary>
    public event Action<PhoneMessage>? MessageReceived;

    /// <summary>음성주문을 활성화한 휴대폰이 연결되어 있는지 여부</summary>
    public bool IsVoicePhoneConnected => Volatile.Read(ref _activeClient)?.State == WebSocketState.Open;

    public async Task StartAsync()
    {
        var baseDir = AppContext.BaseDirectory;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = baseDir,
            WebRootPath = Path.Combine(baseDir, "wwwroot"),
        });
        builder.WebHost.UseUrls(LocalUrl);
        builder.Logging.ClearProviders();

        // 콘솔 앱용 기본 수명 관리(종료 신호 대기)를 쓰지 않는다. 앱 종료는 WPF가 관리한다
        builder.Services.AddSingleton<IHostLifetime, EmbeddedHostLifetime>();

        var app = builder.Build();
        app.UseWebSockets();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.Map("/ws", (RequestDelegate)HandleWebSocketAsync);

        await app.StartAsync().ConfigureAwait(false);
        _app = app;
    }

    /// <summary>음성주문을 활성화한 휴대폰에 WAV 재생 명령을 보낸다. 연결이 없으면 false</summary>
    public Task<bool> PlayAsync(string audioFile) => SendToActiveAsync(new { type = "play", file = audioFile });

    public async Task<bool> SendToActiveAsync(object message)
    {
        var ws = Volatile.Read(ref _activeClient);
        if (ws is null || ws.State != WebSocketState.Open)
        {
            return false;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var ws = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

        using var receiveCancel = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _shutdown.Token);
        var buffer = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                using var payload = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), receiveCancel.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    payload.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                PhoneMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<PhoneMessage>(payload.ToArray(), JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (message is null || string.IsNullOrEmpty(message.Type))
                {
                    continue;
                }

                if (message.Type == VoiceActivatedType)
                {
                    Volatile.Write(ref _activeClient, ws);
                }
                else if (!ReferenceEquals(Volatile.Read(ref _activeClient), ws))
                {
                    // 음성주문을 활성화한 연결이 아닌 곳의 메시지(인식 텍스트 등)는 무시한다
                    continue;
                }

                MessageReceived?.Invoke(message);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            Interlocked.CompareExchange(ref _activeClient, null, ws);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // 휴대폰 연결을 먼저 끊고, 서버 종료는 최대 3초만 기다린다 (연결이 남아 앱이 안 꺼지는 문제 방지)
        _shutdown.Cancel();
        if (_app is not null)
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await _app.StopAsync(stopTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
        }

        _sendLock.Dispose();
    }
}

/// <summary>WPF 앱 안에서 쓰는 웹서버용 수명 관리: 프로세스 종료 이벤트를 붙잡지 않는다</summary>
internal sealed class EmbeddedHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
