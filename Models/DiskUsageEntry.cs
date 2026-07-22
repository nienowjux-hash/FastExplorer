namespace FastExplorer.Models;

// One row in the disk-usage treemap (Controls/DiskUsageView) - a single level's
// worth of direct children of the folder being analyzed, each already carrying its
// own total size (recursive, for directories) rather than a lazily-computed one, so
// TreemapLayout has real numbers to work with up front.
public sealed record DiskUsageEntry(string Name, string FullPath, long SizeBytes, bool IsDirectory)
{
    public string SizeDisplay => FileSystemItem.FormatSize(SizeBytes);
}
