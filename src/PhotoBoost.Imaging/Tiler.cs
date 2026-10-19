namespace PhotoBoost.Imaging;

/// <summary>One tile: the pixels a model reads, and the part of them whose output is kept.</summary>
/// <param name="Input">What is fed to the model: the core plus the overlap, clamped to the image.</param>
/// <param name="Core">What this tile owns; the cores of a plan partition the image exactly.</param>
public readonly record struct Tile(PixelRect Input, PixelRect Core);

/// <summary>A model as the tiler sees it: a 1 x 3 x H x W tensor in, 1 x 3 x (H x scale) x (W x scale) out.</summary>
/// <param name="input">Planar RGB.</param>
/// <param name="width">W.</param>
/// <param name="height">H.</param>
/// <returns>Planar RGB, scale times larger each way.</returns>
public delegate float[] TileModel(float[] input, int width, int height);

/// <summary>
/// Runs a model over an image in overlapping tiles and stitches the result (PB18): a 12 MP photo
/// through Real-ESRGAN at once exhausts memory, and tiles keep the peak bounded. Each tile reads
/// <c>overlap</c> pixels of context on every side and keeps only its core, so a model whose output
/// depends on neighbours within the overlap gives exactly what one whole-image run would.
/// </summary>
public static class Tiler
{
    /// <summary>The tile side a run starts from unless told otherwise (PB41 sizes it from VRAM).</summary>
    public const int DefaultTile = 256;

    /// <summary>The context read around each tile unless told otherwise.</summary>
    public const int DefaultOverlap = 16;

    /// <summary>Splits an image into tiles whose cores partition it.</summary>
    /// <param name="width">Image columns.</param>
    /// <param name="height">Image rows.</param>
    /// <param name="tile">The core side.</param>
    /// <param name="overlap">Context on each side.</param>
    /// <param name="multiple">Every input side must be a multiple of this; the image's must be too.</param>
    /// <returns>The tiles, row by row.</returns>
    public static IReadOnlyList<Tile> Plan(int width, int height, int tile, int overlap, int multiple = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tile, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(overlap);
        ArgumentOutOfRangeException.ThrowIfLessThan(multiple, 1);
        if (width % multiple != 0 || height % multiple != 0)
        {
            throw new ArgumentException($"{width} x {height} is not a multiple of {multiple}; pad the image first.");
        }

        // Rounding the core and the overlap up to the multiple keeps every input side a multiple,
        // since each input edge is a core edge or a clamped image edge, both multiples.
        tile = RoundUp(tile, multiple);
        overlap = RoundUp(overlap, multiple);
        List<Tile> tiles = [];
        for (int y = 0; y < height; y += tile)
        {
            for (int x = 0; x < width; x += tile)
            {
                PixelRect core = new(x, y, Math.Min(tile, width - x), Math.Min(tile, height - y));
                int left = Math.Max(0, x - overlap);
                int top = Math.Max(0, y - overlap);
                int right = Math.Min(width, core.X + core.Width + overlap);
                int bottom = Math.Min(height, core.Y + core.Height + overlap);
                tiles.Add(new Tile(new PixelRect(left, top, right - left, bottom - top), core));
            }
        }

        return tiles;
    }

    /// <summary>Runs a model over the image tile by tile and returns the stitched result.</summary>
    /// <param name="source">The image.</param>
    /// <param name="model">The model.</param>
    /// <param name="options">Scale, tiling and the model's value range.</param>
    /// <param name="progress">Called after each tile with (done, total); may throw to cancel.</param>
    /// <returns>An image scale times larger, its alpha the source's resized by nearest neighbour.</returns>
    public static RgbaImage Run(RgbaImage source, TileModel model, TileOptions options, Action<int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);

        RgbaImage padded = PadToMultiple(source, options.Multiple);
        int scale = options.Scale;
        RgbaImage output = new(padded.Width * scale, padded.Height * scale, source.SourceBitDepth);
        IReadOnlyList<Tile> tiles = Plan(padded.Width, padded.Height, options.Tile, options.Overlap, options.Multiple);

