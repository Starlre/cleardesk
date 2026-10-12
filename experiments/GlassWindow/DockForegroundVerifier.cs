using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ClearDesk.GlassWindow;

internal static class DockForegroundVerifier
{
    internal static void ShowProbe(string[] arguments)
    {
        int index = Array.IndexOf(arguments, "--dock-focus-probe");
        string resultFile = arguments[index + 1];
        var window = new Window
        {
            Title = "澄桌前台切换验证", Width = 320, Height = 160, ShowInTaskbar = false,
            ShowActivated = true, Content = "仅用于检查进入其他应用时 Dock 自动隐藏。"
        };
        window.Show();
        Native.AllowSetForegroundWindow(uint.Parse(arguments[index + 2]));
        File.WriteAllText(resultFile, new WindowInteropHelper(window).Handle.ToInt64().ToString());
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
        timeout.Tick += (_, _) => { timeout.Stop(); Application.Current.Shutdown(); };
        timeout.Start();
    }

    internal static async Task RunAsync(MainWindow launcher, DockWindow dock, string output, Action<bool, string> check)
    {
        var app = (App)Application.Current;
        string resultFile = Path.Combine(output, "foreground-probe-handle.txt");
        File.Delete(resultFile);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add("--dock-focus-probe"); start.ArgumentList.Add(resultFile);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        using var probe = Process.Start(start) ?? throw new InvalidOperationException("Cannot start foreground probe");
        try
        {
            for (int attempt = 0; !File.Exists(resultFile) && attempt < 100; attempt++) await Task.Delay(50);
            check(File.Exists(resultFile), "Separate-process foreground window starts for Dock integration check");
            nint external = (nint)long.Parse(await File.ReadAllTextAsync(resultFile));
            Native.SetForegroundWindow(new WindowInteropHelper(launcher).Handle);
            launcher.Activate();
            await Task.Delay(200);
            app.StartDockVisibility();
            check(dock.IsVisible, $"Dock remains visible while ClearDesk is foreground (foreground={Native.GetForegroundWindow()}, launcher={new WindowInteropHelper(launcher).Handle})");
            Native.SetForegroundWindow(external);
            await Task.Delay(350);
            check(Native.GetForegroundWindow() == external && !dock.IsVisible
                && !Native.IsWindowVisible((nint)dock.Report!.BackdropHandle),
                "Real foreground event hides Dock and glass when another process becomes active");
            nint desktop = DockVisibilityController.GetShellWindow();
            bool desktopActivated = Native.SetForegroundWindow(desktop);
            await Task.Delay(50);
            nint desktopForeground = Native.GetForegroundWindow(); // Explorer may activate its WorkerW, not Progman.
            await Task.Delay(300);
            check(DockVisibilityController.IsDesktopWindow(desktopForeground)
                && Native.GetForegroundWindow() == desktopForeground && dock.IsVisible
                && Native.IsWindowVisible((nint)dock.Report!.BackdropHandle),
                $"Returning to real Windows desktop restores Dock and glass without stealing focus (activated={desktopActivated}, foreground={Native.GetForegroundWindow()}, desktop={desktop}, visible={dock.IsVisible}, glass={Native.IsWindowVisible((nint)dock.Report!.BackdropHandle)})");
            app.HideToTray();
            Native.SetForegroundWindow(external); await Task.Delay(180);
            Native.SetForegroundWindow(desktop); await Task.Delay(350);
            check(!dock.IsVisible, "Returning to desktop never reverses manual hide-to-tray");
            app.ShowLauncher(); await Task.Delay(250);
            check(dock.IsVisible, "Explicit tray restore resumes automatic Dock visibility");
            var settings = new SettingsWindow(launcher, dock) { Owner = launcher };
            try
            {
                settings.Show(); settings.Activate(); await Task.Delay(250);
                check(dock.IsVisible, "ClearDesk settings is treated as own UI and keeps Dock visible");
            }
            finally { settings.Close(); }
            launcher.Activate(); await Task.Delay(200);
            ((System.Windows.Controls.Button)launcher.FindName("AddButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            try
            {
                launcher.IconPool!.Activate(); await Task.Delay(250);
                check(dock.IsVisible, "ClearDesk icon pool keeps Dock visible");
            }
            finally { launcher.IconPool?.Close(); }
            app.StopDockVisibility();
            Native.SetForegroundWindow(external); await Task.Delay(250);
            check(dock.IsVisible, "Disposing foreground listener stops automatic visibility changes");
        }
        finally
        {
            app.StopDockVisibility();
            if (!probe.HasExited)
            {
                probe.CloseMainWindow();
                for (int attempt = 0; !probe.HasExited && attempt < 20; attempt++) await Task.Delay(50);
                if (!probe.HasExited) probe.Kill();
            }
            launcher.Activate(); dock.Show();
        }
    }
}
