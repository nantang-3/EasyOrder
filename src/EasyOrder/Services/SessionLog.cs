using System.IO;

namespace EasyOrder.Services;

/// <summary>
/// 디버깅용 세션별 인식 텍스트 기록 (NFR-07).
/// 실행 폴더의 logs/recognized-YYYYMMDD.log에 "시각 [탭] 세션 ID [탭] 인식 텍스트"를 한 줄씩 남긴다.
/// 음성 데이터는 저장하지 않는다 (NFR-08).
/// </summary>
public static class SessionLog
{
    private static readonly object Gate = new();
    private static readonly string LogDir = Path.Combine(AppContext.BaseDirectory, "logs");

    public static string NewSessionId() => Guid.NewGuid().ToString("N")[..8];

    public static void AppendRecognized(string sessionId, string? text)
    {
        try
        {
            var now = DateTime.Now;
            var line = $"{now:yyyy-MM-dd HH:mm:ss}\t{sessionId}\t{(text ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ')}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(Path.Combine(LogDir, $"recognized-{now:yyyyMMdd}.log"), line);
            }
        }
        catch (IOException)
        {
            // 로그 실패는 주문 흐름에 영향을 주지 않는다
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
