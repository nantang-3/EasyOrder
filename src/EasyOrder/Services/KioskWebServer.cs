using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using EasyOrder.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EasyOrder.Services;

/// <summary>
/// WPF 앱 내장 로컬 웹서버 (IR-08).
/// http://localhost:6703 에서 휴대폰 페이지(wwwroot)를 제공하고,
/// /ws WebSocket으로 재생 명령(PC→휴대폰)과 휴대폰 메시지(휴대폰→PC)를 주고받는다.
/// 휴대폰은 1대만 연결하며, 새 연결이 들어오면 이전 연결을 닫는다.
/// </summary>
public sealed class KioskWebServer : IAsyncDisposable
{
    public const int Port = 6703;
    public static string LocalUrl => $"http://localhost:{Port}";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private WebApplication? _app;
    private WebSocket? _phone;

    /// <summary>휴대폰 메시지 수신 (백그라운드 스레드에서 발생)</summary>
    public event Action<PhoneMessage>? MessageReceived;

    /// <summary>휴대폰 연결/해제 (백그라운드 스레드에서 발생)</summary>
    public event Action<bool>? PhoneConnectionChanged;

    public bool IsPhoneConnected => _phone?.State == WebSocketState.Open;

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

        var app = builder.Build();
        app.UseWebSockets();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.Map("/ws", (RequestDelegate)HandleWebSocketAsync);

        await app.StartAsync().ConfigureAwait(false);
        _app = app;
    }

    /// <summary>휴대폰에 WAV 재생 명령을 보낸다. 연결이 없으면 false</summary>
    public Task<bool> PlayAsync(string audioFile) => SendAsync(new { type = "play", file = audioFile });

    public async Task<bool> SendAsync(object message)
    {
        var ws = _phone;
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
        catch (WebSocketException)
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
        var previous = Interlocked.Exchange(ref _phone, ws);
        if (previous is not null)
        {
            try
            {
                await previous.CloseAsync(WebSocketCloseStatus.PolicyViolation, "replaced", CancellationToken.None).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
            }
        }

        PhoneConnectionChanged?.Invoke(true);

        var buffer = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                using var payload = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted).ConfigureAwait(false);
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

                if (message is not null && !string.IsNullOrEmpty(message.Type))
                {
                    MessageReceived?.Invoke(message);
                }
            }
        }
        catch (WebSocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (Interlocked.CompareExchange(ref _phone, null, ws) == ws)
            {
                PhoneConnectionChanged?.Invoke(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
        }

        _sendLock.Dispose();
    }
}
