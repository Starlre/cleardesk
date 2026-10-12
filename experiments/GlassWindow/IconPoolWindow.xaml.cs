using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClearDesk.GlassWindow;

public partial class IconPoolWindow : SurfaceWindow
{
    private readonly LauncherViewModel _launcher;
    private readonly CancellationToken _token;
    internal IconPoolViewModel Model { get; }
    public IconPoolWindow(LauncherViewModel launcher, CancellationToken token)
    {
        _launcher = launcher; _token = token;
        Model = new IconPoolViewModel(launcher);
        InitializeComponent(); DataContext = Model;
        SetResourceReference(ForegroundProperty, "TextBrush");
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 48);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 48);
        Loaded += (_, _) => PoolSearchBox.Focus();
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }
    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
            if (source is Button) return;
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void OnAddSelected(object sender, RoutedEventArgs e) { if (Model.AddSelected()) Close(); }
    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "导入到图标池", Multiselect = true, DereferenceLinks = false,
            Filter = "应用与快捷方式|*.lnk;*.url;*.exe|所有文件|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var entries = await CatalogService.ImportAsync(dialog.FileNames, _token);
            if (!IsVisible) return;
            _launcher.UpdatePool(entries); Model.SelectEntries(entries);
            Model.Status = "已导入图标池，点击「添加到面板」完成添加。";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Model.Status = $"导入失败：{exception.Message}"; }
    }
}
