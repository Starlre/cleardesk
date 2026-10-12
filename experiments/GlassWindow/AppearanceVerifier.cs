using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClearDesk.GlassWindow;

internal static class AppearanceVerifier
{
    internal static async Task RunAsync(MainWindow launcher, DockWindow dock, string output, Action<bool, string> check)
    {
        var app = (App)Application.Current;
        double original = app.Transparency;
        byte Alpha() => ((SolidColorBrush)Application.Current.Resources["SurfaceTint"]).Color.A;
        void Open() => ((Button)launcher.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Open(); await Task.Delay(150);
            var settings = launcher.Settings!;
            check(settings.IsVisible && !settings.ShowInTaskbar && settings.TransparencySlider.Value == original,
                "Settings button opens transparency slider with current value and no taskbar entry");
            var range = (IRangeValueProvider)new SliderAutomationPeer(settings.TransparencySlider).GetPattern(PatternInterface.RangeValue);
            range.SetValue(20);
            check(Alpha() == 204 && launcher.Opacity == 1 && dock.Opacity == 1,
                "Accessible slider updates shared glass tint in real time while icons and text keep full opacity");
            range.SetValue(80);
            check(Alpha() == 51 && settings.TransparencyValue.Text == "80%",
                "Higher transparency reduces tint alpha and updates percentage readout");
            await Task.Delay(400);
            SmokeVerifier.Capture(settings, Path.Combine(output, "transparency-settings.png"));
            app.HideToTray();
            check(!settings.IsVisible && app.Transparency == 80, "Hiding to tray also hides settings and retains transparency");
            app.ShowLauncher(); await Task.Delay(150);
            check(settings.IsVisible && settings.TransparencySlider.Value == 80,
                "Tray restoration retains settings slider value");
            settings.Close(); Open();
            check(launcher.Settings!.TransparencySlider.Value == 80, "Reopening settings retains last transparency");
            launcher.Settings.Close();
            App.ApplyTheme(true);
            check(Alpha() == 51 && app.Transparency == 80, "Theme changes preserve chosen transparency");
            App.ApplyTheme(true, true);
            check(Alpha() == 255 && app.Transparency == 80, "Solid fallback stays opaque without overwriting transparency preference");
        }
        finally
        {
            launcher.Settings?.Close();
            app.SetTransparency(original); app.SaveAppearance(); App.ApplyTheme(false);
        }
        string path = Path.Combine(output, "appearance-fixture.json");
        check(AppearanceSettingsStore.Save(path, new AppearanceSettings(80))
            && AppearanceSettingsStore.Read(path).Transparency == 80,
            "Persisted transparency loads unchanged on next startup instead of resetting to default");
        await File.WriteAllTextAsync(path, "{invalid");
        check(AppearanceSettingsStore.Read(path).Transparency == 72, "Corrupt appearance settings safely use default transparency");
        await File.WriteAllTextAsync(path, "{\"Transparency\":200}");
        check(AppearanceSettingsStore.Read(path).Transparency == 100, "Saved transparency outside valid range is bounded");
        check(!AppearanceSettingsStore.Save(output, new AppearanceSettings(80)), "Appearance save failure is reported");
    }
}
