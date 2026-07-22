using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FastExplorer.Models;

public enum FileSystemItemKind
{
    Directory,
    File,
    Drive,
}

public partial class FileSystemItem : ObservableObject
{
    // Set asynchronously after the item is first shown (see FolderView's
    // ListView.ContainerContentChanging + ThumbnailService) - mutable and
    // notifying, unlike everything else on this type, which is fixed at creation.
    [ObservableProperty]
    private BitmapImage? thumbnail;

    public bool HasThumbnail => Thumbnail is not null;

    partial void OnThumbnailChanged(BitmapImage? value) => OnPropertyChanged(nameof(HasThumbnail));

    // Set/cleared by TabViewModel.UpdateCutMarkers() to mirror Explorer's dimmed
    // "marked for move" look after Ctrl+X, until the item is pasted or the cut is
    // superseded by another copy/cut.
    [ObservableProperty]
    private bool isCut;

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public FileSystemItemKind Kind { get; init; }
    public long SizeBytes { get; init; }
    public DateTime DateModified { get; init; }
    public DateTime DateCreated { get; init; }
    public string Extension { get; init; } = string.Empty;
    public long? DriveTotalBytes { get; init; }
    public long? DriveFreeBytes { get; init; }
    public bool IsCloudPlaceholder { get; init; }
    public bool IsCloudPinned { get; init; }
    public bool IsNetworkDrive { get; init; }

    public bool IsDirectory => Kind is FileSystemItemKind.Directory or FileSystemItemKind.Drive;

    public string SizeDisplay
    {
        get
        {
            if (Kind == FileSystemItemKind.Drive)
            {
                return DriveTotalBytes is > 0 && DriveFreeBytes is not null
                    ? $"{FormatSize(DriveTotalBytes.Value - DriveFreeBytes.Value)} used of {FormatSize(DriveTotalBytes.Value)}"
                    : string.Empty;
            }
            return IsDirectory ? string.Empty : FormatSize(SizeBytes);
        }
    }

    public string DateModifiedDisplay => DateModified == default ? string.Empty : DateModified.ToString("g");

    public string IconGlyph => IconGlyphMap.GetGlyph(this);

    // Drives the "Filtrar" flyout in FolderView (TabViewModel.FilterCategory) - same
    // extension grouping the icon/color already use, see IconGlyphMap.GetCategory.
    public FileTypeCategory Category => IconGlyphMap.GetCategory(this);

    public string? IconColorHex => IconGlyphMap.GetColorHex(this);

    // Cloud-sync overlay (OneDrive/Files On-Demand) - purely a local file-attribute
    // read, no cloud API involved. Cloud icon = placeholder not downloaded yet;
    // checkmark = "always keep on this device". Built from raw codepoints (rather
    // than typed literally) to avoid encoding round-trip corruption of PUA characters.
    private const int CloudCode = 0xE753;
    private const int CheckMarkCode = 0xE73E;

    public string CloudGlyph => IsCloudPlaceholder
        ? ((char)CloudCode).ToString()
        : IsCloudPinned ? ((char)CheckMarkCode).ToString() : string.Empty;

    public bool HasCloudStatus => CloudGlyph.Length > 0;

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }
}
