using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Interop;
using System.Windows.Media;
using PhotoBoost.Imaging;

namespace PhotoBoost.Render;

/// <summary>
/// The image on the GPU inside the WPF window (PB27, on the PB40 spike's path): a child HWND with
/// a flip swapchain. The wheel zooms about the cursor, a drag pans, and in divider mode a drag near
/// the divider moves it. Nothing WPF draws can overlay it, so the divider is the shader's, and its
/// position is published to UI Automation as a range value a case can read and set.
/// </summary>
public sealed class ImageSurface : HwndHost
{
    private const uint WmLButtonDown = 0x0201, WmLButtonUp = 0x0202, WmMouseMove = 0x0200, WmMouseWheel = 0x020A, WmSetCursor = 0x0020;
    private const string ClassName = "PhotoBoostImageSurface";
    private static SurfaceProc? procKeepAlive;
    private static readonly Dictionary<nint, ImageSurface> Hosts = [];

    private SurfaceRenderer? renderer;
    private nint child;
    private View view = View.Fit;
    private CompareMode mode = CompareMode.Single;
    private double divider = 0.5;
    private Drag drag;
    private (int X, int Y) last;
    private RgbaImage? before;
    private RgbaImage? after;

    private delegate nint SurfaceProc(nint hwnd, uint msg, nint wParam, nint lParam);

    private enum Drag
    {
        None,
        Pan,
        Divider,
    }

    /// <summary>The colours the surface paints itself, from the app's Brand.</summary>
    public SurfacePalette Palette { get; set; }

    /// <summary>The comparison mode.</summary>
    public CompareMode Mode
    {
        get => mode;
        set
        {
            mode = value;
            Redraw();
        }
    }

    /// <summary>The divider, 0 to 1 across the surface.</summary>
    public double Divider
    {
        get => divider;
        set
        {
            divider = Math.Clamp(value, 0, 1);
            Redraw();
        }
    }

    /// <summary>The zoom; 1 fits the image.</summary>
    public double Zoom => view.Zoom;

    /// <summary>Whether drawing fell back to the software rasteriser.</summary>
    public bool IsWarp => renderer?.IsWarp ?? false;

    /// <summary>Shows the after image, compared against before when a mode asks; the view resets to fit.</summary>
    /// <param name="beforeImage">The original, or null.</param>
    /// <param name="afterImage">The version shown.</param>
    public void Show(RgbaImage? beforeImage, RgbaImage afterImage)
    {
        before = beforeImage;
        after = afterImage;
        if (renderer is not null)
        {
            bool sameSize = renderer.ImageSize == (afterImage.Width, afterImage.Height);
            renderer.SetImages(beforeImage, afterImage);
            if (!sameSize)
            {
                view = View.Fit;
            }
        }

        Redraw();
    }

    /// <summary>Back to the whole image.</summary>
    public void ResetView()
    {
        view = View.Fit;
        Redraw();
    }

    /// <inheritdoc/>
    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        EnsureClass();
        const int WsChild = 0x40000000, WsVisible = 0x10000000, WsClipSiblings = 0x04000000;
        child = CreateWindowExW(0, ClassName, null, WsChild | WsVisible | WsClipSiblings, 0, 0, 1, 1, hwndParent.Handle, 0, GetModuleHandleW(null), 0);
        Hosts[child] = this;
        renderer = new SurfaceRenderer();
        (int w, int h) = PixelSize();
        renderer.AttachWindow(child, w, h);
        if (after is not null)
        {
            renderer.SetImages(before, after);
        }

