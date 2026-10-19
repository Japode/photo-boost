namespace PhotoBoost.Imaging.Tests;

/// <summary>PB25: what is exported reads back as what was in memory, within each format's promise.</summary>
public sealed class ImageEncoderTests : IDisposable
{
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "pb-encode-" + Guid.NewGuid().ToString("N"));

    public ImageEncoderTests() => Directory.CreateDirectory(scratch);

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    [Fact]
    public void A16BitPngRoundTripsExactlyWithAlpha()
    {
        RgbaImage image = Pattern(13, 7, 16, transparent: true);

        RgbaImage back = RoundTrip(image, "out.png", new EncodeOptions(ImageFormat.Png));

        Assert.Equal(16, back.SourceBitDepth);
        Assert.Equal(image.Samples.ToArray(), back.Samples.ToArray());
    }

    [Fact]
    public void An8BitOpaquePngRoundTripsExactly()
    {
        RgbaImage image = Pattern(13, 7, 8, transparent: false);

        RgbaImage back = RoundTrip(image, "out.png", new EncodeOptions(ImageFormat.Png));

        Assert.Equal(8, back.SourceBitDepth);
        Assert.Equal(image.Samples.ToArray(), back.Samples.ToArray());
    }

    [Fact]
    public void AJpegKeepsColoursWithinItsLoss()
    {
        RgbaImage image = Flat(32, 16, (200, 40, 90, 255));

        RgbaImage back = RoundTrip(image, "out.jpg", new EncodeOptions(ImageFormat.Jpeg, Quality: 95));

        AssertNear(back, 12, 6, (200, 40, 90), tolerance: 4);
        Assert.False(back.HasTransparency);
    }

    [Fact]
    public void TransparencyBecomesTheBackgroundInAJpeg()
    {
        RgbaImage image = Flat(16, 16, (0, 0, 0, 0));
        Assert.True(ImageEncoder.LosesTransparency(image, ImageFormat.Jpeg));

        RgbaImage back = RoundTrip(image, "out.jpg", new EncodeOptions(ImageFormat.Jpeg, Quality: 100, Background: (10, 200, 30)));

        AssertNear(back, 8, 8, (10, 200, 30), tolerance: 4);
    }

    [Fact]
    public void ALosslessWebpRoundTripsAt8BitsWithAlpha()
    {
        RgbaImage image = Pattern(9, 5, 8, transparent: true);

        RgbaImage back = RoundTrip(image, "out.webp", new EncodeOptions(ImageFormat.Webp, Lossless: true));

        Assert.Equal(image.Samples.ToArray(), back.Samples.ToArray());
    }

    [Fact]
    public void ALossyWebpKeepsColoursWithinItsLoss()
    {
        RgbaImage image = Flat(64, 64, (120, 160, 30, 255));

        RgbaImage back = RoundTrip(image, "lossy.webp", new EncodeOptions(ImageFormat.Webp, Quality: 80));

        AssertNear(back, 32, 32, (120, 160, 30), tolerance: 6);
    }

    [Fact]
    public void AnExistingFileIsReplacedAndNoPartialFileIsLeft()
    {
        string path = Path.Combine(scratch, "out.png");
        File.WriteAllText(path, "old");

        ImageEncoder.Save(Pattern(4, 4, 8, transparent: false), path, new EncodeOptions(ImageFormat.Png));

        Assert.Equal((4, 4), (ImageDecoder.Decode(path).Width, ImageDecoder.Decode(path).Height));
        Assert.Single(Directory.GetFiles(scratch));
    }

    [Theory]
    [InlineData("a.PNG", ImageFormat.Png)]
    [InlineData("b.jpeg", ImageFormat.Jpeg)]
    [InlineData("c.jpg", ImageFormat.Jpeg)]
    [InlineData("d.webp", ImageFormat.Webp)]
    public void TheExtensionNamesTheFormat(string path, ImageFormat expected) => Assert.Equal(expected, ImageEncoder.FormatFor(path));

    [Fact]
    public void AnUnknownExtensionNamesNoFormat() => Assert.Null(ImageEncoder.FormatFor("e.gif"));

    [Fact]
    public void AnOpaqueImageLosesNothingAsJpegAndPngNeverDoes()
    {
        Assert.False(ImageEncoder.LosesTransparency(Flat(2, 2, (1, 2, 3, 255)), ImageFormat.Jpeg));
        Assert.False(ImageEncoder.LosesTransparency(Flat(2, 2, (1, 2, 3, 0)), ImageFormat.Png));
    }

    private RgbaImage RoundTrip(RgbaImage image, string name, EncodeOptions options)
    {
        string path = Path.Combine(scratch, name);
        ImageEncoder.Save(image, path, options);
        return ImageDecoder.Decode(path);
    }

    private static RgbaImage Pattern(int w, int h, int depth, bool transparent)
    {
        RgbaImage image = new(w, h, depth);
        Span<ushort> s = image.Samples;
        for (int i = 0; i < s.Length; i++)
        {
            ushort value = (ushort)((i * 7919L) % (RgbaImage.Max + 1));
            s[i] = depth == 8 ? RgbaImage.From8(RgbaImage.To8(value)) : value;
            if (i % 4 == 3 && !transparent)
            {
                s[i] = RgbaImage.Max;
            }
        }

        return image;
    }

    private static RgbaImage Flat(int w, int h, (byte R, byte G, byte B, byte A) colour)
    {
        RgbaImage image = new(w, h);
        Span<ushort> s = image.Samples;
        for (int i = 0; i < s.Length; i += 4)
        {
            s[i] = RgbaImage.From8(colour.R);
            s[i + 1] = RgbaImage.From8(colour.G);
            s[i + 2] = RgbaImage.From8(colour.B);
            s[i + 3] = RgbaImage.From8(colour.A);
        }

        return image;
    }

    private static void AssertNear(RgbaImage image, int x, int y, (int R, int G, int B) expected, int tolerance)
    {
        Span<ushort> p = image.Row(y).Slice(x * 4, 4);
        Assert.InRange(RgbaImage.To8(p[0]), expected.R - tolerance, expected.R + tolerance);
        Assert.InRange(RgbaImage.To8(p[1]), expected.G - tolerance, expected.G + tolerance);
        Assert.InRange(RgbaImage.To8(p[2]), expected.B - tolerance, expected.B + tolerance);
    }
}
