using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace ClearDesk.GlassWindow;

internal static class DesktopIconVerifier
{
    internal static async Task RunAsync(MainWindow launcher, DockWindow dock, LauncherViewModel model,
        string output, Action<bool, string> check, bool changeDesktop)
    {
        ((Button)launcher.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var settings = launcher.Settings!;
        var initial = DesktopIconVisibility.Read();
        try
        {
            check(AutomationProperties.GetName(settings.HideDesktopIconsToggle) == "隐藏 Windows 桌面图标",
                "Desktop icon toggle has a descriptive accessible name");
            check(settings.HideDesktopIconsToggle.IsEnabled == initial.Available
                && settings.HideDesktopIconsToggle.IsChecked == (initial.Available ? initial.Hidden : null),
                "Desktop icon setting reflects Explorer state and disables when desktop is unavailable");
            if (!changeDesktop) return;
            check(initial.Available, "Windows Explorer desktop is available for icon integration verification");
            var panelOrder = model.SnapshotOrder();
            var dockOrder = model.DockEntries.ToArray();
            settings.HideDesktopIconsToggle.IsChecked = true;
            await settings.DesktopIconChange;
            check(DesktopIconVisibility.Read() is { Available: true, Hidden: true }
                && settings.HideDesktopIconsToggle.IsChecked == true, "Settings switch hides actual Windows desktop icon view");
            check(launcher.IsVisible && dock.IsVisible && model.Entries.SequenceEqual(panelOrder)
                && model.DockEntries.SequenceEqual(dockOrder), "Hiding Windows icons preserves ClearDesk windows and selected icons");
            var repeat = await DesktopIconVisibility.SetHiddenAsync(true);
            check(repeat is { Hidden: true, Error: null }, "Repeated hide request keeps icons hidden rather than toggling them back");
            await Task.Delay(150);
            SmokeVerifier.Capture(settings, Path.Combine(output, "desktop-icon-settings.png"));
            settings.Close();
            ((Button)launcher.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            settings = launcher.Settings!;
            check(settings.HideDesktopIconsToggle.IsChecked == true, "Reopening settings reads current hidden desktop state");
            settings.HideDesktopIconsToggle.IsChecked = false;
            await settings.DesktopIconChange;
            check(DesktopIconVisibility.Read() is { Available: true, Hidden: false }, "Turning setting off restores actual Windows desktop icons");
            repeat = await DesktopIconVisibility.SetHiddenAsync(false);
            check(repeat is { Hidden: false, Error: null }, "Repeated show request keeps icons visible");
            await DesktopIconVisibility.SetHiddenAsync(true);
            settings.Close();
            ((Button)launcher.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            settings = launcher.Settings!;
            check(settings.HideDesktopIconsToggle.IsChecked == true,
                "Settings synchronizes desktop visibility changed outside its own toggle");
        }
        finally
        {
            settings.Close();
            if (changeDesktop && initial.Available)
            {
                var restored = await DesktopIconVisibility.SetHiddenAsync(initial.Hidden);
                if (!restored.Available || restored.Hidden != initial.Hidden)
                    throw new InvalidOperationException("Could not restore original Windows desktop icon visibility: " + restored.Error);
                check(true, "Desktop integration verification restores original user desktop icon visibility");
            }
        }
    }
}
