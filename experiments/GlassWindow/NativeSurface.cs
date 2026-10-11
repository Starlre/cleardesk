using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace GlassDesk.GlassWindow;

public enum BackdropMode { Acrylic = 3, Mica = 2, Solid = 1 }

public sealed record SurfaceReport(string Window, string OsVersion, bool Layered, bool NoActivate,
    int BackdropResult, int FrameResult, int CornerResult, uint Dpi, bool RegionApplied)
{
    public string ClearColor { get; init; } = "";
    public int RenderTier { get; init; }
    public bool HighContrast { get; init; }
    public string? FallbackReason { get; init; }
    public double CornerRadius { get; init; }
}

public class SurfaceWindow : Window
{
    private HwndSource? _source;
    protected bool IsDock { get; set; }
    public BackdropMode Material { get; private set; } = BackdropMode.Acrylic;
    public bool UseCustomContour { get; set; } = true;
    public double SurfaceRadius => IsDock ? 40 : 32;
    public SurfaceReport? Report { get; private set; }
    public event EventHandler? SurfaceChanged;

    public SurfaceWindow()
    {
        // WindowChrome also manages HWND regions and can replace our contour on show.
        // These fixed-size, borderless windows use one region owner instead.
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(hwnd);
            _source?.AddHook(WindowProc);
            if (_source?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
            if (IsDock)
                Native.SetWindowExStyle(hwnd, Native.GetWindowExStyle(hwnd) | 0x08000000L | 0x80L);
            ApplyMaterial(Material);
        };
        SizeChanged += (_, _) => UpdateContour();
        ContentRendered += (_, _) => UpdateContour();
        IsVisibleChanged += (_, _) => { if (IsVisible) UpdateContour(); };
        Closed += (_, _) => _source?.RemoveHook(WindowProc);
    }

    public void ApplyMaterial(BackdropMode mode)
    {
        Material = mode;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        if (_source?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        int enabled = 1;
        Native.DwmSetWindowAttribute(hwnd, 17, ref enabled, sizeof(int));
        int nonClientRendering = 2;
        Native.DwmSetWindowAttribute(hwnd, 2, ref nonClientRendering, sizeof(int));
        var margins = new Native.Margins(-1, -1, -1, -1);
        var frame = Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int dark = App.IsLightTheme ? 0 : 1;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        int corner = UseCustomContour ? 1 : 2; // DWMWCP_DONOTROUND / DWMWCP_ROUND
        var round = Native.DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
        int borderColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE: WPF draws the only edge.
        Native.DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));
        int backdrop = (int)mode;
        var material = Native.DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
        bool transparent = ReadTransparencySetting();
        string? fallback = mode == BackdropMode.Solid ? "User selected solid" :
            material < 0 ? "System backdrop unavailable" : SystemParameters.HighContrast ? "High contrast" :
            !transparent ? "System transparency disabled" : null;
        if (fallback is not null) App.ApplyTheme(App.IsLightTheme, true);
        long style = Native.GetWindowExStyle(hwnd);
        Report = new SurfaceReport(Title, Environment.OSVersion.Version.ToString(),
            (style & 0x80000) != 0, (style & 0x08000000) != 0,
            material, frame, round, Native.GetDpiForWindow(hwnd), false)
        {
            ClearColor = _source?.CompositionTarget?.BackgroundColor.ToString() ?? "unavailable",
            RenderTier = RenderCapability.Tier >> 16,
            HighContrast = SystemParameters.HighContrast,
            FallbackReason = fallback,
            CornerRadius = SurfaceRadius
        };
        UpdateContour();
        SurfaceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateContour()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0 || Report is null) return;
        int corner = UseCustomContour ? 1 : 2;
        int round = Native.DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
        bool applied = false;
        if (UseCustomContour && Native.GetWindowRect(hwnd, out var bounds))
        {
            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            uint dpi = Native.GetDpiForWindow(hwnd);
            int diameter = Math.Min(Math.Min(width, height), (int)Math.Round(2 * SurfaceRadius * dpi / 96.0));
            nint region = Native.CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
            if (region != 0)
            {
                applied = Native.SetWindowRgn(hwnd, region, true) != 0;
                if (!applied) Native.DeleteObject(region); // The OS owns a region only after success.
            }
        }
        else Native.SetWindowRgn(hwnd, 0, true);
        Report = Report with { RegionApplied = applied, Dpi = Native.GetDpiForWindow(hwnd), CornerResult = round };
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (IsDock && message == 0x21) { handled = true; return 3; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        if (message == 0x2E0) Dispatcher.BeginInvoke(UpdateContour); // Rebuild physical contour after WPF applies a DPI change.
        if (message is 0x31E or 0x320 or 0x1A or 0x31A) Dispatcher.BeginInvoke(() =>
        {
            App.ApplyTheme(App.IsLightTheme, Material == BackdropMode.Solid);
            ApplyMaterial(Material);
        });
        return 0;
    }
    private static bool ReadTransparencySetting()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (System.Security.SecurityException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Margins(int left, int right, int top, int bottom)
    { public int Left = left, Right = right, Top = top, Bottom = bottom; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct CursorPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput
    { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input
    { public uint Type; public MouseInput Mouse; }

    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("user32.dll")] internal static extern int GetWindowRgn(nint hwnd, nint region);
    [DllImport("gdi32.dll")] internal static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] internal static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint hwnd, int index, int value);
    internal static long GetWindowExStyle(nint hwnd) => IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, -20).ToInt64() : GetWindowLong(hwnd, -20);
    internal static void SetWindowExStyle(nint hwnd, long value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr(hwnd, -20, (nint)value);
        else SetWindowLong(hwnd, -20, (int)value);
    }
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
}
