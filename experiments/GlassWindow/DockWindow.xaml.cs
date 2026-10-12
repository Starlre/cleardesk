using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ClearDesk.GlassWindow;

public partial class DockWindow : SurfaceWindow
{
    private readonly MainWindow _launcher;
    private readonly LauncherViewModel _viewModel;
    private readonly string? _layoutFile;
    private bool _moving, _pendingToggle;
    private Point _pointerStart, _windowStart;
    private double _dragDpi, _resizeStart, _resizePointerX, _resizeDpi;
    internal const double MinimumScale = 0.65, MaximumScale = 1.6;
    internal double DockScale { get; private set; } = 1;
    public override double SurfaceRadius => 40 * DockScale;
    public DockWindow(MainWindow launcher, LauncherViewModel viewModel, string? layoutFile = null)
    {
        IsDock = true; _launcher = launcher; _viewModel = viewModel; _layoutFile = layoutFile;
        InitializeComponent(); DataContext = viewModel;
        SetResourceReference(ForegroundProperty, "TextBrush");
        LostMouseCapture += (_, _) => { if ((_moving || _pendingToggle) && Mouse.Captured != this) FinishMove(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible && (_moving || _pendingToggle)) FinishMove(); };
    }
    internal void RestoreLayout()
    {
        if (DockLayoutStore.Read(_layoutFile) is not { } layout) return;
        SetScale(layout.Scale, false);
        MoveTo(layout.Left, layout.Top, false);
    }
    internal void SetScale(double scale, bool save = true)
    {
        if (!double.IsFinite(scale)) return;
        var work = SystemParameters.WorkArea;
        double maximum = Math.Max(MinimumScale, Math.Min(MaximumScale, (work.Width - 24) / 620));
        DockScale = Math.Clamp(scale, MinimumScale, maximum);
        Width = 620 * DockScale; Height = 88 * DockScale;
        MoveTo(Left, Top, false);
        UpdateContour();
        if (save) SaveLayout();
    }
    internal void MoveTo(double left, double top, bool save = true)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top)) return;
        // Keep the complete Dock reachable on the currently supported primary work area.
        var work = SystemParameters.WorkArea;
        Left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - Width));
        Top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - Height));
        if (save) SaveLayout();
    }
    private void SaveLayout()
    {
        if (!DockLayoutStore.Save(_layoutFile, new DockLayout(Left, Top, DockScale)))
            _viewModel.Status = "Dock 位置与大小保存失败，请检查目录权限";
    }
    private void OnDockPointerDown(object sender, MouseButtonEventArgs e)
    {
        bool fromToggle = false;
        for (DependencyObject? source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
        {
            if (source is Thumb) return;
            if (source is Button button)
            {
                if (button != ToggleLauncherButton) return;
                fromToggle = true; break;
            }
        }
        Native.GetCursorPos(out var point);
        _pointerStart = new Point(point.X, point.Y); _windowStart = new Point(Left, Top);
        _dragDpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        bool captured = Mouse.Capture(this, CaptureMode.SubTree);
        _pendingToggle = captured && fromToggle;
        _moving = captured && !fromToggle;
        e.Handled = true;
    }
    private void OnDockPointerMove(object sender, MouseEventArgs e)
    {
        if (!_moving && !_pendingToggle) return;
        if (e.LeftButton != MouseButtonState.Pressed) { FinishMove(); return; }
        Native.GetCursorPos(out var point);
        double dx = (point.X - _pointerStart.X) / _dragDpi, dy = (point.Y - _pointerStart.Y) / _dragDpi;
        if (_pendingToggle)
        {
            if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance) return;
            _pendingToggle = false; _moving = true;
        }
        MoveTo(_windowStart.X + dx, _windowStart.Y + dy, false);
        e.Handled = true;
    }
    private void OnDockPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!_moving && !_pendingToggle) return;
        var point = e.GetPosition(ToggleLauncherButton);
        bool click = _pendingToggle && point.X >= 0 && point.Y >= 0
            && point.X <= ToggleLauncherButton.ActualWidth && point.Y <= ToggleLauncherButton.ActualHeight;
        FinishMove(); e.Handled = true;
        if (click) ToggleLauncherButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private void FinishMove()
    {
        bool moved = _moving;
        _moving = _pendingToggle = false;
        if (Mouse.Captured == this) Mouse.Capture(null);
        if (moved) SaveLayout();
    }
    private void OnResizeStarted(object sender, DragStartedEventArgs e)
    {
        _resizeStart = DockScale;
        Native.GetCursorPos(out var point); _resizePointerX = point.X;
        _resizeDpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
    }
    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        // The grip moves as the window grows; use screen displacement rather than grip-relative deltas.
        Native.GetCursorPos(out var point);
        SetScale(_resizeStart + (point.X - _resizePointerX) / _resizeDpi / 620, false);
    }
    private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    { if (e.Canceled) SetScale(_resizeStart, false); SaveLayout(); }
    private void OnDockWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetScale(DockScale + Math.Sign(e.Delta) * 0.1); e.Handled = true;
    }
    private void OnZoomIn(object sender, RoutedEventArgs e) => SetScale(DockScale + 0.1);
    private void OnZoomOut(object sender, RoutedEventArgs e) => SetScale(DockScale - 0.1);
    private void OnResetScale(object sender, RoutedEventArgs e) => SetScale(1);
    private void OnMenuOpened(object sender, RoutedEventArgs e) => ScaleInfo.Header = $"尺寸：{DockScale:P0}";
    private void OnMoveLeft(object sender, RoutedEventArgs e) => MoveToEdge(0);
    private void OnMoveCenter(object sender, RoutedEventArgs e) => MoveToEdge(0.5);
    private void OnMoveRight(object sender, RoutedEventArgs e) => MoveToEdge(1);
    private void MoveToEdge(double position)
    {
        var work = SystemParameters.WorkArea;
        MoveTo(work.Left + 12 + (work.Width - Width - 24) * position, work.Bottom - Height - 12);
    }
    private void OnToggleLauncher(object sender, RoutedEventArgs e)
    {
        if (_launcher.IsVisible && _launcher.WindowState != WindowState.Minimized) _launcher.Hide();
        else
        {
            _launcher.Show();
            _launcher.WindowState = WindowState.Normal;
            if (_launcher.IconPool is { IsVisible: true } pool) pool.Activate();
            else _launcher.Activate();
        }
    }
    private void OnLaunch(object sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: EntryViewModel entry }) _viewModel.Launch(entry); }
}
