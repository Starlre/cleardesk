using System.IO;
using System.Text.Json;

namespace ClearDesk.GlassWindow;

internal static class PanelVerifier
{
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        string root = Path.Combine(output, "panel-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string firstPath = Path.Combine(root, "first.txt"), secondPath = Path.Combine(root, "second.txt"), thirdPath = Path.Combine(root, "third.txt");
        foreach (string path in new[] { firstPath, secondPath, thirdPath }) await File.WriteAllTextAsync(path, "fixture");
        var pool = await CatalogService.ImportAsync([firstPath, secondPath]);
        string legacyPath = Path.Combine(root, "state.json"), panelPath = Path.Combine(root, "panel-state.json");
        string legacy = JsonSerializer.Serialize(new[] { firstPath, secondPath });
        await File.WriteAllTextAsync(legacyPath, legacy);
        var model = new LauncherViewModel(panelPath);
        await model.InitializeAsync(pool);
        check(model.Entries.Count == 0 && model.DockEntries.Count == 0 && model.PoolEntries.Count == 2
            && await File.ReadAllTextAsync(legacyPath) == legacy, "Fresh panel and Dock stay empty while legacy auto-populated state is preserved");
        var picker = new IconPoolViewModel(model);
        picker.VisibleEntries[1].IsSelected = true;
        picker.SearchText = "first";
        check(picker.SelectedCount == 1 && model.Entries.Count == 0 && picker.VisibleEntries.Count == 1,
            "Pool search retains hidden selections without adding anything to panel");
        check(picker.AddSelected() && model.Entries.Count == 1 && model.Entries[0].Entry.SourcePath == secondPath
            && model.DockEntries.Count == 1, "Confirming pool selection adds only chosen entries to panel and Dock");
        picker.SearchText = "second";
        check(picker.VisibleEntries[0].IsAdded && !picker.VisibleEntries[0].CanSelect && !picker.CanAdd,
            "Already-added pool icons are marked and cannot be selected again");
        var third = await CatalogService.ImportAsync([thirdPath]);
        var restored = new LauncherViewModel(panelPath);
        await restored.InitializeAsync([.. pool, .. third]);
        check(restored.Entries.Count == 1 && restored.Entries[0].Entry.SourcePath == secondPath && restored.PoolEntries.Count == 3,
            "Restart restores chosen entries without auto-adding newly scanned applications");
        // A manually imported path may not appear in the system scan on the next startup.
        restored.Add(third);
        var externalRestore = new LauncherViewModel(panelPath);
        await externalRestore.InitializeAsync(pool);
        check(externalRestore.Entries.Count == 2 && externalRestore.PoolEntries.Count == 3,
            "Manually selected file outside scanned catalog survives restart");
        externalRestore.Remove(externalRestore.Entries[0]); externalRestore.Remove(externalRestore.Entries[0]);
        var emptyRestored = new LauncherViewModel(panelPath);
        await emptyRestored.InitializeAsync(pool);
        check(emptyRestored.Entries.Count == 0 && emptyRestored.DockEntries.Count == 0
            && emptyRestored.PoolEntries.Count == 2, "Removing all panel entries persists an empty selection and retains scanned pool");
        check(emptyRestored.Add([pool[0]]) == 1, "Removed application can be added again from pool");
        await File.WriteAllTextAsync(panelPath, "{invalid");
        var corrupt = new LauncherViewModel(panelPath);
        await corrupt.InitializeAsync(pool);
        check(corrupt.Entries.Count == 0 && corrupt.Status.Contains("无法读取") && corrupt.IsCatalogReady,
            "Corrupt panel selection stays empty with visible feedback and usable pool");
    }
}
