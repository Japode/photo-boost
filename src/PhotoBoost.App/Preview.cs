using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoBoost.Imaging;

namespace PhotoBoost.App;

/// <summary>
/// WPF's view of an <see cref="RgbaImage"/> (PB23): a frozen 64-bit bitmap, so 16-bit sources show
/// at their depth, and the checkerboard drawn behind transparent pixels. PB40 replaces the bitmap
/// with a GPU surface for images too large for WPF's own Image to pan smoothly.
/// </summary>
internal static class Preview
{
    /// <summary>A frozen bitmap of the image, safe to hand across threads.</summary>
    public static BitmapSource Bitmap(RgbaImage image)
    {
        ushort[] samples = image.Samples.ToArray();
        BitmapSource bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Rgba64, null, samples, image.Width * 8);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>A tiled checkerboard of Brand's two greys, 8 px squares.</summary>
    public static Brush Checkerboard()
    {
        DrawingGroup tile = new();
        tile.Children.Add(new GeometryDrawing(Brand.CheckerLight, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        GeometryGroup dark = new();
        dark.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
        dark.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));
        tile.Children.Add(new GeometryDrawing(Brand.CheckerDark, null, dark));

        DrawingBrush brush = new(tile)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }
}
