using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas.Effects;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace ClearDesk.GlassWindow;

// The system HWND backdrop fills a rectangle even when SetWindowRgn is rounded.
// A composition visual owns both backdrop sampling and its geometry clip.
internal sealed class ClippedBackdrop : IDisposable
{
    private static nint _dispatcherQueue;
    private readonly nint _hwnd;
    private readonly nint _owner;
    private readonly Compositor _compositor;
    private readonly DesktopWindowTarget _target;
    private readonly SpriteVisual _visual;
    private readonly CompositionRoundedRectangleGeometry _geometry;
    private readonly CompositionEffectFactory _factory;
    private readonly CompositionEffectBrush _brush;
    private readonly CompositionBackdropBrush _source;
    private readonly CompositionGeometricClip _clip;
    private bool _visible;
    internal float BlurAmount { get; private set; } = 24;
    internal nint Handle => _hwnd;

    internal ClippedBackdrop(nint hwnd)
    {
        _owner = hwnd;
        try
        {
        if (_dispatcherQueue == 0)
        {
            var options = new QueueOptions { Size = Marshal.SizeOf<QueueOptions>(), ThreadType = 2, ApartmentType = 2 };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _dispatcherQueue));
        }
        _compositor = new Compositor();
        using var blur = new GaussianBlurEffect
        {
            Name = "Frost", BlurAmount = 24, BorderMode = EffectBorderMode.Hard,
            Source = new CompositionEffectSourceParameter("Desktop")
        };
        _factory = _compositor.CreateEffectFactory(blur, ["Frost.BlurAmount"]);
        _brush = _factory.CreateBrush();
        _source = _compositor.CreateBackdropBrush();
        _brush.SetSourceParameter("Desktop", _source);
        _geometry = _compositor.CreateRoundedRectangleGeometry();
        _visual = _compositor.CreateSpriteVisual();
        _visual.Brush = _brush;
        _clip = _compositor.CreateGeometricClip(_geometry);
        _visual.Clip = _clip;
        // An owned picker and its backdrop must share an owner. Otherwise Windows
        // can place the main window between picker UI and glass when restoring it.
        nint windowOwner = Native.GetWindow(hwnd, 4); // GW_OWNER
        _hwnd = CreateWindowEx(0x082000A0, "STATIC", "澄桌 clipped backdrop", 0x80000000,
            0, 0, 1, 1, windowOwner, 0, 0, 0); // No redirection bitmap, no activation/taskbar, transparent to input.
        if (_hwnd == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var interop = _compositor.As<ICompositorDesktopInterop>();
        Marshal.ThrowExceptionForHR(interop.CreateDesktopWindowTarget(_hwnd, false, out var pointer));
        try { _target = MarshalInspectable<DesktopWindowTarget>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
        _target.Root = _visual;
        }
        catch { Dispose(); throw; }
    }

    internal void Update(int width, int height, float radius, bool visible)
    {
        _visual.Size = _geometry.Size = new Vector2(width, height);
        _geometry.CornerRadius = new Vector2(radius);
        SetTransparency((System.Windows.Application.Current as App)?.Transparency ?? 72);
        _visual.IsVisible = visible;
        if (!visible) { Hide(); return; }
        _visible = true;
        nint region = radius > 0
            ? Native.CreateRoundRectRgn(0, 0, width + 1, height + 1, (int)(radius * 2), (int)(radius * 2))
            : Native.CreateRectRgn(0, 0, width, height);
        if (Native.SetWindowRgn(_hwnd, region, true) == 0) Native.DeleteObject(region);
        SyncPosition();
    }

    internal void SyncPosition()
    {
        if (!_visible || !Native.GetWindowRect(_owner, out var bounds)) return;
        Native.SetWindowPos(_hwnd, _owner, bounds.Left, bounds.Top,
            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x50); // Behind UI, show, no activation.
    }

    internal void Hide() { _visible = false; ShowWindow(_hwnd, 0); }

    internal void SetTransparency(double value)
    {
        float clarity = (float)Math.Clamp(value / 100, 0, 1);
        BlurAmount = .35f + 32 * MathF.Pow(1 - clarity, .8f);
        _brush.Properties.InsertScalar("Frost.BlurAmount", BlurAmount);
    }

    public void Dispose()
    {
        if (_target is not null) _target.Root = null;
        _visual?.Dispose(); _clip?.Dispose(); _geometry?.Dispose(); _brush?.Dispose(); _factory?.Dispose(); _source?.Dispose();
        _target?.Dispose(); _compositor?.Dispose();
        if (_hwnd != 0) DestroyWindow(_hwnd);
    }

    [StructLayout(LayoutKind.Sequential)] private struct QueueOptions { public int Size, ThreadType, ApartmentType; }
    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(QueueOptions options, out nint controller);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(
        uint exStyle, string className, string title, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        [PreserveSig] int CreateDesktopWindowTarget(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out nint target);
        [PreserveSig] int EnsureOnThread(uint threadId);
    }
}
