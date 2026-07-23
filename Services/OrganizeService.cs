using FastExplorer.Models;

namespace FastExplorer.Services;

// Physically reorganizes files into category subfolders (Documentos/, Imagens/, ...) on disk,
// using the same FileTypeCategory extension mapping the "Filtrar" flyout already uses (see
// IconGlyphMap.GetCategory) - so folder names line up with the categories the rest of the app
// already calls things by. Non-recursive by design (mirrors DiskUsageService's one-level-at-a-time
// approach): only the given files themselves are moved, existing subfolders (including ones a
// prior run created) are left untouched, which makes re-running this idempotent for free - an
// already-organized file lives one level deeper than where this looks.
public static class OrganizeService
{
    private static readonly Dictionary<FileTypeCategory, string> CategoryFolderNames = new()
    {
        [FileTypeCategory.Document] = "Documentos",
        [FileTypeCategory.Image] = "Imagens",
        [FileTypeCategory.Audio] = "Áudio",
        [FileTypeCategory.Video] = "Vídeo",
        [FileTypeCategory.Archive] = "Compactados",
        [FileTypeCategory.Executable] = "Executáveis",
        [FileTypeCategory.Code] = "Código",
        [FileTypeCategory.Font] = "Fontes",
        [FileTypeCategory.Other] = "Outros",
    };

    public static IReadOnlyList<(string CurrentPath, string OriginalPath)> OrganizeByType(
        IEnumerable<string> filePaths, string destinationFolder)
    {
        var moves = new List<(string, string)>();
        foreach (var path in filePaths)
        {
            var category = IconGlyphMap.GetCategory(FileSystemService.ToItem(new FileInfo(path)));
            var folderName = CategoryFolderNames.GetValueOrDefault(category, "Outros");
            var targetDir = Path.Combine(destinationFolder, folderName);

            Directory.CreateDirectory(targetDir);
            var name = FileSystemService.MakeUniqueName(targetDir, Path.GetFileName(path));
            var newPath = Path.Combine(targetDir, name);
            File.Move(path, newPath);
            moves.Add((newPath, path));
        }
        return moves;
    }
}
