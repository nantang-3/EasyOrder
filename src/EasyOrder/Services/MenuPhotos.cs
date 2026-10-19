using System.IO;
using System.Windows.Media.Imaging;

namespace EasyOrder.Services;

/// <summary>메뉴 사진 (실행 폴더의 Images/Menu/{메뉴 id}.png, FR-15)</summary>
public static class MenuPhotos
{
    private static readonly Dictionary<string, BitmapImage?> Cache = [];

    /// <summary>사진이 없으면 null</summary>
    public static BitmapImage? Load(string menuId)
    {
        if (Cache.TryGetValue(menuId, out var cached))
        {
            return cached;
        }

        BitmapImage? image = null;
        var path = Path.Combine(AppContext.BaseDirectory, "Images", "Menu", $"{menuId}.png");
        if (File.Exists(path))
        {
            image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
        }

        Cache[menuId] = image;
        return image;
    }
}

/// <summary>메뉴 id → 메뉴 사진 (메뉴 카드용, 없으면 null)</summary>
public sealed class MenuPhotoConverter : System.Windows.Data.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is string id ? MenuPhotos.Load(id) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>메뉴 id → 사진이 없으면 Visible (컵 아이콘 표시용)</summary>
public sealed class MenuPhotoMissingConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is string id && MenuPhotos.Load(id) is not null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
