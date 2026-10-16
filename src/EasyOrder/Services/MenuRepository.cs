using System.IO;
using System.Text.Json;
using EasyOrder.Models;

namespace EasyOrder.Services;

/// <summary>실행 폴더의 Data/menu.json에서 메뉴 데이터를 읽는다 (IR-07)</summary>
public static class MenuRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Data", "menu.json");

    public static MenuCatalog Load(string? path = null)
    {
        path ??= DefaultPath;
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<MenuCatalog>(stream, Options)
            ?? throw new InvalidDataException($"메뉴 데이터를 읽을 수 없습니다: {path}");
    }
}