        for (int i = 0; i < tiles.Count; i++)
        {
            Tile tile = tiles[i];
            float[] input = new float[3 * tile.Input.Area];
            TensorConversion.ToTensor(padded, tile.Input, options.Low, options.High, input);
            float[] result = model(input, tile.Input.Width, tile.Input.Height);
            int outWidth = tile.Input.Width * scale;
            int outHeight = tile.Input.Height * scale;
            if (result.Length != 3 * outWidth * outHeight)
            {
                throw new InvalidOperationException(
                    $"The model returned {result.Length} values for a {tile.Input.Width} x {tile.Input.Height} tile; x{scale} needs {3 * outWidth * outHeight}.");
            }

            PixelRect keep = new(
                (tile.Core.X - tile.Input.X) * scale,
                (tile.Core.Y - tile.Input.Y) * scale,
                tile.Core.Width * scale,
                tile.Core.Height * scale);
            float[] core = Crop(result, outWidth, outHeight, keep);
            TensorConversion.FromTensor(core, options.Low, options.High, output, new PixelRect(tile.Core.X * scale, tile.Core.Y * scale, keep.Width, keep.Height));
            progress?.Invoke(i + 1, tiles.Count);
        }

        ScaleAlpha(padded, output, scale);
        return padded == source ? output : CropImage(output, source.Width * scale, source.Height * scale);
    }

    /// <summary>A copy grown right and down to a multiple, repeating the last column and row.</summary>
    /// <param name="image">The image.</param>
    /// <param name="multiple">The multiple.</param>
    /// <returns>The image itself when it already fits.</returns>
    public static RgbaImage PadToMultiple(RgbaImage image, int multiple)
    {
        ArgumentNullException.ThrowIfNull(image);
        int width = RoundUp(image.Width, multiple);
        int height = RoundUp(image.Height, multiple);
        if (width == image.Width && height == image.Height)
        {
            return image;
        }

        RgbaImage padded = new(width, height, image.SourceBitDepth);
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<ushort> from = image.Row(Math.Min(y, image.Height - 1));
            Span<ushort> to = padded.Row(y);
            from.CopyTo(to);
            ReadOnlySpan<ushort> last = from[^4..];
            for (int x = image.Width; x < width; x++)
            {
                last.CopyTo(to.Slice(x * 4, 4));
            }
        }

        return padded;
    }

    private static float[] Crop(float[] tensor, int width, int height, PixelRect keep)
    {
        float[] cropped = new float[3 * keep.Area];
        for (int channel = 0; channel < 3; channel++)
        {
            for (int y = 0; y < keep.Height; y++)
            {
                tensor.AsSpan((channel * width * height) + ((keep.Y + y) * width) + keep.X, keep.Width)
                    .CopyTo(cropped.AsSpan((channel * keep.Area) + (y * keep.Width), keep.Width));
            }
        }

        return cropped;
    }

    private static void ScaleAlpha(RgbaImage source, RgbaImage output, int scale)
    {
        for (int y = 0; y < output.Height; y++)
        {
            ReadOnlySpan<ushort> from = source.Row(y / scale);
            Span<ushort> to = output.Row(y);
            for (int x = 0; x < output.Width; x++)
            {
                to[(x * 4) + 3] = from[((x / scale) * 4) + 3];
            }
        }
    }

    private static RgbaImage CropImage(RgbaImage image, int width, int height)
    {
        RgbaImage cropped = new(width, height, image.SourceBitDepth);
        for (int y = 0; y < height; y++)
        {
            image.Row(y)[..(width * 4)].CopyTo(cropped.Row(y));
        }

        return cropped;
    }

    private static int RoundUp(int value, int multiple) => (value + multiple - 1) / multiple * multiple;
}

/// <summary>How to tile a model run.</summary>
/// <param name="Scale">Output size over input size (4 for x4plus, 1 for a mask).</param>
/// <param name="Low">The model's value for sample 0, from meta.json.</param>
/// <param name="High">The model's value for sample 65535.</param>
/// <param name="Tile">The core side; rounded up to <paramref name="Multiple"/>.</param>
/// <param name="Overlap">Context on each side; rounded up to <paramref name="Multiple"/>.</param>
/// <param name="Multiple">Input sides must be a multiple of this (2 for x2plus).</param>
public sealed record TileOptions(int Scale, float Low, float High, int Tile = Tiler.DefaultTile, int Overlap = Tiler.DefaultOverlap, int Multiple = 1);
