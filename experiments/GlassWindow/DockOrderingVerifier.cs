using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ClearDesk.GlassWindow;

internal static class DockOrderingVerifier
{
    internal static async Task RunAsync(MainWindow launcher, LauncherViewModel live, string output, Action<bool, string> check)
    {
        string root = Path.Combine(output, "dock-order-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "dock-preferences.json");
        var catalog = live.Entries.Take(12).Select(entry => entry.Entry).ToArray();
        var model = new LauncherViewModel(null, path) { LaunchOverride = _ => true };
        model.Load(catalog);
        var order = model.SnapshotOrder();
        check(model.DockSortMode == DockSortMode.Fixed && model.DockEntries.SequenceEqual(order.Take(8)),
            "New Dock preferences default to fixed panel order with eight entries");
        model.Launch(order[10]); model.Launch(order[9]);
        check(model.DockEntries.SequenceEqual(order.Take(8)), "Launching applications in fixed mode records history without moving Dock icons");
        model.SetDockSortMode(DockSortMode.Recent);
        check(model.DockEntries.SequenceEqual(new[] { order[9], order[10] }.Concat(order.Take(6)))
            && model.Entries.SequenceEqual(order),
            "Recent mode brings used apps beyond first eight into Dock and keeps main panel order unchanged");
        model.Launch(order[10]);
        check(model.DockEntries[0] == order[10] && model.DockEntries[1] == order[9]
            && model.DockEntries.Distinct().Count() == 8,
            "Repeated successful launch moves app to front without duplicate Dock entries");
        var beforeFailure = model.DockEntries.ToArray();
        var savedHistory = await File.ReadAllTextAsync(path);
        model.LaunchOverride = _ => false; model.Launch(order[8]);
        check(model.DockEntries.SequenceEqual(beforeFailure) && await File.ReadAllTextAsync(path) == savedHistory
            && model.Status.Contains("无法打开"), "Failed launch does not change recent order or saved history");
        model.SearchText = order[11].Name;
        check(model.DockEntries.SequenceEqual(beforeFailure), "Panel search does not filter or reorder recent Dock entries");
        model.SearchText = ""; model.Remove(order[10]);
        check(model.DockEntries[0] == order[9] && !model.DockEntries.Contains(order[10]) && model.DockEntries.Count == 8,
            "Removing a recently used panel app removes it from Dock and fills the available slot");
        var restarted = new LauncherViewModel(null, path);
        restarted.Load(model.Entries.Select(entry => entry.Entry).ToArray());
        check(restarted.DockSortMode == DockSortMode.Recent
            && restarted.DockEntries.Select(entry => entry.Entry.LaunchKey).SequenceEqual(model.DockEntries.Select(entry => entry.Entry.LaunchKey)),
            "Restart preserves selected Dock sort mode and recent-use ordering");
        restarted.SetDockSortMode(DockSortMode.Fixed);
        check(restarted.DockEntries.SequenceEqual(restarted.Entries.Take(8)), "Switching to fixed mode restores current panel order");
        restarted.SetDockSortMode(DockSortMode.Recent);
        check(restarted.DockEntries[0].Entry.LaunchKey == order[9].Entry.LaunchKey, "Switching modes retains previous launch history");
        await File.WriteAllTextAsync(path, "{invalid");
        check(DockPreferencesStore.Read(path).SortMode == DockSortMode.Fixed, "Corrupt Dock preferences safely fall back to fixed order");
        await File.WriteAllTextAsync(path, "{\"SortMode\":200,\"RecentLaunchKeys\":[null,\"\",\"same\",\"same\"]}");
        var invalid = DockPreferencesStore.Read(path);
        check(invalid.SortMode == DockSortMode.Fixed && invalid.RecentLaunchKeys!.SequenceEqual(new[] { "same" }),
            "Invalid Dock sort mode and duplicate or empty history entries are normalized");
        var unwritable = new LauncherViewModel(null, root);
        check(!unwritable.SetDockSortMode(DockSortMode.Recent) && unwritable.Status.Contains("保存失败"),
            "Dock sort preference write failure reports visible feedback");

        var originalMode = live.DockSortMode;
        try
        {
            ((Button)launcher.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(150);
            var settings = launcher.Settings!;
            check((DockSortMode)settings.DockSortSelector.SelectedValue == originalMode,
                "Settings Dock sort selector displays current mode");
            settings.DockSortSelector.SelectedValue = DockSortMode.Recent;
            check(live.DockSortMode == DockSortMode.Recent && live.DockEntries.SequenceEqual(live.Entries.Take(8)),
                "Settings selects recent mode immediately with stable panel-order fallback for unused apps");
            await Task.Delay(150);
            SmokeVerifier.Capture(settings, Path.Combine(output, "dock-order-settings.png"));
            settings.Close();
            ((Button)launcher.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check((DockSortMode)launcher.Settings!.DockSortSelector.SelectedValue == DockSortMode.Recent,
                "Reopening settings retains selected Dock sort mode");
            launcher.Settings.DockSortSelector.SelectedValue = DockSortMode.Fixed;
            check(live.DockSortMode == DockSortMode.Fixed && live.DockEntries.SequenceEqual(live.Entries.Take(8)),
                "Settings fixed-order option restores panel-based Dock ordering");
        }
        finally { launcher.Settings?.Close(); live.SetDockSortMode(originalMode); }
    }
}
