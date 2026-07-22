using FastExplorer.Models;

namespace FastExplorer.Services;

// Powers Controls/DiskUsageView's treemap: computes total size for every *direct*
// child of a folder (recursive for subdirectories, via PropertiesService's already-
// tested/cancellable CalculateFolderSize), one level at a time rather than eagerly
// walking the whole subtree up front. DiskUsageView re-calls this on drill-down
// instead of pre-scanning everything, which keeps a single scan bounded to "one
// folder's immediate children" instead of "however much of the disk is under here" -
// the difference between an instant view and potentially scanning all of C:\.
public static class DiskUsageService
{
    public static async Task<IReadOnlyList<DiskUsageEntry>> ScanLevelAsync(string path, CancellationToken cancellationToken)
    {
        // Listing itself runs on a background thread too - DiskUsageView calls this
        // straight from the UI thread, and a folder with tens of thousands of entries
        // (e.g. node_modules, exactly the kind of folder someone reaches for this
        // feature to investigate) can make even Directory.Enumerate*'s first call
        // noticeably block if it isn't offloaded.
        var (directories, files) = await Task.Run(() =>
        {
            try
            {
                var options = new EnumerationOptions { IgnoreInaccessible = true };
                return (Directory.EnumerateDirectories(path, "*", options).ToList(),
                        Directory.EnumerateFiles(path, "*", options).ToList());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (new List<string>(), new List<string>());
            }
        }, cancellationToken);

        if (directories.Count == 0 && files.Count == 0) return Array.Empty<DiskUsageEntry>();

        // Each subdirectory's size is computed independently and in parallel - the
        // same "fan out, don't serialize independent I/O" approach already used for
        // probing drives in FileSystemService.GetDrivesAsync - so a folder with many
        // subfolders scans in roughly the time of the *slowest* one, not their sum.
        var directoryEntries = await Task.WhenAll(directories.Select(async dir =>
        {
            var size = await PropertiesService.CalculateFolderSizeAsync(dir, cancellationToken);
            return new DiskUsageEntry(Path.GetFileName(dir), dir, size, IsDirectory: true);
        }));
        cancellationToken.ThrowIfCancellationRequested();

        var fileEntries = await Task.Run(() => files.Select(file =>
        {
            long size;
            try
            {
                size = new FileInfo(file).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                size = 0;
            }
            return new DiskUsageEntry(Path.GetFileName(file), file, size, IsDirectory: false);
        }).ToList(), cancellationToken);

        return directoryEntries.Concat(fileEntries).OrderByDescending(e => e.SizeBytes).ToList();
    }
}
