using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace ClearDesk.GlassWindow;

internal sealed class DockVisibilityController : IDisposable
{
    private readonly DockWindow _dock;
    private readonly WinEventCallback _callback;
    private readonly DispatcherTimer _settle;
    private nint _hook;
    private bool _requested = true, _eligible = true, _disposed;

    internal DockVisibilityController(DockWindow dock)
    {
        _dock = dock;
        // Coalesce foreground transitions (menus and activation can briefly report HWND 0).
        _settle = new DispatcherTimer(DispatcherPriority.Background, dock.Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(100) };
        _settle.Tick += (_, _) => { _settle.Stop(); Refresh(); };
        _callback = (_, _, _, _, _, _, _) =>
        {
            if (!_disposed) { _settle.Stop(); _settle.Start(); }
        };
        _hook = SetWinEventHook(3, 3, 0, _callback, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND / OUTOFCONTEXT
        if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法监听前台应用变化");
        Refresh();
    }

    internal void SetRequested(bool requested)
    {
        _requested = requested;
        Refresh();
    }

    internal void Refresh()
    {
        if (_disposed) return;
        nint foreground = Native.GetForegroundWindow();
        if (foreground != 0)
        {
            GetWindowThreadProcessId(foreground, out uint processId);
            string className = WindowClass(foreground);
            if (processId == Environment.ProcessId) _eligible = true; // Includes own dialogs and menus.
            else if (IsDesktopWindow(foreground)) _eligible = true;
            // Taskbar clicks must not reveal Dock over an application. Keep the previous decision.
            else if (className is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")) _eligible = false;
        }
        bool visible = _requested && _eligible;
        if (visible && !_dock.IsVisible) _dock.Show(); // WS_EX_NOACTIVATE preserves keyboard focus.
        else if (!visible && _dock.IsVisible) _dock.Hide(); // Also hides its Composition backdrop.
    }

    internal static bool IsDesktopWindow(nint window) => window != 0
        && (window == GetShellWindow() || WindowClass(window) is "Progman" or "WorkerW");

    private static string WindowClass(nint window)
    {
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settle.Stop();
        if (_hook != 0) { UnhookWinEvent(_hook); _hook = 0; }
        GC.KeepAlive(_callback);
    }

    private delegate void WinEventCallback(nint hook, uint eventType, nint window,
        int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module,
        WinEventCallback callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] internal static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder name, int length);
}
