using EasyOrder.Models;

namespace EasyOrder.Services;

/// <summary>
/// 인식된 문장에서 식사 방식을 판정한다 (FR-03).
/// 띄어쓰기·문장부호를 무시하고 동의어가 문장에 들어 있으면 인정한다.
/// 양쪽 말이 모두 들어 있거나 둘 다 없으면 null(인식 실패)
/// </summary>
public static class DineModeParser
{
    private static readonly string[] TakeoutWords = ["포장", "테이크아웃", "가져갈게요", "가지고갈게요", "들고갈게요"];
    private static readonly string[] DineInWords = ["매장", "먹고갈게요", "여기서먹을게요", "여기서먹고갈게요", "매장에서먹을게요"];

    public static DineMode? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = Normalize(text);
        var takeout = TakeoutWords.Any(normalized.Contains);
        var dineIn = DineInWords.Any(normalized.Contains);
        if (takeout == dineIn)
        {
            return null;
        }

        return takeout ? DineMode.Takeout : DineMode.DineIn;
    }

    private static string Normalize(string text) =>
        new(text.Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)).ToArray());
}
