using System.IO;
using System.Text.Json;

namespace ClearDesk.GlassWindow;

internal sealed record DockLayout(double Left, double Top, double Scale);

internal static class DockLayoutStore
{
    internal static DockLayout? Read(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            var layout = JsonSerializer.Deserialize<DockLayout>(File.ReadAllText(path));
            return layout is not null && double.IsFinite(layout.Left) && double.IsFinite(layout.Top) && double.IsFinite(layout.Scale)
                ? layout : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal static bool Save(string? path, DockLayout layout)
    {
        if (path is null) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(layout));
            File.Move(path + ".tmp", path, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }
}
