using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace ClearDesk.GlassWindow;

public partial class App : Application
{
    private readonly CancellationTokenSource _lifetime = new();
    public DockWindow Dock { get; private set; } = null!;
    public static bool IsLightTheme { get; private set; }
    private nint _previousForeground;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool verify = e.Args.Contains("--smoke-test");
        if (verify) _previousForeground = Native.GetForegroundWindow();
        string output = Option(e.Args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "GlassWindow");
        string stateRoot = Option(e.Args, "--state-root") ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "GlassWindow");
        try
        {
            var viewModel = new LauncherViewModel(verify ? null : Path.Combine(stateRoot, "state.json"));
            if (verify) viewModel.LaunchOverride = _ => { }; // Verification must never launch external applications.
            var launcher = new MainWindow(viewModel, _lifetime.Token) { ShowActivated = !verify };
            MainWindow = launcher;
            Dock = new DockWindow(launcher, viewModel);
            Rect work = SystemParameters.WorkArea;
            launcher.Width = Math.Min(1040, work.Width - 40);
            launcher.Height = Math.Min(640, work.Height - 150);
            launcher.Left = work.Left + (work.Width - launcher.Width) / 2;
            launcher.Top = work.Top + Math.Max(16, (work.Height - launcher.Height - 112) / 2);
            Dock.Left = work.Left + (work.Width - Dock.Width) / 2;
            Dock.Top = Math.Min(work.Bottom - Dock.Height - 12, launcher.Top + launcher.Height + 20);
            Window? fixture = verify ? SmokeVerifier.CreateBackdrop(launcher, work) : null;
            if (fixture is not null) launcher.Topmost = true; // Keep the fixture visible when the verifier hides UI for pixel comparison.
            fixture?.Show();
            launcher.Show(); Dock.Show();
            if (fixture is not null)
                Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(fixture).Handle,
                    new System.Windows.Interop.WindowInteropHelper(launcher).Handle, 0, 0, 0, 0, 0x13);
            var entries = (await CatalogService.LoadAsync(_lifetime.Token)).ToList();
            string stateFile = Path.Combine(stateRoot, "state.json");
            if (!verify && File.Exists(stateFile))
            {
                try
                {
                    var paths = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(stateFile, _lifetime.Token)) ?? [];
                    var existing = entries.Select(entry => entry.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    entries.AddRange(await CatalogService.ImportAsync(paths.Where(path => !existing.Contains(path)), _lifetime.Token));
                }
                catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { }
            }
            viewModel.Load(entries);
            if (verify)
            {
                await SmokeVerifier.RunAsync(launcher, Dock, viewModel, output, e.Args.Contains("--input-test"));
                RestoreForeground();
                Shutdown(0);
            }
        }
        catch (OperationCanceledException) { Shutdown(); }
        catch (Exception exception)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "error.txt"), exception.ToString());
            if (!verify) MessageBox.Show(exception.Message, "澄桌 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            RestoreForeground();
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { _lifetime.Cancel(); _lifetime.Dispose(); base.OnExit(e); }
    private void RestoreForeground()
    {
        if (_previousForeground == 0) return;
        nint current = Native.GetForegroundWindow();
        if (Windows.Cast<Window>().Any(window => new System.Windows.Interop.WindowInteropHelper(window).Handle == current))
            Native.SetForegroundWindow(_previousForeground);
    }
    private static string? Option(string[] args, string name)
    { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null; }

    public static void ApplyTheme(bool light, bool solid = false)
    {
        IsLightTheme = light;
        var colors = light
            ? new Dictionary<string, string> { ["SurfaceTint"] = solid ? "#FFF0F2F6" : "#70F0F3F8",
                ["TextBrush"] = "#151923", ["MutedBrush"] = "#3C4351", ["ControlFill"] = "#18000000", ["HoverFill"] = "#25000000", ["AccentBrush"] = "#1759AF" }
            : new Dictionary<string, string> { ["SurfaceTint"] = solid ? "#FF20232B" : "#48202530",
                ["TextBrush"] = "#FAFAFC", ["MutedBrush"] = "#CDD0D9", ["ControlFill"] = "#18FFFFFF", ["HoverFill"] = "#26FFFFFF", ["AccentBrush"] = "#B8D6FF" };
        foreach (var pair in colors)
            Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
        Current.Resources["SurfaceEdge"] = new LinearGradientBrush(new GradientStopCollection
        {
            new((Color)ColorConverter.ConvertFromString(light ? "#90FFFFFF" : "#80FFFFFF"), 0),
            new((Color)ColorConverter.ConvertFromString(light ? "#30000000" : "#24FFFFFF"), 0.5),
            new((Color)ColorConverter.ConvertFromString(light ? "#45FFFFFF" : "#48FFFFFF"), 1)
        }, new Point(0, 0), new Point(0, 1));
    }
}
