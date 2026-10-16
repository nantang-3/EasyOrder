namespace EasyOrder.Models;

/// <summary>
/// 휴대폰 → PC WebSocket 메시지 (IR-08).
/// type: "voice-activated"(음성주문활성화 버튼), 이후 "recognized"(인식 텍스트) 등
/// </summary>
public sealed class PhoneMessage
{
    public string Type { get; init; } = "";
    public string? Text { get; init; }
}
