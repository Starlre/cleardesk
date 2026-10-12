using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClearDesk.GlassWindow;

public sealed class SettingsWindow : Window
{
    internal Slider TransparencySlider { get; }
    internal TextBlock TransparencyValue { get; }
    internal ComboBox DockSortSelector { get; }

    public SettingsWindow(SurfaceWindow launcher, SurfaceWindow dock)
    {
        var app = (App)Application.Current;
        Title = "澄桌 · 设置"; Width = 480; Height = Math.Min(580, SystemParameters.WorkArea.Height - 48); MinWidth = 400; MinHeight = 340;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 250));
        Foreground = new SolidColorBrush(Color.FromRgb(24, 30, 40));
        FontSize = 14;
        var root = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(new TextBlock { Text = "设置", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 24) });
        var label = new DockPanel();
        TransparencyValue = new TextBlock { Text = $"{app.Transparency:0}%", HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(TransparencyValue, Dock.Right); label.Children.Add(TransparencyValue);
        label.Children.Add(new TextBlock { Text = "透明度", FontWeight = FontWeights.SemiBold });
        root.Children.Add(label);
        TransparencySlider = new Slider
        {
            Minimum = 0, Maximum = 100, Value = app.Transparency, SmallChange = 1, LargeChange = 10,
            TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true,
            Height = 32, Margin = new Thickness(0, 8, 0, 0)
        };
        AutomationProperties.SetName(TransparencySlider, "玻璃面板透明度");
        root.Children.Add(TransparencySlider);
        var ends = new DockPanel();
        var more = new TextBlock { Text = "更透明", HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(more, Dock.Right); ends.Children.Add(more);
        ends.Children.Add(new TextBlock { Text = "更实色" }); root.Children.Add(ends);
        root.Children.Add(new TextBlock
        {
            Text = "主面板与 Dock 同步调整，图标和文字保持清晰。\n下次启动沿用当前透明度。",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 12, 0, 12)
        });
        var saveStatus = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        root.Children.Add(saveStatus);
        var theme = new CheckBox { Content = "浅色外观", IsChecked = App.IsLightTheme, Margin = new Thickness(0, 16, 0, 20) };
        root.Children.Add(theme);

        var model = (LauncherViewModel)launcher.DataContext;
        root.Children.Add(new TextBlock { Text = "Dock 应用排序", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        DockSortSelector = new ComboBox { SelectedValuePath = "Tag", MinHeight = 32 };
        DockSortSelector.Items.Add(new ComboBoxItem { Content = "固定排序", Tag = DockSortMode.Fixed });
        DockSortSelector.Items.Add(new ComboBoxItem { Content = "最近使用", Tag = DockSortMode.Recent });
        DockSortSelector.SelectedValue = model.DockSortMode;
        AutomationProperties.SetName(DockSortSelector, "Dock 应用排序");
        root.Children.Add(DockSortSelector);
        var sortHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 8) };
        root.Children.Add(sortHint);
        var sortSaveStatus = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        root.Children.Add(sortSaveStatus);
        void UpdateSortHint() => sortHint.Text = model.DockSortMode == DockSortMode.Recent
            ? "通过澄桌最近启动的应用排在前面，未使用的按面板顺序补齐，最多显示 8 个。"
            : "按主面板顺序显示前 8 个应用，使用应用不会改变顺序。";
        DockSortSelector.SelectionChanged += (_, _) =>
        {
            if (DockSortSelector.SelectedValue is not DockSortMode mode) return;
            sortSaveStatus.Text = model.SetDockSortMode(mode) ? "" : "Dock 排序保存失败，请检查目录权限。";
            UpdateSortHint();
        };
        UpdateSortHint();

        var advanced = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        root.Children.Add(new Expander { Header = "高级选项", Content = advanced });
        advanced.Children.Add(new TextBlock { Text = "面板材质", Margin = new Thickness(0, 0, 0, 8) });
        var material = new ComboBox { SelectedValuePath = "Tag", Margin = new Thickness(0, 0, 0, 16) };
        material.Items.Add(new ComboBoxItem { Content = "玻璃", Tag = BackdropMode.Glass });
        material.Items.Add(new ComboBoxItem { Content = "实色", Tag = BackdropMode.Solid });
        material.SelectedValue = launcher.Material;
        AutomationProperties.SetName(material, "面板材质");
        advanced.Children.Add(material);
        var contour = new CheckBox { Content = "圆角裁切", IsChecked = launcher.UseCustomContour, Margin = new Thickness(0, 0, 0, 16) };
        advanced.Children.Add(contour);
        var unavailable = new TextBlock { Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        advanced.Children.Add(unavailable);
        var report = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), FontSize = 11,
            Height = 220, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        advanced.Children.Add(new Expander { Header = "窗口诊断", Content = report });
        void Update()
        {
            var selected = (BackdropMode)material.SelectedValue;
            App.ApplyTheme(theme.IsChecked == true, selected == BackdropMode.Solid);
            launcher.UseCustomContour = dock.UseCustomContour = contour.IsChecked == true;
            launcher.ApplyMaterial(selected); dock.ApplyMaterial(selected);
            TransparencySlider.IsEnabled = selected == BackdropMode.Glass && launcher.Report?.FallbackReason is null;
            unavailable.Text = TransparencySlider.IsEnabled ? "" : "当前使用实色背景，切换到玻璃材质并开启系统透明效果后可调整透明度。";
            report.Text = JsonSerializer.Serialize(new[] { launcher.Report, dock.Report }, new JsonSerializerOptions { WriteIndented = true });
        }
        var saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        void Save()
        {
            saveTimer.Stop();
            saveStatus.Text = app.SaveAppearance() ? "" : "透明度保存失败，请检查目录权限。";
        }
        saveTimer.Tick += (_, _) => Save();
        TransparencySlider.ValueChanged += (_, _) =>
        {
            app.SetTransparency(TransparencySlider.Value);
            TransparencyValue.Text = $"{app.Transparency:0}%";
            saveTimer.Stop(); saveTimer.Start();
        };
        Closed += (_, _) => Save();
        material.SelectionChanged += (_, _) => Update();
        theme.Checked += (_, _) => Update(); theme.Unchecked += (_, _) => Update();
        contour.Checked += (_, _) => Update(); contour.Unchecked += (_, _) => Update();
        Update();
    }
}
