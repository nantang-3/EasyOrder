using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyOrder.Models;

/// <summary>메뉴 데이터(menu.json) 최상위 구조 (요구사항명세서 6.1, DR-05)</summary>
public sealed class MenuCatalog
{
    public string Store { get; init; } = "";
    public List<OptionGroup> OptionGroups { get; init; } = [];
    public List<MenuCategory> Categories { get; init; } = [];
}

/// <summary>옵션 그룹 (온도, 사이즈, 샷 조절, 우유 변경)</summary>
public sealed class OptionGroup
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>기본값. 샷 조절은 숫자(0), 우유 변경은 문자열("우유"), 그 외는 없음</summary>
    public JsonElement? Default { get; init; }

    public List<OptionChoice> Choices { get; init; } = [];
}

/// <summary>옵션 선택지. JSON에서는 문자열("HOT") 또는 {label, value} 객체로 표현</summary>
[JsonConverter(typeof(OptionChoiceConverter))]
public sealed class OptionChoice
{
    public string Label { get; init; } = "";
    public int? Value { get; init; }
}

/// <summary>메뉴 대분류 (커피, 라떼, 에이드, 주스, 차)</summary>
public sealed class MenuCategory
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<string> OptionGroups { get; init; } = [];
    public List<MenuItem> Menus { get; init; } = [];
}

/// <summary>메뉴. Aliases는 음성 인식용 별칭 자리</summary>
public sealed class MenuItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<string> Aliases { get; init; } = [];
}

/// <summary>문자열/객체 두 형태의 옵션 선택지를 읽고 쓰는 변환기</summary>
public sealed class OptionChoiceConverter : JsonConverter<OptionChoice>
{
    public override OptionChoice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new OptionChoice { Label = reader.GetString() ?? "" };
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        return new OptionChoice
        {
            Label = root.TryGetProperty("label", out var label) ? label.GetString() ?? "" : "",
            Value = root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : null,
        };
    }

    public override void Write(Utf8JsonWriter writer, OptionChoice value, JsonSerializerOptions options)
    {
        if (value.Value is null)
        {
            writer.WriteStringValue(value.Label);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("label", value.Label);
        writer.WriteNumber("value", value.Value.Value);
        writer.WriteEndObject();
    }
}
