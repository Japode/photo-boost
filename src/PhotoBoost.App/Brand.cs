using System.Windows.Media;

namespace PhotoBoost.App;

/// <summary>
/// Every colour PhotoBoost decides for itself, each declared once, as bytes (PB37).
///
/// <para><b>Borrowed from claude-tray's Brand and quickshell's</b>: a colour is a value, and each
/// surface converts it at its own edge. Everything else is Fluent's: the chrome paints with the
/// theme's brushes, so light, dark and the user's accent come from Windows. What is here is only
/// what Fluent has no opinion on, and an architecture test refuses a colour written anywhere else.</para>
/// </summary>
public static class Brand
{
    /// <summary>The light squares of the checkerboard drawn behind transparent pixels.</summary>
    public const byte CheckerLightLevel = 0xF2;

    /// <summary>The dark squares, close enough to the light ones that the photo stays the subject.</summary>
    public const byte CheckerDarkLevel = 0xD9;

    /// <summary>The before/after divider's red, green and blue: an orange that stays visible on any photo.</summary>
    public const byte DividerR = 0xE8;

    /// <inheritdoc cref="DividerR"/>
    public const byte DividerG = 0x59;

    /// <inheritdoc cref="DividerR"/>
    public const byte DividerB = 0x0C;

    /// <summary>What transparency becomes in a JPEG export by default (PB25): paper white.</summary>
    public static readonly (byte R, byte G, byte B) JpegPaper = (255, 255, 255);

    /// <summary>The other JPEG background offered, for subjects cut out to sit on dark pages.</summary>
    public static readonly (byte R, byte G, byte B) JpegNight = (0, 0, 0);

    /// <summary>The checkerboard's light squares.</summary>
    public static readonly Brush CheckerLight = Frozen(Color.FromRgb(CheckerLightLevel, CheckerLightLevel, CheckerLightLevel));

    /// <summary>The checkerboard's dark squares.</summary>
    public static readonly Brush CheckerDark = Frozen(Color.FromRgb(CheckerDarkLevel, CheckerDarkLevel, CheckerDarkLevel));

    /// <summary>The comparison slider's line (PB27).</summary>
    public static readonly Brush Divider = Frozen(Color.FromRgb(DividerR, DividerG, DividerB));

    /// <summary>Frozen, as every shared brush is: used across threads, never changed, no lock per draw.</summary>
    private static SolidColorBrush Frozen(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }
}
