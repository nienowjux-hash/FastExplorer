using System.Text.Json;

namespace FastExplorer.Services;

public class FavoriteEntry
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public static class FavoritesService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastExplorer");

    private static readonly string FavoritesFile = Path.Combine(SettingsDir, "favorites.json");

    public static List<FavoriteEntry> Load()
    {
        List<FavoriteEntry> favorites;
        try
        {
            if (!File.Exists(FavoritesFile))
            {
                favorites = DefaultFavorites();
            }
            else
            {
                var json = File.ReadAllText(FavoritesFile);
                favorites = JsonSerializer.Deserialize<List<FavoriteEntry>>(json) ?? DefaultFavorites();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            favorites = DefaultFavorites();
        }

        // Runs on every load (not just first-run defaults) so upgrading an existing
        // install picks up OneDrive too, without needing to reset favorites.json.
        if (EnsureOneDriveFavorite(favorites))
        {
            Save(favorites);
        }

        return favorites;
    }

    private static bool EnsureOneDriveFavorite(List<FavoriteEntry> favorites)
    {
        var oneDrivePath = Environment.GetEnvironmentVariable("OneDrive")
            ?? Environment.GetEnvironmentVariable("OneDriveConsumer");
        if (string.IsNullOrEmpty(oneDrivePath) || !Directory.Exists(oneDrivePath)) return false;
        if (favorites.Any(f => string.Equals(f.Path, oneDrivePath, StringComparison.OrdinalIgnoreCase))) return false;

        favorites.Insert(0, new FavoriteEntry { Name = "OneDrive", Path = oneDrivePath });
        return true;
    }

    public static void Save(IEnumerable<FavoriteEntry> favorites)
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(favorites, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FavoritesFile, json);
    }

    private static List<FavoriteEntry> DefaultFavorites()
    {
        var list = new List<FavoriteEntry>();
        void AddIfExists(string name, Environment.SpecialFolder folder)
        {
            var path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                list.Add(new FavoriteEntry { Name = name, Path = path });
            }
        }

        AddIfExists("Área de Trabalho", Environment.SpecialFolder.DesktopDirectory);
        AddIfExists("Documentos", Environment.SpecialFolder.MyDocuments);
        AddIfExists("Imagens", Environment.SpecialFolder.MyPictures);

        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
        {
            list.Add(new FavoriteEntry { Name = "Downloads", Path = downloads });
        }

        return list;
    }
}
