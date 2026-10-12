using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClearDesk.GlassWindow;

public enum DockSortMode { Fixed, Recent }
internal sealed record DockPreferences(DockSortMode SortMode = DockSortMode.Fixed, string[]? RecentLaunchKeys = null);

internal static class DockPreferencesStore
{
    internal const int HistoryLimit = 512;
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter<DockSortMode>() }
    };
    internal static DockPreferences Read(string? path)
    {
        if (path is null || !File.Exists(path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<DockPreferences>(File.ReadAllText(path), Options);
            return settings is null ? new() : new(
                Enum.IsDefined(settings.SortMode) ? settings.SortMode : DockSortMode.Fixed,
                settings.RecentLaunchKeys?.Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.Ordinal).Take(HistoryLimit).ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    internal static bool Save(string? path, DockPreferences settings)
    {
        if (path is null) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings, Options));
            File.Move(path + ".tmp", path, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }
}
