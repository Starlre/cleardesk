using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace ClearDesk.GlassWindow;

internal static class DockVerifier
{
    internal static async Task RunAsync(MainWindow launcher, DockWindow dock, LauncherViewModel model, string output, Action<bool, string> check)
    {
        double left = dock.Left, top = dock.Top, scale = dock.DockScale;
        try
        {
            ((MenuItem)dock.FindName("ZoomInItem")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Task.Delay(150);
            check(dock.Width > 620 && dock.Height > 88 && dock.SurfaceRadius > 40,
                "Dock zoom-in menu enlarges full window, icons and matching glass contour");
            Native.GetWindowRect(new WindowInteropHelper(dock).Handle, out var ui);
            Native.GetWindowRect((nint)dock.Report!.BackdropHandle, out var glass);
            check(ui.Equals(glass), "Resized Dock glass remains aligned with window bounds");
            SmokeVerifier.Capture(dock, Path.Combine(output, "dock-enlarged.png"));
            ((MenuItem)dock.FindName("ZoomOutItem")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(Math.Abs(dock.DockScale - scale) < 0.001, "Dock zoom-out menu reverses zoom-in");
            dock.SetScale(99, false);
            check(dock.DockScale <= DockWindow.MaximumScale, "Dock scaling enforces upper bound");
            dock.SetScale(-99, false);
            await Task.Delay(150);
            check(dock.DockScale == DockWindow.MinimumScale, "Dock scaling enforces lower bound");
            SmokeVerifier.Capture(dock, Path.Combine(output, "dock-small.png"));
            dock.MoveTo(-1e6, 1e6, false);
            var work = SystemParameters.WorkArea;
            check(dock.Left >= work.Left && dock.Top + dock.Height <= work.Bottom + 0.01,
                "Dock movement clamps offscreen coordinates to reachable work area");
        }
        finally { dock.SetScale(scale, false); dock.MoveTo(left, top, false); }

        string root = Path.Combine(output, "dock-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "dock-layout.json");
        var saved = new DockWindow(launcher, model, path) { Left = left, Top = top };
        saved.SetScale(1.2);
        saved.MoveTo(left + 20, top - 30);
        var layout = DockLayoutStore.Read(path)!;
        var restored = new DockWindow(launcher, model, path);
        try
        {
            restored.RestoreLayout();
            check(restored.Left == layout.Left && restored.Top == layout.Top && restored.DockScale == layout.Scale,
                "Dock position and scale survive JSON save and window reconstruction");
            await File.WriteAllTextAsync(path, "{invalid");
            check(DockLayoutStore.Read(path) is null, "Corrupt Dock layout safely falls back to defaults");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new DockLayout(-1e6, 1e6, 100)));
            restored.RestoreLayout();
            check(restored.DockScale <= DockWindow.MaximumScale && restored.Left >= SystemParameters.WorkArea.Left,
                "Restored invalid Dock size and coordinates are bounded");
            check(!DockLayoutStore.Save(root, new DockLayout(left, top, scale)), "Dock layout write failure is reported");
        }
        finally { saved.Close(); restored.Close(); }
    }
}
