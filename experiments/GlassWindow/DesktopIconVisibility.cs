using System.Runtime.InteropServices;

namespace ClearDesk.GlassWindow;

internal sealed record DesktopIconState(bool Available, bool Hidden, string? Error = null);

internal static class DesktopIconVisibility
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static DesktopIconState Read()
    {
        var (_, icons) = FindDesktop();
        return icons == 0 ? new(false, false, "暂时无法访问 Windows 桌面，请等待资源管理器恢复后重试。")
            : new(true, !IsWindowVisible(icons));
    }

    internal static async Task<DesktopIconState> SetHiddenAsync(bool hidden)
    {
        await Gate.WaitAsync();
        try
        {
            return await Task.Run(async () =>
            {
                var (view, icons) = FindDesktop();
                if (icons == 0) return Read();
                if (!IsWindowVisible(icons) == hidden) return new DesktopIconState(true, hidden);
                // Explorer's "View > Show desktop icons" command updates its own persisted preference.
                // Never hide WorkerW/Progman: they also host wallpaper and desktop input.
                bool delivered = SendMessageTimeout(view, 0x0111, 0x7402, 0, 0x0002, 1500, out _) != 0;
                DesktopIconState state = Read();
                for (int attempt = 0; state.Available && state.Hidden != hidden && attempt < 10; attempt++)
                { await Task.Delay(50); state = Read(); }
                if (!state.Available || state.Hidden == hidden) return state;
                return state with { Error = delivered
                    ? "Windows 未能改变桌面图标显示状态，请稍后重试。"
                    : "资源管理器暂时未响应，请稍后重试。" };
            });
        }
        finally { Gate.Release(); }
    }

    private static (nint View, nint Icons) FindDesktop()
    {
        nint view = FindWindowEx(FindWindow("Progman", null), 0, "SHELLDLL_DefView", null);
        if (view == 0)
        {
            EnumWindows((window, _) =>
            {
                view = FindWindowEx(window, 0, "SHELLDLL_DefView", null);
                return view == 0;
            }, 0);
        }
        nint icons = view == 0 ? 0 : FindWindowEx(view, 0, "SysListView32", "FolderView");
        if (view != 0 && icons == 0) icons = FindWindowEx(view, 0, "SysListView32", null);
        return (view, icons);
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam,
        uint flags, uint timeout, out nuint result);
}
