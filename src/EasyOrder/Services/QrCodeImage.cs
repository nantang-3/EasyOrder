using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace EasyOrder.Services;

/// <summary>문자열(접속 주소)을 WPF에 표시할 QR 이미지로 만든다 (FR-13)</summary>
public static class QrCodeImage
{
    public static BitmapImage Create(string text, int pixelsPerModule = 20)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);

        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
