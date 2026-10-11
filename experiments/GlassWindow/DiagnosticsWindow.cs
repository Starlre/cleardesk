using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace GlassDesk.GlassWindow;

public sealed class DiagnosticsWindow : Window
{
    public DiagnosticsWindow(SurfaceWindow launcher, SurfaceWindow dock)
    {
        Title = "Glass Desk · 窗口验证"; Width = 560; Height = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White; Foreground = System.Windows.Media.Brushes.Black;
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "材质与窗口行为", FontSize = 20, Margin = new Thickness(0, 0, 0, 20) });
        var material = new ComboBox { ItemsSource = Enum.GetValues<BackdropMode>(), SelectedItem = launcher.Material, Margin = new Thickness(0, 0, 0, 16) };
        var theme = new CheckBox { Content = "浅色外观", IsChecked = App.IsLightTheme, Margin = new Thickness(0, 0, 0, 16) };
        var contour = new CheckBox { Content = "大圆角 / 胶囊裁切（需比较阴影变化）", IsChecked = launcher.UseCustomContour, Margin = new Thickness(0, 0, 0, 16) };
        var report = new TextBox { IsReadOnly = true, AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11, Height = 270, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(material); root.Children.Add(theme); root.Children.Add(contour); root.Children.Add(report);
        root.Children.Add(new TextBlock { Text = "接口返回成功只说明调用被接受，视觉效果仍需人工确认。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        Content = root;
        void Update()
        {
            var selected = (BackdropMode)material.SelectedItem;
            App.ApplyTheme(theme.IsChecked == true, selected == BackdropMode.Solid);
            launcher.UseCustomContour = dock.UseCustomContour = contour.IsChecked == true;
            launcher.ApplyMaterial(selected); dock.ApplyMaterial(selected);
            report.Text = JsonSerializer.Serialize(new[] { launcher.Report, dock.Report }, new JsonSerializerOptions { WriteIndented = true });
        }
        material.SelectionChanged += (_, _) => Update();
        theme.Checked += (_, _) => Update(); theme.Unchecked += (_, _) => Update();
        contour.Checked += (_, _) => Update(); contour.Unchecked += (_, _) => Update();
        Update();
    }
}
