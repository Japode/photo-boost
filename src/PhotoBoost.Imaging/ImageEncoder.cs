using SharpGen.Runtime.Win32;
using SkiaSharp;
using Vortice.WIC;

namespace PhotoBoost.Imaging;

/// <summary>The formats a result can be exported as.</summary>
public enum ImageFormat
{
    /// <summary>Lossless, alpha kept, 16 bits a channel when the source had them.</summary>
    Png,

    /// <summary>Lossy with a quality setting; no alpha, so transparency is flattened.</summary>
    Jpeg,

    /// <summary>Lossy or lossless, alpha kept.</summary>
    Webp,
}

/// <summary>How to write an export.</summary>
/// <param name="Format">The file format.</param>
/// <param name="Quality">1 to 100, for JPEG and lossy WebP.</param>
/// <param name="Lossless">WebP only: write it lossless.</param>
/// <param name="Background">The colour transparency is flattened onto for JPEG, as 8-bit R, G, B.</param>
public sealed record EncodeOptions(ImageFormat Format, int Quality = 92, bool Lossless = false, (byte R, byte G, byte B)? Background = null);

/// <summary>
/// Writes an <see cref="RgbaImage"/> to a file (PB25). PNG and JPEG go through WIC, Windows' own
/// codecs; WIC has no WebP encoder (WINCODEC_ERR_COMPONENTNOTFOUND on the PB25 desk), so WebP goes
/// through SkiaSharp, MIT, used for nothing else. No metadata is written: the pixels are already
/// upright and in sRGB, which is what a viewer assumes without any.
/// </summary>
public static class ImageEncoder
{
    /// <summary>White, what transparency becomes in a JPEG unless the user picks otherwise.</summary>
    public static readonly (byte R, byte G, byte B) DefaultBackground = (255, 255, 255);

    /// <summary>The format a file name asks for.</summary>
    /// <param name="path">The file name.</param>
    /// <returns>The format, or null for an extension none of them use.</returns>
    public static ImageFormat? FormatFor(string path) => Path.GetExtension(path).ToUpperInvariant() switch
    {
        ".PNG" => ImageFormat.Png,
        ".JPG" or ".JPEG" => ImageFormat.Jpeg,
        ".WEBP" => ImageFormat.Webp,
        _ => null,
    };

    /// <summary>Whether writing the image in this format would drop transparency, which the UI warns about.</summary>
    /// <param name="image">The image.</param>
    /// <param name="format">The format.</param>
    /// <returns>True for a JPEG of an image with any transparent pixel.</returns>
    public static bool LosesTransparency(RgbaImage image, ImageFormat format)
    {
        ArgumentNullException.ThrowIfNull(image);
        return format == ImageFormat.Jpeg && image.HasTransparency;
    }

    /// <summary>Writes the file, replacing one already there.</summary>
    /// <param name="image">The image.</param>
    /// <param name="path">The file.</param>
    /// <param name="options">Format and quality.</param>
    public static void Save(RgbaImage image, string path, EncodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Quality, 100);

