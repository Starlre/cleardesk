using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClearDesk.GlassWindow;

internal static class SmokeVerifier
{
    internal static Window CreateBackdrop(MainWindow launcher, Rect work)
    {
        var shapes = new DrawingGroup();
        shapes.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(67, 72, 88)), null, new RectangleGeometry(new Rect(0, 0, 1200, 900))));
        shapes.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(138, 157, 186)), null,
            Geometry.Parse("M400,0 L760,0 L260,900 L0,900 Z")));
        shapes.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(41, 43, 56)), null,
            Geometry.Parse("M900,0 L1200,0 L750,900 L450,900 Z")));
        shapes.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(199, 192, 176)), null,
            Geometry.Parse("M1200,180 L1200,900 L780,900 Z")));
        return new Window
        {
            Title = "澄桌 · Controlled backdrop", Width = launcher.Width + 80,
            Height = Math.Min(work.Height - 8, launcher.Height + 144),
            Left = launcher.Left - 40, Top = Math.Max(work.Top, launcher.Top - 16),
            ShowActivated = false, ShowInTaskbar = false, Topmost = true, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            Background = new DrawingBrush(shapes) { Stretch = Stretch.Fill }
        };
    }

    internal static async Task RunAsync(MainWindow launcher, DockWindow dock, LauncherViewModel model, string output, bool inputTest)
    {
        Directory.CreateDirectory(output);
        File.Delete(Path.Combine(output, "error.txt"));
        var checks = new List<string>();
        void Check(bool result, string name)
        { if (!result) throw new InvalidOperationException("Smoke check failed: " + name); checks.Add(name); }
        void CheckContour(SurfaceWindow window, string stage)
        {
            nint handle = new WindowInteropHelper(window).Handle;
            nint region = Native.CreateRectRgn(0, 0, 0, 0);
            try
            {
                Check(region != 0 && Native.GetWindowRgn(handle, region) > 0
                    && Native.GetWindowRect(handle, out _), $"{window.Title}: native contour available {stage}");
                Native.GetWindowRect(handle, out var bounds);
                int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
                int radius = (int)Math.Round(window.SurfaceRadius * Native.GetDpiForWindow(handle) / 96.0);
                // A system 8 DIP corner passes a (2,2) check. These probes distinguish
                // the intended larger arc and catch a replaced region after hide/show.
                int cutout = radius / 4, inside = radius / 2;
                Check(!Native.PtInRegion(region, cutout, cutout)
                    && !Native.PtInRegion(region, width - 1 - cutout, cutout)
                    && !Native.PtInRegion(region, cutout, height - 1 - cutout)
                    && !Native.PtInRegion(region, width - 1 - cutout, height - 1 - cutout)
                    && Native.PtInRegion(region, inside, inside)
                    && Native.PtInRegion(region, width / 2, height / 2),
                    $"{window.Title}: four cutouts match {window.SurfaceRadius} DIP arc {stage}");
            }
            finally { if (region != 0) Native.DeleteObject(region); }
        }

        Check(model.Entries.Count >= 3, "Real Shell entries loaded");
        Check(ReferenceEquals(((System.Windows.Input.KeyBinding)launcher.InputBindings[0]).Command, model.EscapeCommand), "Escape binding uses live cancel/hide command");
        Check(model.Entries.All(entry => entry.Icon is Freezable { IsFrozen: true }), "Shell icons frozen for STA-to-UI transfer");
        Check(launcher.Report is { Layered: false, BackdropResult: 0, FallbackReason: null, BackdropHandle: not 0 }, "Launcher uses non-layered UI over clipped Composition blur");
        Check(dock.Report is { Layered: false, NoActivate: true, BackdropResult: 0, FallbackReason: null, BackdropHandle: not 0 }, "Dock uses no-activate UI over clipped Composition blur");
        Check(launcher.Report is { RegionApplied: true, CornerRadius: 32 }, "Launcher starts with a matching 32 DIP four-corner contour");
        CheckContour(launcher, "at startup");
        nint dockHandle = new WindowInteropHelper(dock).Handle;
        Check(Native.SendMessage(dockHandle, 0x21, 0, (nint)0x02010001) == 3, "Dock WM_MOUSEACTIVATE returns MA_NOACTIVATE");
        nint foreground = Native.GetForegroundWindow();
        dock.Hide();
        Check(!Native.IsWindowVisible((nint)dock.Report!.BackdropHandle), "Hiding Dock also hides its backing visual window");
        dock.Show();
        Check(Native.GetForegroundWindow() == foreground, "Showing Dock preserves foreground window");
        model.EscapeCommand.Execute(null);
        Check(!launcher.IsVisible && dock.IsVisible, "Escape hides only launcher and preserves Dock");
        Check(!Native.IsWindowVisible((nint)launcher.Report!.BackdropHandle), "Hiding launcher also hides its backing visual window");
        launcher.Show();
        double previousLeft = launcher.Left, previousTop = launcher.Top;
        launcher.Left += 8; launcher.Top += 8;
        await Task.Delay(150);
        Native.GetWindowRect(new WindowInteropHelper(launcher).Handle, out var movedUi);
        Native.GetWindowRect((nint)launcher.Report!.BackdropHandle, out var movedBackdrop);
        Check(movedUi.Equals(movedBackdrop), "Moving launcher keeps Composition backdrop aligned to all four bounds");
        launcher.Left = previousLeft; launcher.Top = previousTop;

        var original = model.SnapshotOrder();
        model.SearchText = original[0].Name;
        Check(model.VisibleEntries.Count > 0 && model.VisibleEntries.All(entry => entry.Name.Contains(original[0].Name, StringComparison.CurrentCultureIgnoreCase)), "Name search returns matching entries");
        Check(!model.MoveVisible(model.VisibleEntries[0], 1), "Search view cannot change stored order");
        model.SearchText = "";
        Check(model.VisibleEntries.Count == Math.Min(LauncherViewModel.PageSize, original.Length), "Page capacity bounded");
        Check(model.MoveVisible(original[0], 2) && model.Entries[2] == original[0], "Reorder keeps master and visible sequence aligned");
        model.MoveVisible(original[0], 0);
        Check(model.Entries.SequenceEqual(original), "Reverse reorder restores exact order");
        model.MoveVisible(original[0], 3);
        model.RestoreOrder(original);
        Check(model.Entries.SequenceEqual(original), "Cancelled reorder restores snapshot");
        Check(!model.MoveVisible(original[0], -1) && !model.MoveVisible(original[0], 999), "Out-of-range reorder rejected");
        Check(model.Add([original[0].Entry]) == 0 && model.Entries.Count == original.Length, "Duplicate import preserves catalog size");
        if (model.PageCount > 1)
        {
            model.NextPageCommand.Execute(null);
            Check(model.VisibleEntries[0] == original[LauncherViewModel.PageSize], "Next page uses stable global order");
            model.PreviousPageCommand.Execute(null);
        }
        string statePath = Path.Combine(output, "verified-state.json");
        var state = new LauncherViewModel(statePath);
        state.Load(original.Select(entry => entry.Entry).ToArray());
        state.MoveVisible(state.Entries[0], 2); state.CommitOrder();
        var restored = new LauncherViewModel(statePath);
        restored.Load(original.Select(entry => entry.Entry).ToArray());
        Check(restored.Entries.Select(entry => entry.Entry.SourcePath).SequenceEqual(state.Entries.Select(entry => entry.Entry.SourcePath)), "Committed order survives JSON round trip");
        string brokenState = Path.Combine(output, "corrupt-state.json");
        await File.WriteAllTextAsync(brokenState, "{invalid");
        var corrupt = new LauncherViewModel(brokenState);
        corrupt.Load(original.Select(entry => entry.Entry).ToArray());
        Check(corrupt.Entries.Select(entry => entry.Entry.SourcePath).SequenceEqual(original.Select(entry => entry.Entry.SourcePath))
            && corrupt.Status.Contains("无法读取"), "Corrupt state restores default order with visible feedback");
        var unwritable = new LauncherViewModel(output); // Existing directory cannot be replaced with a state file.
        unwritable.Load(original.Select(entry => entry.Entry).ToArray());
        unwritable.Add([original[0].Entry]);
        Check(unwritable.Status.Contains("保存失败"), "Save failure feedback is preserved after import");
        File.Delete(output + ".tmp");
        if (inputTest) await VerifyPointerDragAsync(launcher, model, checks);

        model.Status = $"{model.Entries.Count} 个入口";
        launcher.Activate();
        await Task.Delay(450);
        Check(Native.GetWindow(new WindowInteropHelper(launcher).Handle, 2) == (nint)launcher.Report!.BackdropHandle,
            "After activation the backdrop is directly behind launcher UI");
        CheckContour(launcher, "after hide/show and activation");
        CheckContour(dock, "after hide/show");
        await VerifyTransparentCornersAsync(launcher, output, "dark", checks);
        await VerifyTransparentCornersAsync(dock, output, "dark", checks);
        int rectangularBackdrop = 3;
        Native.DwmSetWindowAttribute(new WindowInteropHelper(launcher).Handle, 38, ref rectangularBackdrop, sizeof(int));
        await Task.Delay(180);
        bool regressionDetected = false;
        try { await VerifyTransparentCornersAsync(launcher, output, "rectangular-regression", checks); }
        catch (InvalidOperationException exception) when (exception.Message.Contains("glass still leaks")) { regressionDetected = true; }
        finally { launcher.ApplyMaterial(BackdropMode.Glass); }
        Check(regressionDetected, "Pixel check detects deliberately restored rectangular system backdrop");
        launcher.Activate();
        await Task.Delay(150);
        Capture(launcher, Path.Combine(output, "launcher-dark.png"));
        Capture(launcher, Path.Combine(output, "launcher-dark-context.png"), 24);
        Capture(dock, Path.Combine(output, "dock-system-corners.png"));
        var originalReports = new[] { launcher.Report, dock.Report };
        App.ApplyTheme(true);
        launcher.ApplyMaterial(BackdropMode.Glass); dock.ApplyMaterial(BackdropMode.Glass);
        await Task.Delay(300);
        CheckContour(launcher, "after light theme change");
        CheckContour(dock, "after light theme change");
        await VerifyTransparentCornersAsync(launcher, output, "light", checks);
        await VerifyTransparentCornersAsync(dock, output, "light", checks);
        Capture(launcher, Path.Combine(output, "launcher-light.png"));
        launcher.UseCustomContour = dock.UseCustomContour = true;
        launcher.UpdateContour(); dock.UpdateContour();
        Check(launcher.Report?.RegionApplied == true && dock.Report?.RegionApplied == true, "Explicit rounded regions accepted");
        await Task.Delay(250);
        Capture(dock, Path.Combine(output, "dock-capsule.png"));
        App.ApplyTheme(false, true);
        launcher.ApplyMaterial(BackdropMode.Solid); dock.ApplyMaterial(BackdropMode.Solid);
        await Task.Delay(250);
        CheckContour(launcher, "in solid fallback");
        await VerifyTransparentCornersAsync(launcher, output, "solid", checks);
        Capture(launcher, Path.Combine(output, "launcher-solid.png"));
        await File.WriteAllTextAsync(Path.Combine(output, "smoke-report.json"), JsonSerializer.Serialize(new
        {
            Status = "passed", OsVersion = Environment.OSVersion.Version.ToString(),
            Runtime = Environment.Version.ToString(), EntryCount = model.Entries.Count,
            Checks = checks, InitialSurfaces = originalReports, PointerInputTest = inputTest,
            VisualVerification = "Screenshots captured; arbitrary background motion, external mouse drag, mixed DPI and shadows require manual review."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task VerifyPointerDragAsync(MainWindow launcher, LauncherViewModel model, List<string> checks)
    {
        Native.GetCursorPos(out var previous);
        var original = model.SnapshotOrder();
        int launchAttempts = 0;
        model.LaunchOverride = _ => launchAttempts++;
        void Button(uint flags)
        {
            if (Native.SendInput(1, [new Native.Input { Mouse = new Native.MouseInput { Flags = flags } }],
                System.Runtime.InteropServices.Marshal.SizeOf<Native.Input>()) != 1)
                throw new InvalidOperationException("Pointer injection unavailable.");
        }
        async Task Move(Point from, Point to)
        {
            for (int step = 1; step <= 8; step++)
            {
                Native.SetCursorPos((int)(from.X + (to.X - from.X) * step / 8), (int)(from.Y + (to.Y - from.Y) * step / 8));
                await Task.Delay(45);
            }
        }
        try
        {
            launcher.Activate();
            Native.SetWindowPos(new WindowInteropHelper(launcher).Handle, (nint)(-1), 0, 0, 0, 0, 0x13);
            await Task.Delay(350);
            Point start = launcher.TileCenterOnScreen(0), target = launcher.TileCenterOnScreen(2);
            Native.SetCursorPos((int)start.X, (int)start.Y); await Task.Delay(80);
            Button(0x2); await Task.Delay(80); await Move(start, target); Button(0x4); await Task.Delay(250);
            if (model.Entries[2] != original[0] || launchAttempts != 0)
            {
                Capture(launcher, Path.Combine(Environment.CurrentDirectory, "artifacts", "pointer-failure.png"), 24);
                throw new InvalidOperationException("Real pointer drag did not reorder correctly or attempted to launch a target.");
            }
            checks.Add("Injected pointer drag reorders real UI without launching target");
            model.RestoreOrder(original); launcher.UpdateLayout(); await Task.Delay(250);
            start = launcher.TileCenterOnScreen(0); target = launcher.TileCenterOnScreen(3);
            Native.SetCursorPos((int)start.X, (int)start.Y); await Task.Delay(80);
            Button(0x2); await Task.Delay(80); await Move(start, target);
            model.EscapeCommand.Execute(null); Button(0x4); await Task.Delay(200);
            if (!model.Entries.SequenceEqual(original) || !launcher.IsVisible || launchAttempts != 0)
                throw new InvalidOperationException("Escape failed to cancel real pointer drag.");
            checks.Add("Escape cancels injected pointer drag and keeps launcher visible");
        }
        finally
        {
            Button(0x4);
            Native.SetCursorPos(previous.X, previous.Y);
            model.LaunchOverride = _ => { };
            model.RestoreOrder(original);
        }
    }

    private static async Task VerifyTransparentCornersAsync(SurfaceWindow window, string output, string theme, List<string> checks)
    {
        string name = ReferenceEquals(window, Application.Current.MainWindow) ? "launcher" : "dock";
        var visible = Capture(window, Path.Combine(output, $"{name}-{theme}-visible.png"));
        window.Hide();
        await Task.Delay(180);
        var hidden = Capture(window, Path.Combine(output, $"{name}-{theme}-background.png"));
        window.Show();
        await Task.Delay(180);
        int stride = visible.PixelWidth * 4;
        var front = new byte[stride * visible.PixelHeight];
        var back = new byte[front.Length];
        new FormatConvertedBitmap(visible, PixelFormats.Bgra32, null, 0).CopyPixels(front, stride, 0);
        new FormatConvertedBitmap(hidden, PixelFormats.Bgra32, null, 0).CopyPixels(back, stride, 0);
        int radius = (int)Math.Round(window.SurfaceRadius * Native.GetDpiForWindow(new WindowInteropHelper(window).Handle) / 96.0);
        int maxDifference = 0, sampleCount = 0;
        for (int corner = 0; corner < 4; corner++)
        for (int y = Math.Max(2, radius / 8); y <= radius / 4; y += 2)
        for (int x = Math.Max(2, radius / 8); x <= radius / 4; x += 2)
        {
            int px = (corner & 1) == 0 ? x : visible.PixelWidth - 1 - x;
            int py = (corner & 2) == 0 ? y : visible.PixelHeight - 1 - y;
            int index = py * stride + px * 4;
            for (int channel = 0; channel < 3; channel++)
                maxDifference = Math.Max(maxDifference, Math.Abs(front[index + channel] - back[index + channel]));
            sampleCount++;
        }
        if (maxDifference > 2)
            throw new InvalidOperationException($"{window.Title} {theme}: rounded cutout changes background pixels by {maxDifference}; glass still leaks outside its arc.");
        checks.Add($"{window.Title} {theme}: {sampleCount} corner pixels match hidden background (max RGB delta {maxDifference})");
    }

    private static BitmapSource Capture(Window window, string destination, int padding = 0)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (!Native.GetWindowRect(hwnd, out var rect)) throw new InvalidOperationException("Window bounds unavailable.");
        int width = rect.Right - rect.Left + padding * 2, height = rect.Bottom - rect.Top + padding * 2;
        nint source = Native.GetDC(0), target = 0, bitmap = 0, previous = 0;
        try
        {
            target = Native.CreateCompatibleDC(source); bitmap = Native.CreateCompatibleBitmap(source, width, height);
            if (target == 0 || bitmap == 0) throw new InvalidOperationException("Screenshot buffer unavailable.");
            previous = Native.SelectObject(target, bitmap);
            if (!Native.BitBlt(target, 0, 0, width, height, source, rect.Left - padding, rect.Top - padding, 0x40CC0020))
                throw new InvalidOperationException("Window screenshot capture failed.");
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(destination); encoder.Save(file);
            image.Freeze();
            return image;
        }
        finally
        {
            if (previous != 0) Native.SelectObject(target, previous);
            if (bitmap != 0) Native.DeleteObject(bitmap);
            if (target != 0) Native.DeleteDC(target);
            if (source != 0) Native.ReleaseDC(0, source);
        }
    }
}
