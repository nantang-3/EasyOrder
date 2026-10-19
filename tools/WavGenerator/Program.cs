// 안내 음성 WAV 3종 사전 생성 도구 (IR-04)
// Azure Speech TTS(F0, ko-KR SunHi)로 생성하며, 키·지역은 환경 변수에서만 읽는다.
//   AZURE_SPEECH_KEY, AZURE_SPEECH_REGION
// 사용법: dotnet run --project tools/WavGenerator [-- [출력 폴더] [--only 파일명]]
//   --only 를 주면 해당 파일만 생성한다 (이미 만든 파일을 다시 만들지 않기 위해)
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

const string VoiceName = "ko-KR-SunHiNeural";

(string File, string Text)[] prompts =
[
    ("greeting.wav", "안녕하세요. 폴리텍점입니다. 포장이신가요? 매장에서 드시나요?"), // FR-02
    ("ask-order.wav", "무엇을 주문하시겠습니까?"),                                  // FR-05
    ("retry.wav", "천천히 말해주세요."),                                            // FR-09
    ("touch-guide.wav", "음성 인식이 어렵습니다. 화면을 터치해서 주문해 주세요."),  // FR-09 (3회 실패)
    ("cancel.wav", "취소되었습니다. 다시 주문해주세요"),                            // FR-17 (취소)
];

string? only = null;
var positional = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--only" && i + 1 < args.Length)
    {
        only = args[++i];
    }
    else
    {
        positional.Add(args[i]);
    }
}

if (only is not null && !prompts.Any(p => p.File == only))
{
    Console.Error.WriteLine($"알 수 없는 파일명: {only}");
    return 1;
}

var key = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
var region = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
{
    Console.Error.WriteLine("환경 변수 AZURE_SPEECH_KEY, AZURE_SPEECH_REGION이 설정되어 있지 않습니다.");
    return 1;
}

var outputDir = positional.Count > 0 ? positional[0] : Path.Combine("src", "EasyOrder", "wwwroot", "audio");
Directory.CreateDirectory(outputDir);

var config = SpeechConfig.FromSubscription(key, region);
config.SpeechSynthesisVoiceName = VoiceName;
config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Riff24Khz16BitMonoPcm);

foreach (var (file, text) in prompts)
{
    if (only is not null && file != only)
    {
        continue;
    }

    var path = Path.Combine(outputDir, file);
    using (var audio = AudioConfig.FromWavFileOutput(path))
    using (var synthesizer = new SpeechSynthesizer(config, audio))
    {
        using var result = await synthesizer.SpeakTextAsync(text);
        if (result.Reason != ResultReason.SynthesizingAudioCompleted)
        {
            var detail = SpeechSynthesisCancellationDetails.FromResult(result);
            Console.Error.WriteLine($"생성 실패: {file} (사유: {detail.Reason}, 코드: {detail.ErrorCode})");
            return 1;
        }
    }

    Console.WriteLine($"생성 완료: {path} ({new FileInfo(path).Length:N0} bytes)");
}

return 0;
