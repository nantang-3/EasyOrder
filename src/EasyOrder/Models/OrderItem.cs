using System.ComponentModel;

namespace EasyOrder.Models;

/// <summary>주문 목록 항목 (FR-08, FR-12, FR-15)</summary>
public sealed class OrderItem : INotifyPropertyChanged
{
    public const int MaxQuantity = 999;

    private int _quantity = 1;

    public required MenuItem Menu { get; init; }

    /// <summary>옵션 요약 (예: "ICE · Normal · 두유")</summary>
    public required string OptionSummary { get; init; }

    /// <summary>음성으로 담았는지 여부 (false면 터치)</summary>
    public required bool IsVoice { get; init; }

    /// <summary>같은 음성 발화로 함께 담긴 항목 묶음 (취소 시 함께 제거, FR-17)</summary>
    public Guid? VoiceBatchId { get; init; }

    public string Name => Menu.Name;

    public string SourceLabel => IsVoice ? "음성" : "터치";

    /// <summary>수량 (1~999)</summary>
    public int Quantity
    {
        get => _quantity;
        set
        {
            var clamped = Math.Clamp(value, 1, MaxQuantity);
            if (clamped == _quantity)
            {
                return;
            }

            _quantity = clamped;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
