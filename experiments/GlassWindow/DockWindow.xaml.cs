using System.Windows;
using System.Windows.Controls;

namespace ClearDesk.GlassWindow;

public partial class DockWindow : SurfaceWindow
{
    private readonly MainWindow _launcher;
    private readonly LauncherViewModel _viewModel;
    public DockWindow(MainWindow launcher, LauncherViewModel viewModel)
    {
        IsDock = true; _launcher = launcher; _viewModel = viewModel;
        InitializeComponent(); DataContext = viewModel;
        SetResourceReference(ForegroundProperty, "TextBrush");
    }
    private void OnToggleLauncher(object sender, RoutedEventArgs e)
    {
        if (_launcher.IsVisible) _launcher.Hide();
        else { _launcher.Show(); _launcher.Activate(); }
    }
    private void OnLaunch(object sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: EntryViewModel entry }) _viewModel.Launch(entry); }
}
