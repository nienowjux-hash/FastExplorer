using System.Text.Json;
using FastExplorer.Models;

namespace FastExplorer.Services;

public static class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastExplorer");

    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    private class SettingsData
    {
        public AppTheme Theme { get; set; } = AppTheme.System;
        public bool ShowHiddenFiles { get; set; }
        public AccentColor AccentColor { get; set; } = AccentColor.Blue;
    }

    private static SettingsData LoadData()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new SettingsData();
            var json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new SettingsData();
        }
    }

    private static void SaveData(SettingsData data)
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFile, json);
    }

    public static AppTheme LoadTheme() => LoadData().Theme;

    public static void SaveTheme(AppTheme theme)
    {
        var data = LoadData();
        data.Theme = theme;
        SaveData(data);
    }

    public static bool LoadShowHiddenFiles() => LoadData().ShowHiddenFiles;

    public static void SaveShowHiddenFiles(bool value)
    {
        var data = LoadData();
        data.ShowHiddenFiles = value;
        SaveData(data);
    }

    public static AccentColor LoadAccentColor() => LoadData().AccentColor;

    public static void SaveAccentColor(AccentColor accent)
    {
        var data = LoadData();
        data.AccentColor = accent;
        SaveData(data);
    }
}
