namespace FastExplorer.Models;

// Drives the "Filtrar" flyout in FolderView - deliberately the same grouping
// IconGlyphMap already uses to pick an icon/color per extension (see
// IconGlyphMap.GetCategory), rather than a second, separately-maintained
// extension list.
public enum FileTypeCategory
{
    All,
    Folder,
    Document,
    Image,
    Audio,
    Video,
    Archive,
    Executable,
    Code,
    Font,
    Other,
}
