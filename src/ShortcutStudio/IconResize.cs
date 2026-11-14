using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShortcutStudio;

public static class IconResize
{
    /// <summary>Centre-crops to a square and scales to 64×64 PNG (Steam's achievement icon size, 128 for crispness).</summary>
    public static byte[] To64(byte[] bytes, int size = 128)
    {
        var src = Images.Load(bytes);
        if (src == null) return bytes;
        if (src.PixelWidth == src.PixelHeight && src.PixelWidth <= size) return bytes;

        int side = Math.Min(src.PixelWidth, src.PixelHeight);
        var crop = new CroppedBitmap(src, new System.Windows.Int32Rect((src.PixelWidth - side) / 2, (src.PixelHeight - side) / 2, side, side));
        double scale = Math.Min(1.0, (double)size / side);
        var scaled = new TransformedBitmap(crop, new ScaleTransform(scale, scale));

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(scaled));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
