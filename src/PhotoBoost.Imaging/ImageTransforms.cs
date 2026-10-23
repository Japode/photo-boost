using System.Runtime.InteropServices;

namespace PhotoBoost.Imaging;

/// <summary>
/// The edits that move pixels without changing any (PB65): a quarter turn either way. Each pixel's
/// eight bytes are copied whole, alpha included, so nothing is resampled and four turns give back
/// the same bytes. Rows are walked in blocks so the column-wise writes of a turn stay in the cache,
/// which keeps an 8192 px photo well under a second on the UI thread.
/// </summary>
public static class ImageTransforms
{
    private const int Block = 64;

    /// <summary>The photo turned a quarter clockwise.</summary>
    /// <param name="image">The source.</param>
    /// <returns>A new image, its width the source's height.</returns>
    public static RgbaImage RotateRight(RgbaImage image) => Turn(image, clockwise: true);

    /// <summary>The photo turned a quarter counter-clockwise.</summary>
    /// <param name="image">The source.</param>
    /// <returns>A new image, its width the source's height.</returns>
    public static RgbaImage RotateLeft(RgbaImage image) => Turn(image, clockwise: false);

    private static RgbaImage Turn(RgbaImage image, bool clockwise)
    {
        ArgumentNullException.ThrowIfNull(image);
        int w = image.Width, h = image.Height;
        RgbaImage result = new(h, w, image.SourceBitDepth);
        ReadOnlySpan<ulong> source = MemoryMarshal.Cast<ushort, ulong>(image.Samples);
        Span<ulong> target = MemoryMarshal.Cast<ushort, ulong>(result.Samples);
        for (int by = 0; by < h; by += Block)
        {
            int yEnd = Math.Min(by + Block, h);
            for (int bx = 0; bx < w; bx += Block)
            {
                int xEnd = Math.Min(bx + Block, w);
                for (int y = by; y < yEnd; y++)
                {
                    int row = y * w;
                    for (int x = bx; x < xEnd; x++)
                    {
                        // Clockwise, (x, y) lands at (h - 1 - y, x); counter-clockwise at (y, w - 1 - x).
                        int to = clockwise ? (x * h) + (h - 1 - y) : ((w - 1 - x) * h) + y;
                        target[to] = source[row + x];
                    }
                }
            }
        }

        return result;
    }
}