        return new HandleRef(this, child);
    }

    /// <inheritdoc/>
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Hosts.Remove(hwnd.Handle);
        renderer?.Dispose();
        renderer = null;
        DestroyWindow(hwnd.Handle);
    }

    /// <inheritdoc/>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        (int w, int h) = PixelSize();
        renderer?.Resize(w, h);
        Redraw();
    }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new SurfacePeer(this);

    /// <summary>
    /// Leaves WM_GETOBJECT on the child window to Windows' default provider. HwndHost would answer it
    /// through its peer's interop child, which only WPF's own internal HwndHost peer supplies; ours,
    /// which publishes the divider, has none, and HwndHost then throws on a null provider and takes
    /// the process down the first time UI Automation looks.
    /// </summary>
    /// <inheritdoc/>
    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WmGetObject = 0x003D;
        if (msg == WmGetObject)
        {
            handled = true;
            return DefWindowProcW(hwnd, (uint)msg, wParam, lParam);
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private (int Width, int Height) PixelSize()
    {
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? VisualTreeHelper.GetDpi(this).DpiScaleX;
        return (Math.Max(1, (int)Math.Round(ActualWidth * scale)), Math.Max(1, (int)Math.Round(ActualHeight * scale)));
    }

    private void Redraw() => renderer?.Render(view, mode, divider, Palette);

    private nint OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (renderer is null || after is null)
        {
            return DefWindowProcW(child, msg, wParam, lParam);
        }

        int x = (short)(lParam & 0xFFFF), y = (short)((lParam >> 16) & 0xFFFF);
        (int iw, int ih) = renderer.ImageSize;
        switch (msg)
        {
            case WmLButtonDown:
                SetFocus(child);
                SetCapture(child);
                drag = mode == CompareMode.Divider && ViewMath.OnDivider(divider, x, renderer.Width) ? Drag.Divider : Drag.Pan;
                last = (x, y);
                return 0;
            case WmMouseMove when drag == Drag.Divider:
                Divider = (double)x / renderer.Width;
                return 0;
            case WmMouseMove when drag == Drag.Pan:
                view = ViewMath.Pan(view, x - last.X, y - last.Y, iw, ih, renderer.Width, renderer.Height);
                last = (x, y);
                Redraw();
                return 0;
            case WmLButtonUp:
                drag = Drag.None;
                ReleaseCapture();
                return 0;
            case WmMouseWheel:
                // Wheel coordinates are screen coordinates.
                POINT p = new() { X = x, Y = y };
                ScreenToClient(child, ref p);
                double notches = (short)((wParam >> 16) & 0xFFFF) / 120.0;
                view = ViewMath.ZoomAt(view, Math.Pow(1.25, notches), iw, ih, renderer.Width, renderer.Height, p.X, p.Y);
                Redraw();
                return 0;
            case WmSetCursor when mode == CompareMode.Divider:
                break;
        }

        return DefWindowProcW(child, msg, wParam, lParam);
    }

    private static void EnsureClass()
    {
        if (procKeepAlive is not null)
        {
            return;
        }

        procKeepAlive = (hwnd, msg, wParam, lParam) =>
            Hosts.TryGetValue(hwnd, out ImageSurface? host) ? host.OnMessage(msg, wParam, lParam) : DefWindowProcW(hwnd, msg, wParam, lParam);
        WndClassEx description = new()
        {
            Size = (uint)Marshal.SizeOf<WndClassEx>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(procKeepAlive),
            Instance = GetModuleHandleW(null),
            Cursor = LoadCursorW(0, 32512),
            ClassName = Marshal.StringToHGlobalUni(ClassName),
        };
        if (RegisterClassExW(ref description) == 0)
        {
            throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public nint WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint IconSmall;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx description);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint ex, string cls, string? name, int style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint hwnd, ref POINT point);

    [DllImport("user32.dll")]
    private static extern nint LoadCursorW(nint instance, nint name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    /// <summary>
    /// What the surface tells UI Automation: a slider named Comparison whose value is the divider in
    /// percent, settable, and whose item status says the zoom and mode, since a swapchain publishes
    /// nothing a case could otherwise read (PB40, winwright WW552).
    /// </summary>
    private sealed class SurfacePeer(ImageSurface owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        public double Value => Math.Round(owner.Divider * 100, 1);

        public bool IsReadOnly => owner.Mode != CompareMode.Divider;

        public double Maximum => 100;

        public double Minimum => 0;

        public double LargeChange => 10;

        public double SmallChange => 1;

        public void SetValue(double value)
        {
            if (IsReadOnly)
            {
                throw new ElementNotEnabledException();
            }

            owner.Divider = value / 100;
        }

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

        protected override string GetClassNameCore() => nameof(ImageSurface);

        protected override string GetNameCore() => "Comparison";

        protected override string GetItemStatusCore() =>
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"zoom {owner.Zoom:0.##}x, {owner.Mode}{(owner.IsWarp ? ", WARP" : string.Empty)}");

        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
