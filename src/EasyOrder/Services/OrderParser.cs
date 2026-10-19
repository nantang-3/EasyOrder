using System.Text.RegularExpressions;
using EasyOrder.Models;

namespace EasyOrder.Services;

/// <summary>음성 주문 분석 결과의 한 줄 (메뉴·수량)</summary>
public sealed record ParsedOrderLine(MenuCategory Category, MenuItem Menu, int Quantity);

/// <summary>음성 주문 분석 결과</summary>
public sealed class OrderParseResult
{
    /// <summary>"취소"라고 말했는지 (FR-17)</summary>
    public bool IsCancel { get; init; }

    public IReadOnlyList<ParsedOrderLine> Lines { get; init; } = [];
}

/// <summary>
/// 인식된 문장에서 메뉴와 수량을 분석한다 (FR-07).
/// - 띄어쓰기·문장부호 무시, 표기 차이(라테→라떼, 마키아토→마끼아또, 쥬스→주스) 보정
/// - 메뉴명과 menu.json의 별칭(aliases)을 긴 것부터 찾아 여러 메뉴를 구분 (복수 메뉴 허용)
/// - 각 메뉴 바로 뒤의 수량(숫자 또는 한/두/세…, 하나/둘/셋…)을 읽고, 없으면 1잔, 최대 999
/// </summary>
public sealed partial class OrderParser
{
    private static readonly (string From, string To)[] SpellingVariants =
    [
        ("라테", "라떼"),
        ("마키아토", "마끼아또"),
        ("쥬스", "주스"),
    ];

    private static readonly (string Word, int Value)[] Tens =
    [
        ("스물", 20), ("스무", 20), ("서른", 30), ("마흔", 40), ("쉰", 50),
        ("예순", 60), ("일흔", 70), ("여든", 80), ("아흔", 90), ("열", 10),
    ];

    // (단어, 값, 단독으로 쓰이는 말인지). 한/두/세/네는 뒤에 잔·개 등이 붙어야 수량으로 인정
    private static readonly (string Word, int Value, bool Standalone)[] Units =
    [
        ("하나", 1, true), ("다섯", 5, true), ("여섯", 6, true), ("일곱", 7, true), ("여덟", 8, true), ("아홉", 9, true),
        ("둘", 2, true), ("셋", 3, true), ("넷", 4, true),
        ("한", 1, false), ("두", 2, false), ("세", 3, false), ("석", 3, false), ("네", 4, false), ("넉", 4, false),
    ];

    private static readonly string[] Counters = ["잔", "개", "컵", "병"];

    private readonly List<(string Key, MenuCategory Category, MenuItem Menu)> _keys = [];

    public OrderParser(MenuCatalog catalog)
    {
        foreach (var category in catalog.Categories)
        {
            foreach (var menu in category.Menus)
            {
                foreach (var name in menu.Aliases.Prepend(menu.Name))
                {
                    var key = Normalize(name);
                    if (key.Length > 0)
                    {
                        _keys.Add((key, category, menu));
                    }
                }
            }
        }

        _keys.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
    }

    [GeneratedRegex(@"^\d{1,4}")]
    private static partial Regex LeadingDigits();

    public OrderParseResult Parse(string? text)
    {
        var normalized = Normalize(text ?? "");
        if (normalized.Length == 0)
        {
            return new OrderParseResult();
        }

        if (normalized.Contains("취소"))
        {
            return new OrderParseResult { IsCancel = true };
        }

        // 메뉴 위치 찾기 (긴 이름 우선, 겹치지 않게)
        var matches = new List<(int Start, int End, MenuCategory Category, MenuItem Menu)>();
        var i = 0;
        while (i < normalized.Length)
        {
            var found = false;
            foreach (var (key, category, menu) in _keys)
            {
                if (string.CompareOrdinal(normalized, i, key, 0, key.Length) == 0)
                {
                    matches.Add((i, i + key.Length, category, menu));
                    i += key.Length;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                i++;
            }
        }

        var lines = new List<ParsedOrderLine>();
        for (var m = 0; m < matches.Count; m++)
        {
            var segmentEnd = m + 1 < matches.Count ? matches[m + 1].Start : normalized.Length;
            var segment = normalized[matches[m].End..segmentEnd];
            var quantity = ParseQuantity(segment) ?? 1;
            lines.Add(new ParsedOrderLine(matches[m].Category, matches[m].Menu, Math.Clamp(quantity, 1, OrderItem.MaxQuantity)));
        }

        return new OrderParseResult { Lines = lines };
    }

    /// <summary>메뉴 바로 뒤에 오는 수량. 없으면 null</summary>
    public static int? ParseQuantity(string segment)
    {
        var digits = LeadingDigits().Match(segment);
        if (digits.Success)
        {
            return int.Parse(digits.Value);
        }

        var pos = 0;
        var value = 0;
        var needsCounter = false;
        foreach (var (word, tens) in Tens)
        {
            if (segment.AsSpan(pos).StartsWith(word))
            {
                value += tens;
                pos += word.Length;
                needsCounter = word == "스무";
                break;
            }
        }

        foreach (var (word, unit, standalone) in Units)
        {
            if (segment.AsSpan(pos).StartsWith(word))
            {
                value += unit;
                pos += word.Length;
                needsCounter = !standalone;
                break;
            }
        }

        if (value == 0)
        {
            return null;
        }

        if (needsCounter && !Counters.Any(c => segment.AsSpan(pos).StartsWith(c)))
        {
            return null;
        }

        return value;
    }

    private static string Normalize(string text)
    {
        var compact = new string(text.Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)).ToArray());
        foreach (var (from, to) in SpellingVariants)
        {
            compact = compact.Replace(from, to);
        }

        return compact;
    }
}

/// <summary>확인 멘트용 수량 읽기 (1~99는 "한/두/…/스무/아흔아홉", 그 이상은 숫자)</summary>
public static class KoreanCount
{
    private static readonly string[] UnitWords = ["", "한", "두", "세", "네", "다섯", "여섯", "일곱", "여덟", "아홉"];
    private static readonly string[] TensWords = ["", "열", "스물", "서른", "마흔", "쉰", "예순", "일흔", "여든", "아흔"];

    public static string ToSpoken(int n)
    {
        if (n is < 1 or > 99)
        {
            return n.ToString();
        }

        if (n == 20)
        {
            return "스무";
        }

        return TensWords[n / 10] + UnitWords[n % 10];
    }
}
