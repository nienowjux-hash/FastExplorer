using System.Security.Cryptography;
using FastExplorer.Models;

namespace FastExplorer.Services;

// Finds byte-for-byte identical files under a folder (recursive). Two-phase to avoid
// hashing everything: files are first grouped by size (a cheap stat, no I/O beyond the
// directory listing itself) - duplicates must share a size, so only files that already
// share a size with at least one other file ever get hashed. On a folder with mostly
// uniquely-sized files (the common case), this skips hashing almost everything.
public static class DuplicateFinderService
{
    public static async Task<IReadOnlyList<DuplicateFileGroup>> FindDuplicatesAsync(
        string rootPath, CancellationToken cancellationToken)
    {
        var bySize = await Task.Run(() =>
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System,
            };

            var groups = new Dictionary<long, List<string>>();
            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(rootPath, "*", options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return groups;
            }

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long size;
                try
                {
                    size = new FileInfo(path).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (!groups.TryGetValue(size, out var list))
                {
                    list = new List<string>();
                    groups[size] = list;
                }
                list.Add(path);
            }
            return groups;
        }, cancellationToken);

        var result = new List<DuplicateFileGroup>();
        foreach (var (size, paths) in bySize)
        {
            if (paths.Count < 2) continue;
            cancellationToken.ThrowIfCancellationRequested();

            var byHash = new Dictionary<string, List<string>>();
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string hash;
                try
                {
                    hash = await ComputeHashAsync(path, cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (!byHash.TryGetValue(hash, out var list))
                {
                    list = new List<string>();
                    byHash[hash] = list;
                }
                list.Add(path);
            }

            foreach (var group in byHash.Values)
            {
                if (group.Count > 1)
                {
                    result.Add(new DuplicateFileGroup { SizeBytes = size, Paths = group });
                }
            }
        }

        return result.OrderByDescending(g => g.SizeBytes * (g.Paths.Count - 1)).ToList();
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes);
    }
}
