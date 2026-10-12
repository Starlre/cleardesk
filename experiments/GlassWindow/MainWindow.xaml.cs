using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClearDesk.GlassWindow;

public partial class MainWindow : SurfaceWindow
{
    private readonly LauncherViewModel _viewModel;
    private readonly CancellationToken _token;
    private Button? _pendingButton;
    private EntryViewModel? _pendingEntry;
    private EntryViewModel? _draggedEntry;
    private EntryViewModel[]? _originalOrder;
    private Point _start;
    private DragImageAdorner? _adorner;
    private AdornerLayer? _adornerLayer;
    private SettingsWindow? _settings;
    internal SettingsWindow? Settings => _settings;
    internal IconPoolWindow? IconPool { get; private set; }

    public MainWindow(LauncherViewModel viewModel, CancellationToken token)
    {
        _viewModel = viewModel; _token = token;
        viewModel.EscapeCommand = new RelayCommand(() => { if (_draggedEntry is not null) EndDrag(false); else ((App)Application.Current).HideToTray(); });
        InitializeComponent();
        DataContext = viewModel;
        SetResourceReference(ForegroundProperty, "TextBrush");
        StateChanged += (_, _) =>
        {
            SyncIconPoolVisibility();
            if (WindowState == WindowState.Minimized) Dispatcher.BeginInvoke(() =>
            {
                if (WindowState == WindowState.Minimized) HidePanel();
            });
        };
        IsVisibleChanged += (_, _) => SyncIconPoolVisibility();
        Closing += (_, e) =>
        {
            var app = (App)Application.Current;
            if (!app.IsExiting) { e.Cancel = true; app.HideToTray(); }
        };
        PreviewMouseMove += OnPointerMove;
        PreviewMouseLeftButtonUp += OnPointerUp;
        LostMouseCapture += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, this) && Mouse.Captured != this && _draggedEntry is not null) EndDrag(false);
        };
        Deactivated += (_, _) => { if (_draggedEntry is not null) EndDrag(false); };
        Tiles.SizeChanged += (_, _) =>
        {
            if (FindChild<AnimatedTilePanel>(Tiles) is { } panel)
            { panel.RowHeight = Math.Max(72, Math.Min(108, Tiles.ActualHeight / 4)); panel.InvalidateMeasure(); }
        };
    }

    private void OnLaunch(object sender, RoutedEventArgs e)
    {
        if (_draggedEntry is null && sender is Button { DataContext: EntryViewModel entry }) _viewModel.Launch(entry);
    }
    internal Point TileCenterOnScreen(int index)
    {
        Tiles.UpdateLayout();
        var container = (UIElement)Tiles.ItemContainerGenerator.ContainerFromIndex(index);
        return container.PointToScreen(new Point(container.RenderSize.Width / 2, container.RenderSize.Height / 2));
    }
    private void OnClose(object sender, RoutedEventArgs e) => ((App)Application.Current).HideToTray();
    private void OnMinimize(object sender, RoutedEventArgs e) => HidePanel();
    internal void HidePanel() { EndDrag(false); Hide(); }
    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParent<Button>(source) is not null) return;
        if (e.LeftButton == MouseButtonState.Pressed) { EndDrag(false); DragMove(); }
    }
    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow(this, ((App)Application.Current).Dock) { Owner = this, Topmost = Topmost };
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }
        else { _settings.Show(); _settings.Activate(); }
    }
    private void OnAdd(object sender, RoutedEventArgs e) => OpenIconPool();
    private void SyncIconPoolVisibility()
    {
        if (_settings is not null)
        {
            if (IsVisible && WindowState != WindowState.Minimized) _settings.Show();
            else _settings.Hide();
        }
        if (IconPool is null) return;
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            if (!IconPool.IsVisible) IconPool.Show();
        }
        else IconPool.Hide();
    }
    private void OpenIconPool()
    {
        if (IconPool is null)
        {
            IconPool = new IconPoolWindow(_viewModel, _token) { Owner = this, Topmost = Topmost };
            IconPool.Closed += (_, _) => IconPool = null;
            IconPool.Show();
        }
        else { IconPool.Model.Refresh(); IconPool.Activate(); }
    }
    private void OnExternalDragOver(object sender, DragEventArgs e)
    { e.Effects = _viewModel.IsCatalogReady && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void OnExternalDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!_viewModel.IsCatalogReady) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await ImportAsync(paths);
    }
    private async Task ImportAsync(IEnumerable<string> paths)
    {
        try
        {
            var entries = await CatalogService.ImportAsync(paths, _token);
            _viewModel.UpdatePool(entries);
            OpenIconPool();
            IconPool?.Model.SelectEntries(entries);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _viewModel.Status = $"导入失败：{exception.Message}"; }
    }
    private void OnMoveEarlier(object sender, RoutedEventArgs e) => MoveFromMenu(sender, -1);
    private void OnMoveLater(object sender, RoutedEventArgs e) => MoveFromMenu(sender, 1);
    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: Button { DataContext: EntryViewModel entry } } })
        { _viewModel.Remove(entry); IconPool?.Model.Refresh(); }
    }
    private void MoveFromMenu(object sender, int direction)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: Button { DataContext: EntryViewModel entry } } })
            _viewModel.MoveBy(entry, direction);
    }
    private void OnTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.CanReorder || sender is not Button { DataContext: EntryViewModel entry } button) return;
        _pendingButton = button; _pendingEntry = entry; _start = e.GetPosition(Root);
    }
    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        Point point = e.GetPosition(Root);
        if (_draggedEntry is null && _pendingButton is not null && _pendingEntry is not null)
        {
            if (Math.Abs(point.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(point.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var dpi = VisualTreeHelper.GetDpi(_pendingButton);
            var bitmap = new RenderTargetBitmap(Math.Max(1, (int)(_pendingButton.ActualWidth * dpi.DpiScaleX)),
                Math.Max(1, (int)(_pendingButton.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(_pendingButton); bitmap.Freeze();
            _originalOrder = _viewModel.SnapshotOrder(); _draggedEntry = _pendingEntry;
            _adornerLayer = AdornerLayer.GetAdornerLayer(Root);
            _adorner = new DragImageAdorner(Root, bitmap, new Size(_pendingButton.ActualWidth, _pendingButton.ActualHeight));
            _adornerLayer?.Add(_adorner);
            _draggedEntry.IsDragging = true;
            Mouse.Capture(this, CaptureMode.SubTree);
        }
        if (_draggedEntry is null) return;
        _adorner?.Move(point);
        if (FindChild<AnimatedTilePanel>(Tiles) is { } panel)
        {
            Point local = e.GetPosition(panel);
            if (local.X >= 0 && local.Y >= 0 && local.X < panel.ActualWidth && local.Y < panel.ActualHeight)
            {
                int index = (int)(local.Y / panel.RowHeight) * panel.Columns + (int)(local.X / (panel.ActualWidth / panel.Columns));
                _viewModel.MoveVisible(_draggedEntry, Math.Min(index, _viewModel.VisibleEntries.Count - 1));
            }
        }
        e.Handled = true;
    }
    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedEntry is not null) { EndDrag(true); e.Handled = true; }
        _pendingButton = null; _pendingEntry = null;
    }
    private void EndDrag(bool commit)
    {
        var dragged = _draggedEntry; _draggedEntry = null;
        _pendingButton = null; _pendingEntry = null;
        if (dragged is null) return;
        dragged.IsDragging = false;
        if (_adorner is not null) _adornerLayer?.Remove(_adorner);
        _adorner = null;
        Mouse.Capture(null);
        if (commit) _viewModel.CommitOrder();
        else if (_originalOrder is not null) _viewModel.RestoreOrder(_originalOrder);
        _originalOrder = null;
    }
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        while (child is not null)
        { if (child is T match) return match; child = VisualTreeHelper.GetParent(child); }
        return null;
    }
}

internal sealed class DragImageAdorner(UIElement parent, ImageSource image, Size size) : Adorner(parent)
{
    private Point _point;
    public void Move(Point point) { _point = point; IsHitTestVisible = false; InvalidateVisual(); }
    protected override void OnRender(DrawingContext context)
    {
        context.PushOpacity(0.9);
        context.DrawImage(image, new Rect(new Point(_point.X - size.Width / 2, _point.Y - size.Height / 2), size));
        context.Pop();
    }
}
