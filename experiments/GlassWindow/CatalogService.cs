using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClearDesk.GlassWindow;

public sealed record CatalogEntry(string Name, string SourcePath, ImageSource? Icon)
{
    internal string LaunchKey { get; init; } = LaunchIdentity.ForSource(SourcePath);
    internal IReadOnlyList<string> SourcePaths { get; init; } = [SourcePath];
}

public static class CatalogService
{
    public static Task<IReadOnlyList<CatalogEntry>> LoadAsync(CancellationToken token = default) => OnSta(() =>
    {
        var paths = new List<string>();
        foreach (var folder in new[] { Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu,
                     Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory })
        {
            token.ThrowIfCancellationRequested();
            string root = Environment.GetFolderPath(folder);
            if (!Directory.Exists(root)) continue;
            try
            {
                bool recurse = folder is Environment.SpecialFolder.StartMenu or Environment.SpecialFolder.CommonStartMenu;
                paths.AddRange(Directory.EnumerateFiles(root, "*", new EnumerationOptions
                { RecurseSubdirectories = recurse, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                    .Where(IsShortcut));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var entries = MergeDuplicates(paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => { token.ThrowIfCancellationRequested(); return Create(path); })).ToList();
        if (entries.Count == 0)
        {
            foreach (var name in new[] { "notepad.exe", "explorer.exe", "calc.exe" })
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), name);
                if (!File.Exists(path)) path = Path.Combine(Environment.SystemDirectory, name);
                if (File.Exists(path)) entries.Add(Create(path));
            }
        }
        return (IReadOnlyList<CatalogEntry>)entries;
    }, token);

    public static Task<IReadOnlyList<CatalogEntry>> ImportAsync(IEnumerable<string> paths, CancellationToken token = default)
    {
        string[] snapshot = paths.ToArray();
        return OnSta<IReadOnlyList<CatalogEntry>>(() => MergeDuplicates(snapshot.Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(path => { token.ThrowIfCancellationRequested(); return Create(path); })), token);
    }

    internal static IReadOnlyList<CatalogEntry> MergeDuplicates(IEnumerable<CatalogEntry> entries) => entries
        .GroupBy(entry => entry.LaunchKey, StringComparer.Ordinal)
        .Select(group => group.First() with
        {
            SourcePaths = group.SelectMany(entry => entry.SourcePaths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        }).ToArray();

    private static bool IsShortcut(string path) => Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase);

    private static CatalogEntry Create(string path)
    {
        string name = Directory.Exists(path) ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path);
        ImageSource? icon = null;
        try
        {
            var info = new ShellFileInfo();
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100) != 0 && info.Icon != 0)
            {
                try
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();
                    icon = bitmap;
                }
                finally { DestroyIcon(info.Icon); }
            }
        }
        catch (ArgumentException) { }
        catch (COMException) { }
        return new CatalogEntry(name, Path.GetFullPath(path), icon ?? FallbackIcon()) { LaunchKey = LaunchIdentity.Read(path) };
    }

    private static ImageSource FallbackIcon()
    {
        var drawing = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(169, 195, 230)), null,
            new RectangleGeometry(new Rect(0, 0, 36, 36), 8, 8));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    public static bool Launch(CatalogEntry entry, out string? error)
    {
        try { Process.Start(new ProcessStartInfo(entry.SourcePath) { UseShellExecute = true }); error = null; return true; }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        { error = exception.Message; return false; }
    }

    private static Task<T> OnSta<T>(Func<T> action, CancellationToken token)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { token.ThrowIfCancellationRequested(); completion.TrySetResult(action()); }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }) { IsBackground = true, Name = "ClearDesk.Shell.STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon; public int IconIndex; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
}
