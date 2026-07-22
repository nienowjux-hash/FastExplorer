using System.Text;
using FastExplorer.Models;

namespace FastExplorer.Services;

public static class SearchService
{
    // Recursively searches by file/folder name (substring, case-insensitive).
    // Streams results back via the callback so the UI can render matches as they
    // arrive instead of waiting for the whole tree to be walked.
    public static Task SearchByNameAsync(
        string rootPath,
        string query,
        Action<FileSystemItem> onResult,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            WalkDirectoryParallel(rootPath, cancellationToken, item =>
            {
                if (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    onResult(item);
            });
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

    public static Task SearchByContentAsync(
        string rootPath,
        string query,
        Action<FileSystemItem> onResult,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            WalkDirectoryParallel(rootPath, cancellationToken, item =>
            {
                if (item.IsDirectory) return;
                if (!TextExtensions.Contains(item.Extension)) return;
                if (item.SizeBytes > MaxContentSearchBytes) return;

                try
                {
                    var text = File.ReadAllText(item.FullPath, Encoding.UTF8);
                    if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
                        onResult(item);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Skip files that are locked, deleted mid-walk, or inaccessible.
                }
            });
        }, cancellationToken);
    }

    // Fans subdirectories out across the thread pool instead of walking one directory
    // at a time - directory enumeration is I/O-bound, so overlapping many of them cuts
    // wall-clock time substantially on a whole-drive search. It also keeps cancellation
    // responsive: a single huge folder (e.g. WinSxS) blocking one worker no longer stalls
    // every other branch of the tree, and each branch checks the token independently.
    private static void WalkDirectoryParallel(string rootPath, CancellationToken cancellationToken, Action<FileSystemItem> onItem)
    {
        void Walk(string dir)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<FileSystemItem> children;
            try
            {
                children = FileSystemService.EnumerateDirectory(dir).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return;
            }

            var subdirs = new List<string>();
            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                onItem(child);
                if (child.IsDirectory) subdirs.Add(child.FullPath);
            }

            if (subdirs.Count == 0) return;

            var options = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount,
            };
            try
            {
                Parallel.ForEach(subdirs, options, Walk);
            }
            catch (Exception ex) when (IsCancellation(ex, cancellationToken))
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        Walk(rootPath);
    }

    private static bool IsCancellation(Exception ex, CancellationToken token)
    {
        if (ex is OperationCanceledException oce) return oce.CancellationToken == token || token.IsCancellationRequested;
        if (ex is AggregateException agg) return agg.Flatten().InnerExceptions.All(e => IsCancellation(e, token));
        return false;
    }
}
