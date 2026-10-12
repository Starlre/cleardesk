using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ClearDesk.GlassWindow;

internal sealed class TrayIcon : IDisposable
{
    internal const int CallbackMessage = 0x8001;
    private static readonly Guid AppId = new("e0b79c67-9498-46d7-9d82-8e79207093fb");
    private readonly HwndSource _source;
    private readonly Action _show;
    private readonly DispatcherTimer _retry;
    private readonly uint _taskbarCreated;
    private NotifyIconData _data;
    private bool _version4;
    private bool _disposed;
    internal bool IsRegistered { get; private set; }
    internal ContextMenu Menu { get; } = new();

    internal TrayIcon(Window window, Action show, Action hide, Action exit, bool verification)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;
        _show = show;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "ClearDesk.ico");
        nint icon = LoadImage(0, path, 1, GetSystemMetrics(49), GetSystemMetrics(50), 0x10);
        if (icon == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取澄桌托盘图标");
        _data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _source.Handle, Id = 1,
            Flags = 1 | 2 | 4 | 0x20 | 0x80, Callback = CallbackMessage, Icon = icon,
            Tip = "澄桌（ClearDesk）", Info = "", InfoTitle = "", Guid = verification ? Guid.NewGuid() : AppId
        };
        var showItem = new MenuItem { Header = "显示应用面板" };
        var hideItem = new MenuItem { Header = "隐藏全部窗口" };
        var exitItem = new MenuItem { Header = "退出澄桌" };
        showItem.Click += (_, _) => show();
        hideItem.Click += (_, _) => hide();
        exitItem.Click += (_, _) => exit();
        Menu.Items.Add(showItem); Menu.Items.Add(hideItem); Menu.Items.Add(new Separator()); Menu.Items.Add(exitItem);
        Menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(Menu) is HwndSource popup) Native.SetForegroundWindow(popup.Handle);
            Menu.Focus();
        };
        Menu.Closed += (_, _) => { if (!_source.IsDisposed) Native.SendMessage(_data.Window, 0, 0, 0); };
        _retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _retry.Tick += (_, _) => { if (Register()) _retry.Stop(); };
        _source.AddHook(WindowProc);
        if (!Register())
        {
            // Explorer may not be ready at login. Keep the UI usable and retry registration.
            _retry.Start();
        }
    }

    private bool Register()
    {
        if (_disposed) return false;
        _data.Version = 0;
        IsRegistered = ShellNotifyIcon(0, ref _data);
        if (IsRegistered)
        {
            _data.Version = 4;
            _version4 = ShellNotifyIcon(4, ref _data);
        }
        return IsRegistered;
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (_taskbarCreated != 0 && (uint)message == _taskbarCreated)
        {
            // Explorer restarts remove all notification icons. Re-register with the same GUID.
            IsRegistered = false;
            if (!Register()) _retry.Start();
        }
        if (message != CallbackMessage) return 0;
        handled = true;
        int notification = _version4 ? (int)(lParam.ToInt64() & 0xFFFF) : (int)lParam;
        if (notification is 0x400 or 0x401 || (!_version4 && notification == 0x202)) _show();
        else if (notification == 0x7B || (!_version4 && notification == 0x205)) OpenMenu();
        return 0;
    }

    private void OpenMenu()
    {
        Menu.IsOpen = false;
        Native.GetCursorPos(out var point);
        var dpi = _source.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var position = dpi.Transform(new Point(point.X, point.Y));
        Menu.Placement = PlacementMode.AbsolutePoint;
        Menu.HorizontalOffset = position.X; Menu.VerticalOffset = position.Y;
        Menu.IsOpen = true;
    }

    internal bool TryGetBounds(out Native.Rect bounds)
    {
        var id = new NotifyIconIdentifier
        { Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Window = _data.Window, Id = _data.Id, Guid = _data.Guid };
        return ShellNotifyIconGetRect(ref id, out bounds) == 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _retry.Stop(); Menu.IsOpen = false;
        if (!_source.IsDisposed) _source.RemoveHook(WindowProc);
        if (IsRegistered) ShellNotifyIcon(2, ref _data);
        IsRegistered = false;
        if (_data.Icon != 0) { DestroyIcon(_data.Icon); _data.Icon = 0; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier { public uint Size; public nint Window; public uint Id; public Guid Guid; }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")]
    private static extern int ShellNotifyIconGetRect(ref NotifyIconIdentifier id, out Native.Rect bounds);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
}
