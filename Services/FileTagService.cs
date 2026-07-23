using System.Text.Json;

namespace FastExplorer.Services;

public sealed record FileTagInfo(string ColorHex, string? Label);

// Colored tags a user assigns to files/folders for their own visual organization -
// independent of "Organizar arquivos" (which sorts by file *type*; tags are whatever
// the user wants, e.g. "Urgente"). One tag per path, persisted in its own JSON file
// (same %LocalAppData%\FastExplorer convention as FavoritesService/SettingsService,
// kept separate from settings.json since this is an open-ended, potentially large
// path-keyed map rather than a fixed set of app settings).
public static class FileTagService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastExplorer");

    private static readonly string TagsFile = Path.Combine(SettingsDir, "tags.json");

    // Paths are compared case-insensitively (NTFS default) with a trailing separator
    // trimmed, so a folder's tag survives whether it's looked up as "C:\Foo" or "C:\Foo\".
    private static string NormalizeKey(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();

    public static FileTagInfo? GetTag(string path) =>
        LoadTags().TryGetValue(NormalizeKey(path), out var info) ? info : null;

    public static void SetTag(string path, string? colorHex, string? label)
    {
        var tags = LoadTags();
        var key = NormalizeKey(path);
        if (colorHex is null) tags.Remove(key);
        else tags[key] = new FileTagInfo(colorHex, string.IsNullOrWhiteSpace(label) ? null : label.Trim());
        SaveTags(tags);
    }

    private static Dictionary<string, FileTagInfo> LoadTags()
    {
        try
        {
            if (!File.Exists(TagsFile)) return new();
            var json = File.ReadAllText(TagsFile);
            return JsonSerializer.Deserialize<Dictionary<string, FileTagInfo>>(json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    private static void SaveTags(Dictionary<string, FileTagInfo> tags)
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(tags);
        File.WriteAllText(TagsFile, json);
    }
}
