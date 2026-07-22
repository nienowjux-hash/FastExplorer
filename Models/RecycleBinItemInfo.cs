namespace FastExplorer.Models;

// A snapshot row for the Recycle Bin browser (Controls/RecycleBinView). OriginalPath
// is DeletedFrom+Name recombined - it's what RecycleBinService.TryRestore/
// TryDeletePermanently re-match against, since the underlying Shell.Application COM
// item isn't held onto between listing and acting on it (see RecycleBinService).
public sealed record RecycleBinItemInfo(string Name, string DeletedFrom, string OriginalPath, long Size, DateTime DateDeleted)
{
    public string SizeDisplay => FileSystemItem.FormatSize(Size);

    public string DateDeletedDisplay => DateDeleted == default ? string.Empty : DateDeleted.ToString("g");
}
