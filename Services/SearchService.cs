using System.Text;
using FastExplorer.Models;

namespace FastExplorer.Services;

public static class SearchService
{
    // Recursively searches by file/folder name (substring, case-insensitive).
    // Streams results back via the callback so the UI can render matches as they
    // arrive instead of waiting for the whole tree to be walked.
    public static async Task SearchByNameAsync(
        string rootPath,
        string query,
        Action<FileSystemItem> onResult,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            foreach (var item in WalkDirectory(rootPath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    onResult(item);
                }
            }
        }, cancellationToken);
    }

    // Recursively searches inside plain-text file contents for a substring.
    // Skips files that are large or obviously binary to stay fast and avoid
    // reading gigabyte media files byte-by-byte.
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".cs", ".cpp", ".c", ".h", ".js", ".ts", ".py",
        ".java", ".json", ".xml", ".html", ".css", ".csv", ".yaml", ".yml", ".ini", ".config",
    };

    private const long MaxContentSearchBytes = 10 * 1024 * 1024; // 10 MB

    public static async Task SearchByContentAsync(
        string rootPath,
        string query,
        Action<FileSystemItem> onResult,
        CancellationToken cancellationToken)
    {
        await Task.Run(async () =>
        {
            foreach (var item in WalkDirectory(rootPath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.IsDirectory) continue;
                if (!TextExtensions.Contains(item.Extension)) continue;
                if (item.SizeBytes > MaxContentSearchBytes) continue;

                try
                {
                    var text = await File.ReadAllTextAsync(item.FullPath, Encoding.UTF8, cancellationToken);
                    if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        onResult(item);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Skip files that are locked, deleted mid-walk, or inaccessible.
                }
            }
        }, cancellationToken);
    }

    private static IEnumerable<FileSystemItem> WalkDirectory(string rootPath, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            IEnumerable<FileSystemItem> children;
            try
            {
                children = FileSystemService.EnumerateDirectory(current).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var child in children)
            {
                yield return child;
                if (child.IsDirectory)
                {
                    pending.Push(child.FullPath);
                }
            }
        }
    }
}
