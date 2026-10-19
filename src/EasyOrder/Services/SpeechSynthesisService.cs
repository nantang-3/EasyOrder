using System.IO;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace EasyOrder.Services;

/// <summary>
/// 주문 확인 멘트처럼 내용이 바뀌는 안내를 실행 중에 Azure TTS(SunHi)로 만든다 (FR-17, IR-04).
/// 키·지역은 환경 변수(AZURE_SPEECH_KEY, AZURE_SPEECH_REGION)에서만 읽는다.
/// 만든 WAV는 wwwroot/audio/tts/에 저장해 휴대폰이 내려받아 재생한다.
/// Azure로 만들지 못하면(키 없음, 인터넷·서비스 오류) Windows 내장 음성(한국어)으로 대신 만든다.
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

    /// <summary>문장을 WAV로 만들고 휴대폰 재생용 경로("tts/파일명.wav")를 돌려준다. 모두 실패하면 null</summary>
    public async Task<string?> SynthesizeAsync(string text)
    {
        var fileName = $"confirm-{DateTime.Now:HHmmssfff}.wav";
        var path = Path.Combine(_outputDir, fileName);

        var created = await TrySynthesizeWithAzureAsync(text, path).ConfigureAwait(false)
            || await Task.Run(() => TrySynthesizeWithWindows(text, path)).ConfigureAwait(false);
        if (!created)
        {
            return null;
        }

        CleanUpOldFiles();
        return $"tts/{fileName}";
    }

    private static async Task<bool> TrySynthesizeWithAzureAsync(string text, string path)
    {
        var key = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        var region = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
        {
            return false;
        }

        try
        {
            var config = SpeechConfig.FromSubscription(key, region);
            config.SpeechSynthesisVoiceName = VoiceName;
            config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Riff24Khz16BitMonoPcm);

            using var synthesizer = new SpeechSynthesizer(config, (AudioConfig?)null);
            using var result = await synthesizer.SpeakTextAsync(text).ConfigureAwait(false);
            if (result.Reason != ResultReason.SynthesizingAudioCompleted)
            {
                return false;
            }

            await File.WriteAllBytesAsync(path, result.AudioData).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>대체 수단: Windows 내장 음성(ko-KR)으로 WAV 생성. 한국어 음성이 설치되어 있지 않으면 false</summary>
    private static bool TrySynthesizeWithWindows(string text, string path)
    {
        try
        {
            using var synthesizer = new System.Speech.Synthesis.SpeechSynthesizer();
            var korean = synthesizer.GetInstalledVoices()
                .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name.Equals("ko-KR", StringComparison.OrdinalIgnoreCase));
            if (korean is null)
            {
                return false;
            }

            synthesizer.SelectVoice(korean.VoiceInfo.Name);
            synthesizer.SetOutputToWaveFile(path);
            synthesizer.Speak(text);
            synthesizer.SetOutputToNull();
            return true;
        }
        catch (Exception)
        {
            TryDelete(path);
            return false;
        }
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
