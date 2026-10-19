using System.IO;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace EasyOrder.Services;

/// <summary>
/// 주문 확인 멘트처럼 내용이 바뀌는 안내를 실행 중에 Azure TTS(SunHi)로 만든다 (FR-17, IR-04).
/// 키·지역은 환경 변수(AZURE_SPEECH_KEY, AZURE_SPEECH_REGION)에서만 읽는다.
/// 만든 WAV는 wwwroot/audio/tts/에 저장해 휴대폰이 내려받아 재생한다.
/// </summary>
public sealed class SpeechSynthesisService
{
    private const string VoiceName = "ko-KR-SunHiNeural";
    private const int KeepFiles = 10;

    private readonly string _outputDir = Path.Combine(AppContext.BaseDirectory, "wwwroot", "audio", "tts");

    public SpeechSynthesisService()
    {
        Directory.CreateDirectory(_outputDir);
        foreach (var file in Directory.GetFiles(_outputDir, "*.wav"))
        {
            TryDelete(file);
        }
    }

    /// <summary>문장을 WAV로 만들고 휴대폰 재생용 경로("tts/파일명.wav")를 돌려준다. 실패하면 null</summary>
    public async Task<string?> SynthesizeAsync(string text)
    {
        var key = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        var region = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
        {
            return null;
        }

        var config = SpeechConfig.FromSubscription(key, region);
        config.SpeechSynthesisVoiceName = VoiceName;
        config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Riff24Khz16BitMonoPcm);

        using var synthesizer = new SpeechSynthesizer(config, (AudioConfig?)null);
        using var result = await synthesizer.SpeakTextAsync(text).ConfigureAwait(false);
        if (result.Reason != ResultReason.SynthesizingAudioCompleted)
        {
            return null;
        }

        var fileName = $"confirm-{DateTime.Now:HHmmssfff}.wav";
        await File.WriteAllBytesAsync(Path.Combine(_outputDir, fileName), result.AudioData).ConfigureAwait(false);
        CleanUpOldFiles();
        return $"tts/{fileName}";
    }

    private void CleanUpOldFiles()
    {
        var old = new DirectoryInfo(_outputDir).GetFiles("*.wav")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Skip(KeepFiles);
        foreach (var file in old)
        {
            TryDelete(file.FullName);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
