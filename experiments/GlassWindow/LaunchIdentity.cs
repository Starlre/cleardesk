using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;

namespace ClearDesk.GlassWindow;

internal static class LaunchIdentity
{
    internal static string ForSource(string path) => Key("source", NormalizePath(path));

    internal static string Read(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return ReadLink(path);
            if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                bool inShortcut = false;
                foreach (string line in File.ReadLines(path))
                {
                    string text = line.Trim();
                    if (text.StartsWith('[')) inShortcut = text.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase);
                    else if (inShortcut && text.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        string url = text[4..].Trim();
                        if (Uri.TryCreate(url, UriKind.Absolute, out _)) return Key("url", url);
                    }
                }
                return ForSource(path);
            }
            return Key("file", NormalizePath(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException or ArgumentException or NotSupportedException)
        { return ForSource(path); }
    }

    private static string ReadLink(string path)
    {
        // Read without Resolve: no target search, link modification, dialog or network resolution.
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        byte[] header = reader.ReadBytes(76);
        if (header.Length != 76 || BitConverter.ToUInt32(header, 0) != 76) return ForSource(path);
        uint flags = BitConverter.ToUInt32(header, 20);
        // Advertised installers and environment-dependent targets require richer identities.
        if ((flags & (0x1000u | 0x200u)) != 0) return ForSource(path);
        object instance = new ShellLink();
        try
        {
            ((IPersistFile)instance).Load(path, 0);
            var link = (IShellLinkW)instance;
            var target = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            var directory = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, 0, 4);
            link.GetArguments(arguments, arguments.Capacity);
            link.GetWorkingDirectory(directory, directory.Capacity);
            if (!Path.IsPathFullyQualified(target.ToString())) return ForSource(path);
            string workingDirectory = directory.ToString();
            if (workingDirectory.Length > 0 && !Path.IsPathFullyQualified(workingDirectory)) return ForSource(path);
            return Key("link", NormalizePath(target.ToString()), arguments.ToString(),
                workingDirectory.Length == 0 ? "" : NormalizePath(workingDirectory),
                (flags & (0x2000u | 0x8000u)).ToString(), BitConverter.ToUInt32(header, 60).ToString());
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();
    private static string Key(params string[] parts) => JsonSerializer.Serialize(parts);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, nint findData, uint flags);
        void GetIDList(out nint idList);
        void SetIDList(nint idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
    }
}
