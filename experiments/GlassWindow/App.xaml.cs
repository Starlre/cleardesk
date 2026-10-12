using System.IO;
using System.Windows;
using System.Windows.Media;

namespace ClearDesk.GlassWindow;

public partial class App : Application
{
    private readonly CancellationTokenSource _lifetime = new();
    private SingleInstance? _instance;
    private bool _pendingShow;
    public DockWindow Dock { get; private set; } = null!;
    public static bool IsLightTheme { get; private set; }
    internal double Transparency { get; private set; } = 72;
    private string? _appearanceFile;
    private bool _appearanceDirty;
    private nint _previousForeground;
    private Window? _verificationBackdrop;
    internal TrayIcon? Tray { get; private set; }
    internal bool IsExiting { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        int probeIndex = Array.IndexOf(e.Args, "--instance-probe");
        if (probeIndex >= 0 && probeIndex + 1 < e.Args.Length)
        {
            _instance = await SingleInstance.AcquireAsync(() => { }, e.Args[probeIndex + 1]);
            ExitApplication(_instance is null ? 0 : 2);
            return;
        }
        bool verify = e.Args.Contains("--smoke-test");
        if (verify) _previousForeground = Native.GetForegroundWindow();
        string output = Option(e.Args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "GlassWindow");
        string stateRoot = Option(e.Args, "--state-root") ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "GlassWindow");
        void Stage(string stage)
        {
            try { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "startup.log"), stage); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        Stage("Acquiring instance");
        try
        {
            if (!verify)
            {
                _instance = await SingleInstance.AcquireAsync(() =>
                {
                    if (Tray is null) _pendingShow = true;
                    else ShowLauncher();
                });
                if (_instance is null) { ExitApplication(); return; }
            }
            _appearanceFile = verify ? null : Path.Combine(stateRoot, "appearance-settings.json");
            Stage("Loading preferences");
            Transparency = AppearanceSettingsStore.Read(_appearanceFile).Transparency;
            // The old state.json contains an automatically populated catalog, not explicit choices.
            // Keep it untouched; panel-state.json stores only manually selected entries.
            var viewModel = new LauncherViewModel(verify ? null : Path.Combine(stateRoot, "panel-state.json"),
                verify ? null : Path.Combine(stateRoot, "dock-preferences.json"));
            if (verify) viewModel.LaunchOverride = _ => true; // Verification must never launch external applications.
            Stage("Creating windows");
            var launcher = new MainWindow(viewModel, _lifetime.Token) { ShowActivated = !verify };
            MainWindow = launcher;
            Dock = new DockWindow(launcher, viewModel, verify ? null : Path.Combine(stateRoot, "dock-layout.json"));
            Rect work = SystemParameters.WorkArea;
            launcher.Width = Math.Min(1040, work.Width - 40);
            launcher.Height = Math.Min(640, work.Height - 150);
            launcher.Left = work.Left + (work.Width - launcher.Width) / 2;
            launcher.Top = work.Top + Math.Max(16, (work.Height - launcher.Height - 112) / 2);
            Dock.Left = work.Left + (work.Width - Dock.Width) / 2;
            Dock.Top = Math.Min(work.Bottom - Dock.Height - 12, launcher.Top + launcher.Height + 20);
            Dock.RestoreLayout();
            Window? fixture = verify ? SmokeVerifier.CreateBackdrop(launcher, work) : null;
            _verificationBackdrop = fixture;
            if (fixture is not null) launcher.Topmost = true; // Keep the fixture visible when the verifier hides UI for pixel comparison.
            fixture?.Show();
            Stage("Showing launcher");
            launcher.Show(); Dock.Show();
            Stage("Registering tray");
            Tray = new TrayIcon(launcher, ShowLauncher, HideToTray, () => ExitApplication(), verify);
            if (_pendingShow) { _pendingShow = false; ShowLauncher(); }
            if (fixture is not null)
                Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(fixture).Handle,
                    new System.Windows.Interop.WindowInteropHelper(launcher).Handle, 0, 0, 0, 0, 0x13);
            await viewModel.InitializeAsync(await CatalogService.LoadAsync(_lifetime.Token), _lifetime.Token);
            Stage("Ready");
            if (!Tray.IsRegistered) viewModel.Status = "系统托盘暂时不可用，正在重试；请保留此窗口。";
            if (verify)
            {
                await SmokeVerifier.RunAsync(launcher, Dock, viewModel, output, e.Args.Contains("--input-test"), e.Args.Contains("--desktop-icon-test"));
                RestoreForeground();
                ((System.Windows.Controls.MenuItem)Tray.Menu.Items[3]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            }
        }
        catch (OperationCanceledException) { ExitApplication(); }
        catch (Exception exception)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "error.txt"), exception.ToString());
            if (!verify) MessageBox.Show(exception.Message, "澄桌 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            RestoreForeground();
            ExitApplication(1);
        }
    }
    internal void ShowLauncher()
    {
        Dock.Show();
        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        if (MainWindow is MainWindow { IconPool: { IsVisible: true } pool }) pool.Activate();
        else MainWindow.Activate();
    }
    internal void HideToTray()
    {
        if (Tray is not { IsRegistered: true })
        {
            ShowLauncher();
            if (MainWindow.DataContext is LauncherViewModel model)
                model.Status = "系统托盘暂时不可用，正在重试；请保留此窗口。";
            return;
        }
        if (MainWindow is MainWindow launcher) launcher.HidePanel();
        Dock.Hide();
    }
    private void ExitApplication(int code = 0)
    {
        IsExiting = true;
        // Remove verification overlays before releasing graphics resources, even after an exception.
        _verificationBackdrop?.Hide();
        foreach (var window in Windows.Cast<Window>().ToArray()) window.Hide();
        Shutdown(code);
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e) { IsExiting = true; base.OnSessionEnding(e); }
    protected override void OnExit(ExitEventArgs e)
    {
        SaveAppearance();
        Tray?.Dispose(); Tray = null;
        _instance?.Dispose(); _instance = null;
        _lifetime.Cancel(); _lifetime.Dispose(); base.OnExit(e);
    }
    private void RestoreForeground()
    {
        if (_previousForeground == 0) return;
        nint current = Native.GetForegroundWindow();
        if (Windows.Cast<Window>().Any(window => new System.Windows.Interop.WindowInteropHelper(window).Handle == current))
            Native.SetForegroundWindow(_previousForeground);
    }
    private static string? Option(string[] args, string name)
    { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null; }

    internal void SetTransparency(double value)
    {
        if (!double.IsFinite(value)) return;
        Transparency = Math.Clamp(value, 0, 100);
        _appearanceDirty = true;
        bool solid = MainWindow is SurfaceWindow surface
            && (surface.Material == BackdropMode.Solid || surface.Report?.FallbackReason is not null);
        ApplyTheme(IsLightTheme, solid);
        foreach (var window in Windows.OfType<SurfaceWindow>()) window.UpdateGlassTransparency(Transparency);
    }

    internal bool SaveAppearance()
    {
        if (!_appearanceDirty) return true;
        bool saved = AppearanceSettingsStore.Save(_appearanceFile, new AppearanceSettings(Transparency));
        if (saved) _appearanceDirty = false;
        return saved;
    }

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
        if (!solid && Current is App app)
        {
            var tint = ((SolidColorBrush)Current.Resources["SurfaceTint"]).Color;
            tint.A = (byte)Math.Round(255 * (1 - app.Transparency / 100));
            Current.Resources["SurfaceTint"] = new SolidColorBrush(tint);
        }
        Current.Resources["SurfaceEdge"] = new LinearGradientBrush(new GradientStopCollection
        {
            new((Color)ColorConverter.ConvertFromString(light ? "#90FFFFFF" : "#80FFFFFF"), 0),
            new((Color)ColorConverter.ConvertFromString(light ? "#30000000" : "#24FFFFFF"), 0.5),
            new((Color)ColorConverter.ConvertFromString(light ? "#45FFFFFF" : "#48FFFFFF"), 1)
        }, new Point(0, 0), new Point(0, 1));
    }
}