        // Written beside the target and moved over it, so a failed export never leaves half a file.
        string partial = path + ".partial";
        try
        {
            switch (options.Format)
            {
                case ImageFormat.Png:
                    SavePng(image, partial);
                    break;
                case ImageFormat.Jpeg:
                    SaveJpeg(image, partial, options);
                    break;
                case ImageFormat.Webp:
                    SaveWebp(image, partial, options);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options), options.Format, "Unknown format.");
            }

            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            File.Delete(partial);
        }
    }

    private static void SavePng(RgbaImage image, string path)
    {
        bool alpha = image.HasTransparency;
        if (image.SourceBitDepth == 16)
        {
            int channels = alpha ? 4 : 3;
            ushort[] samples = Pack16(image, channels);
            byte[] bytes = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            WriteWic(path, ContainerFormatGuids.Png, alpha ? PixelFormat.Format64bppRGBA : PixelFormat.Format48bppRGB, image.Width, image.Height, bytes, image.Width * channels * 2, null);
        }
        else
        {
            WriteWic(path, ContainerFormatGuids.Png, alpha ? PixelFormat.Format32bppBGRA : PixelFormat.Format24bppBGR, image.Width, image.Height, Pack8Bgr(image, alpha, null), image.Width * (alpha ? 4 : 3), null);
        }
    }

    private static void SaveJpeg(RgbaImage image, string path, EncodeOptions options)
    {
        byte[] bgr = Pack8Bgr(image, alpha: false, options.Background ?? DefaultBackground);
        WriteWic(path, ContainerFormatGuids.Jpeg, PixelFormat.Format24bppBGR, image.Width, image.Height, bgr, image.Width * 3, options.Quality / 100f);
    }

    private static void SaveWebp(RgbaImage image, string path, EncodeOptions options)
    {
        SKImageInfo info = new(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using SKBitmap bitmap = new(info);
        Span<byte> pixels = bitmap.GetPixelSpan();
        ReadOnlySpan<ushort> samples = image.Samples;
        for (int i = 0; i < samples.Length; i++)
        {
            pixels[i] = RgbaImage.To8(samples[i]);
        }

        using SKPixmap pixmap = bitmap.PeekPixels();
        SKWebpEncoderOptions webp = new(options.Lossless ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy, options.Quality);
        using SKData data = pixmap.Encode(webp) ?? throw new IOException("SkiaSharp could not encode the WebP.");
        using FileStream file = File.Create(path);
        data.SaveTo(file);
    }

    private static ushort[] Pack16(RgbaImage image, int channels)
    {
        ReadOnlySpan<ushort> samples = image.Samples;
        ushort[] packed = new ushort[image.Width * image.Height * channels];
        for (int p = 0, o = 0; p < samples.Length; p += 4, o += channels)
        {
            samples.Slice(p, channels).CopyTo(packed.AsSpan(o, channels));
        }

        return packed;
    }

    /// <summary>8-bit BGR or BGRA; with a background, alpha is composited onto it instead.</summary>
    private static byte[] Pack8Bgr(RgbaImage image, bool alpha, (byte R, byte G, byte B)? background)
    {
        int channels = alpha ? 4 : 3;
        ReadOnlySpan<ushort> samples = image.Samples;
        byte[] packed = new byte[image.Width * image.Height * channels];
        for (int p = 0, o = 0; p < samples.Length; p += 4, o += channels)
        {
            float a = background is null ? 1f : samples[p + 3] / (float)RgbaImage.Max;
            (byte r, byte g, byte b) = background ?? (0, 0, 0);
            packed[o] = Over(samples[p + 2], b, a);
            packed[o + 1] = Over(samples[p + 1], g, a);
            packed[o + 2] = Over(samples[p], r, a);
            if (alpha)
            {
                packed[o + 3] = RgbaImage.To8(samples[p + 3]);
            }
        }

        return packed;
    }

    private static byte Over(ushort sample, byte background, float alpha) =>
        alpha >= 1f ? RgbaImage.To8(sample) : (byte)MathF.Round((sample / 257f * alpha) + (background * (1 - alpha)));

    private static unsafe void WriteWic(string path, Guid container, Guid format, int width, int height, byte[] pixels, int stride, float? quality)
    {
        using IWICImagingFactory factory = new();
        using IWICStream stream = factory.CreateStream(path, FileAccess.Write);
        using IWICBitmapEncoder encoder = factory.CreateEncoder(container);
        encoder.Initialize(stream, BitmapEncoderCacheOption.NoCache);
        using IWICBitmapFrameEncode frame = encoder.CreateNewFrame(out IPropertyBag2 properties);
        using (properties)
        {
            if (quality is float q)
            {
                properties.Set("ImageQuality", q);
            }

            frame.Initialize(properties);
        }

        frame.SetSize((uint)width, (uint)height);
        Guid requested = format;
        frame.SetPixelFormat(ref requested);
        if (requested != format)
        {
            throw new InvalidOperationException($"WIC's encoder would not take {format}; it offered {requested}.");
        }

        fixed (byte* data = pixels)
        {
            frame.WritePixels((uint)height, (uint)stride, (uint)pixels.Length, data);
        }

        frame.Commit();
        encoder.Commit();
    }
}
