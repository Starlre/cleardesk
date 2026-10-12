using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClearDesk.GlassWindow;

public enum BackdropMode { Glass, Solid }

public sealed record SurfaceReport(string Window, string OsVersion, bool Layered, bool NoActivate,
    int BackdropResult, int FrameResult, int CornerResult, uint Dpi, bool RegionApplied)
{
    public string ClearColor { get; init; } = "";
    public int RenderTier { get; init; }
    public bool HighContrast { get; init; }
    public string? FallbackReason { get; init; }
    public double CornerRadius { get; init; }
    public string BackdropImplementation { get; init; } = "";
    public long BackdropHandle { get; init; }
}

public class SurfaceWindow : Window
{
    private HwndSource? _source;
    private ClippedBackdrop? _backdrop;
    protected bool IsDock { get; set; }
    public BackdropMode Material { get; private set; } = BackdropMode.Glass;
    public bool UseCustomContour { get; set; } = true;
    public virtual double SurfaceRadius => IsDock ? 40 : 32;
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
        LocationChanged += (_, _) => _backdrop?.SyncPosition();
        ContentRendered += (_, _) => UpdateContour();
        IsVisibleChanged += (_, _) => { if (IsVisible) UpdateContour(); else _backdrop?.Hide(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) _backdrop?.Hide(); else UpdateContour(); };
        Closed += (_, _) => { _source?.RemoveHook(WindowProc); _backdrop?.Dispose(); };
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
        var margins = new Native.Margins(0, 0, 0, 0);
        var frame = Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int dark = App.IsLightTheme ? 0 : 1;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        int corner = UseCustomContour ? 1 : 2; // DWMWCP_DONOTROUND / DWMWCP_ROUND
        var round = Native.DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
        int borderColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE: WPF draws the only edge.
        Native.DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));
        int backdrop = 1; // Disable the rectangular system background.
        var material = Native.DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
        bool transparent = ReadTransparencySetting();
        string? fallback = mode == BackdropMode.Solid ? "User selected solid" :
            material < 0 ? "Could not disable rectangular system backdrop" : SystemParameters.HighContrast ? "High contrast" :
            !transparent ? "System transparency disabled" : null;
        if (fallback is null)
        {
            try { _backdrop ??= new ClippedBackdrop(hwnd); }
            catch (Exception exception) when (exception is COMException or System.IO.FileNotFoundException
                or System.ComponentModel.Win32Exception or TypeInitializationException)
            {
                material = exception.HResult;
                fallback = "Composition unavailable: " + exception.Message;
            }
        }
        App.ApplyTheme(App.IsLightTheme, fallback is not null);
        long style = Native.GetWindowExStyle(hwnd);
        Report = new SurfaceReport(Title, Environment.OSVersion.Version.ToString(),
            (style & 0x80000) != 0, (style & 0x08000000) != 0,
            material, frame, round, Native.GetDpiForWindow(hwnd), false)
        {
            ClearColor = _source?.CompositionTarget?.BackgroundColor.ToString() ?? "unavailable",
            RenderTier = RenderCapability.Tier >> 16,
            HighContrast = SystemParameters.HighContrast,
            FallbackReason = fallback,
            CornerRadius = SurfaceRadius,
            BackdropImplementation = fallback is null ? "Windows Composition / clipped Gaussian blur" : "Solid fallback",
            BackdropHandle = _backdrop?.Handle.ToInt64() ?? 0
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
        if (Native.GetWindowRect(hwnd, out var bounds))
        {
            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            uint dpi = Native.GetDpiForWindow(hwnd);
            int diameter = Math.Min(Math.Min(width, height), (int)Math.Round(2 * SurfaceRadius * dpi / 96.0));
            nint region = UseCustomContour
                ? Native.CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter)
                : Native.CreateRectRgn(0, 0, width, height);
            if (region != 0)
            {
                var blur = new Native.BlurBehind { Flags = 3, Enabled = 1, Region = region };
                Native.DwmEnableBlurBehindWindow(hwnd, ref blur); // Preserve client alpha without a rectangular frame.
                bool regionSet = Native.SetWindowRgn(hwnd, region, true) != 0;
                applied = regionSet && UseCustomContour;
                if (!regionSet) Native.DeleteObject(region); // The OS owns a region only after success.
            }
            _backdrop?.Update(width, height, UseCustomContour ? diameter / 2f : 0,
                IsVisible && WindowState != WindowState.Minimized && Report.FallbackReason is null);
        }
        else Native.SetWindowRgn(hwnd, 0, true);
        Report = Report with { RegionApplied = applied, Dpi = Native.GetDpiForWindow(hwnd), CornerResult = round, CornerRadius = SurfaceRadius };
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (IsDock && message == 0x21) { handled = true; return 3; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        if (message == 0x47) _backdrop?.SyncPosition(); // Keep the backdrop directly behind UI after movement/z-order changes.
        if (message == 0x5) // WM_SIZE: restored bounds settle after the state-change handlers.
        {
            if (wParam == 1) _backdrop?.Hide();
            else Dispatcher.BeginInvoke(UpdateContour);
        }
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
    [StructLayout(LayoutKind.Sequential)] internal struct BlurBehind { public int Flags, Enabled; public nint Region; public int Transition; }
    [StructLayout(LayoutKind.Sequential)] internal struct CursorPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput
    { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input
    { public uint Type; public MouseInput Mouse; }

    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
    [DllImport("dwmapi.dll")] internal static extern int DwmEnableBlurBehindWindow(nint hwnd, ref BlurBehind blur);
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
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AllowSetForegroundWindow(uint processId);
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
