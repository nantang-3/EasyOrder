using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace EasyOrder.Services;

/// <summary>
/// Cloudflare Quick Tunnel (FR-13).
/// cloudflared를 자동 실행해 로컬 웹서버(포트 6703)를 HTTPS 주소(trycloudflare.com)로 연결하고,
/// 출력에서 주소를 읽어 PublicUrl로 알린다. 앱 종료 시 cloudflared도 종료한다.
/// </summary>
public sealed partial class CloudflareTunnel : IDisposable
{
    private Process? _process;

    /// <summary>터널 HTTPS 주소. 아직 받지 못했으면 null</summary>
    public string? PublicUrl { get; private set; }

    /// <summary>시작 실패 사유. 정상이면 null</summary>
    public string? Error { get; private set; }

    /// <summary>주소를 받았거나 시작에 실패했을 때 발생 (백그라운드 스레드)</summary>
    public event Action? StatusChanged;

    [GeneratedRegex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase)]
    private static partial Regex TunnelUrlRegex();

    public void Start(string localUrl)
    {
        var exe = FindCloudflared();
        if (exe is null)
        {
            Fail("cloudflared를 찾을 수 없습니다");
            return;
        }

        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("tunnel");
        info.ArgumentList.Add("--no-autoupdate");
        info.ArgumentList.Add("--url");
        info.ArgumentList.Add(localUrl);

        try
        {
            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => ReadLine(e.Data);
            process.ErrorDataReceived += (_, e) => ReadLine(e.Data);
            process.Exited += (_, _) =>
            {
                if (PublicUrl is null)
                {
                    Fail("cloudflared가 종료되었습니다");
                }
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
        }
        catch (Exception ex)
        {
            Fail($"cloudflared 실행 실패: {ex.Message}");
        }
    }

    private void ReadLine(string? line)
    {
        if (PublicUrl is not null || string.IsNullOrEmpty(line))
        {
            return;
        }

        var match = TunnelUrlRegex().Match(line);
        if (match.Success)
        {
            PublicUrl = match.Value;
            StatusChanged?.Invoke();
        }
    }

    private void Fail(string message)
    {
        Error = message;
        StatusChanged?.Invoke();
    }

    /// <summary>PATH와 winget 기본 설치 위치에서 cloudflared.exe를 찾는다.</summary>
    private static string? FindCloudflared()
    {
        var candidates = new List<string>();
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(dir))
            {
                candidates.Add(Path.Combine(dir.Trim(), "cloudflared.exe"));
            }
        }

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "cloudflared", "cloudflared.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "cloudflared", "cloudflared.exe"));

        return candidates.FirstOrDefault(File.Exists);
    }

    public void Dispose()
    {
        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }
}
