using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ClearDesk.GlassWindow;

internal static class CatalogVerifier
{
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        string root = Path.Combine(output, "catalog-fixtures");
        Directory.CreateDirectory(root);
        string target = Path.Combine(root, "target.exe"), otherTarget = Path.Combine(root, "other.exe");
        File.WriteAllBytes(target, []); File.WriteAllBytes(otherTarget, []);
        string desktop = Path.Combine(root, "desktop"), menu = Path.Combine(root, "menu");
        Directory.CreateDirectory(desktop); Directory.CreateDirectory(menu);
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        string MakeLink(string folder, string name, string destination, string arguments = "", string? directory = null)
        {
            string path = Path.Combine(folder, name + ".lnk");
            object shortcut = ((dynamic)shell).CreateShortcut(path);
            try
            {
                dynamic link = shortcut;
                link.TargetPath = destination; link.Arguments = arguments;
                link.WorkingDirectory = directory ?? root;
                link.Save();
            }
            finally { Marshal.FinalReleaseComObject(shortcut); }
            return path;
        }
        try
        {
            string first = MakeLink(menu, "Application", target);
            string duplicate = MakeLink(desktop, "Application", target, directory: root + Path.DirectorySeparatorChar);
            string withArgument = MakeLink(menu, "Profile", target, "--profile A");
            string caseArgument = MakeLink(menu, "OtherProfile", target, "--profile a");
            string differentDirectory = MakeLink(menu, "OtherDirectory", target, directory: desktop);
            string sameName = MakeLink(desktop, "Profile", otherTarget);
            string administrator = MakeLink(menu, "Administrator", target);
            byte[] adminBytes = File.ReadAllBytes(administrator);
            BitConverter.GetBytes(BitConverter.ToUInt32(adminBytes, 20) | 0x2000u).CopyTo(adminBytes, 20);
            File.WriteAllBytes(administrator, adminBytes);
            var merged = await CatalogService.ImportAsync([first, duplicate]);
            check(merged.Count == 1 && merged[0].SourcePaths.Count == 2, "Real desktop/menu shortcuts to same launch merge with source aliases");
            var variants = await CatalogService.ImportAsync([first, duplicate, withArgument, caseArgument, differentDirectory, sameName, administrator]);
            check(variants.Count == 6, "Distinct arguments, argument case, working directories, targets and administrator flags remain separate");

            string brokenA = Path.Combine(root, "broken-a.lnk"), brokenB = Path.Combine(root, "broken-b.lnk");
            await File.WriteAllTextAsync(brokenA, "invalid"); await File.WriteAllTextAsync(brokenB, "invalid");
            check((await CatalogService.ImportAsync([brokenA, brokenB])).Count == 2, "Unresolved shortcuts retain distinct source identities");
            string urlA = Path.Combine(root, "a.url"), urlB = Path.Combine(root, "b.url"), urlC = Path.Combine(root, "c.url");
            await File.WriteAllTextAsync(urlA, "[InternetShortcut]\nURL=https://example.com/app?profile=A\n");
            await File.WriteAllTextAsync(urlB, "[InternetShortcut]\nURL=https://example.com/app?profile=A\n");
            await File.WriteAllTextAsync(urlC, "[InternetShortcut]\nURL=https://example.com/app?profile=a\n");
            check((await CatalogService.ImportAsync([urlA, urlB, urlC])).Count == 2, "Identical URLs merge while query case remains significant");

            string statePath = Path.Combine(root, "old-state.json");
            await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new[] { duplicate, withArgument, first }));
            var model = new LauncherViewModel(statePath);
            model.Load([variants.Single(entry => entry.SourcePath == withArgument), merged[0]]);
            check(model.Entries.Count == 2 && model.Entries[0].Entry.SourcePath == first, "Old duplicate source preserves earliest saved position through aliases");
            check(model.Add(await CatalogService.ImportAsync([duplicate])) == 0, "Importing another shortcut to an existing launch adds no duplicate");
            var restored = new LauncherViewModel(statePath);
            restored.Load([variants.Single(entry => entry.SourcePath == withArgument), merged[0]]);
            check(restored.Entries[0].Entry.SourcePath == first, "Migrated unique order survives save and reload");
            // Startup may append an old saved shortcut after scanning the catalog.
            var fromState = await CatalogService.ImportAsync([duplicate]);
            var restoredDuplicate = new LauncherViewModel(statePath);
            restoredDuplicate.Load([merged[0], .. fromState]);
            check(restoredDuplicate.Entries.Count == 1, "Restoring old saved imports cannot reintroduce duplicate launches");
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
}
