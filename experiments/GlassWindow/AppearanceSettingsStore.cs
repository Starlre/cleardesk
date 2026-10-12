using System.IO;
using System.Text.Json;

namespace ClearDesk.GlassWindow;

internal sealed record AppearanceSettings(double Transparency = 72);

internal static class AppearanceSettingsStore
{
    internal static AppearanceSettings Read(string? path)
    {
        if (path is null || !File.Exists(path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path));
            return settings is not null && double.IsFinite(settings.Transparency)
                ? settings with { Transparency = Math.Clamp(settings.Transparency, 0, 100) } : new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    internal static bool Save(string? path, AppearanceSettings settings)
    {
        if (path is null) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings));
            File.Move(path + ".tmp", path, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }
}
