namespace FastExplorer.Models;

// Maps file kinds/extensions to Segoe Fluent Icons glyphs (plus a per-type accent
// color). Deliberately avoids Shell icon extraction (SHGetFileInfo/IExtractIcon) for
// speed - that call is one of the biggest reasons Windows Explorer feels slow on
// large folders. This can't reproduce an app's own icon (e.g. a shortcut to Docker
// or Discord shows a generic shortcut glyph, not that app's real icon) but covers
// common file types with distinct, colored icons at zero per-file cost.
// Glyphs are built from raw codepoints (rather than embedding the glyph character
// literally in source) to avoid encoding round-trip corruption of PUA characters.
public static class IconGlyphMap
{
    private const int FolderCode = 0xE8B7;
    private const int DocumentCode = 0xE8A5;
    private const int AudioCode = 0xE8D6;
    private const int VideoCode = 0xE714;
    private const int PictureCode = 0xE8B9;
    private const int PdfCode = 0xEA90;
    private const int CodeCode = 0xE943;
    private const int ExecutableCode = 0xECAA;
    private const int DriveCode = 0xEDA2;
    private const int ArchiveCode = 0xE8DE;
    private const int LinkCode = 0xE71B;
    private const int FontCode = 0xE8D2;

    private const string FolderColor = "#FFCA28";
    private const string WordColor = "#2B579A";
    private const string ExcelColor = "#217346";
    private const string PowerPointColor = "#D24726";
    private const string PdfColor = "#EC1C24";
    private const string ArchiveColor = "#C77B2E";
    private const string PictureColor = "#9C27B0";
    private const string AudioColor = "#E91E63";
    private const string VideoColor = "#C2185B";
    private const string CodeColor = "#5C6BC0";

    private static string G(int codePoint) => ((char)codePoint).ToString();

    // Category rides along with the icon/color choice - both are ultimately the same
    // "what kind of file is this extension" lookup, so FileTypeCategory (used by the
    // Filtrar flyout in FolderView) is derived from this table instead of a second,
    // separately-maintained extension list that could drift out of sync with the icons.
    private sealed record IconStyle(int Code, string? ColorHex, FileTypeCategory Category);

    private static readonly Dictionary<string, IconStyle> ExtensionStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documents - reuse the plain document glyph, tinted per app so the list
        // reads by color the way Explorer's real icons do, without extracting them.
        [".pdf"] = new IconStyle(PdfCode, PdfColor, FileTypeCategory.Document),
        [".doc"] = new IconStyle(DocumentCode, WordColor, FileTypeCategory.Document),
        [".docx"] = new IconStyle(DocumentCode, WordColor, FileTypeCategory.Document),
        [".docm"] = new IconStyle(DocumentCode, WordColor, FileTypeCategory.Document),
        [".rtf"] = new IconStyle(DocumentCode, WordColor, FileTypeCategory.Document),
        [".odt"] = new IconStyle(DocumentCode, WordColor, FileTypeCategory.Document),
        [".xls"] = new IconStyle(DocumentCode, ExcelColor, FileTypeCategory.Document),
        [".xlsx"] = new IconStyle(DocumentCode, ExcelColor, FileTypeCategory.Document),
        [".xlsm"] = new IconStyle(DocumentCode, ExcelColor, FileTypeCategory.Document),
        [".csv"] = new IconStyle(DocumentCode, ExcelColor, FileTypeCategory.Document),
        [".ods"] = new IconStyle(DocumentCode, ExcelColor, FileTypeCategory.Document),
        [".ppt"] = new IconStyle(DocumentCode, PowerPointColor, FileTypeCategory.Document),
        [".pptx"] = new IconStyle(DocumentCode, PowerPointColor, FileTypeCategory.Document),
        [".pptm"] = new IconStyle(DocumentCode, PowerPointColor, FileTypeCategory.Document),
        [".odp"] = new IconStyle(DocumentCode, PowerPointColor, FileTypeCategory.Document),
        [".txt"] = new IconStyle(DocumentCode, null, FileTypeCategory.Document),

        // Images
        [".jpg"] = new IconStyle(PictureCode, PictureColor, FileTypeCategory.Image),
        [".jpeg"] = new IconStyle(PictureCode, PictureColor, FileTypeCategory.Image),
        [".png"] = new IconStyle(PictureCode, PictureColor, FileTypeCategory.Image),
        [".gif"] = new IconStyle(PictureCode, PictureColor, FileTypeCategory.Image),
        [".bmp"] = new IconStyle(PictureCode, PictureColor, FileTypeCategory.Image),
        [".svg"] = new IconStyle(PictureCode, PictureColor, FileTypeCategory.Image),
        [".webp"] = new IconStyle(PictureCode, PictureColor, FileTypeCategory.Image),
        [".ico"] = new IconStyle(PictureCode, PictureColor, FileTypeCategory.Image),
        [".heic"] = new IconStyle(PictureCode, PictureColor, FileTypeCategory.Image),

        // Audio
        [".mp3"] = new IconStyle(AudioCode, AudioColor, FileTypeCategory.Audio),
        [".wav"] = new IconStyle(AudioCode, AudioColor, FileTypeCategory.Audio),
        [".flac"] = new IconStyle(AudioCode, AudioColor, FileTypeCategory.Audio),
        [".aac"] = new IconStyle(AudioCode, AudioColor, FileTypeCategory.Audio),
        [".m4a"] = new IconStyle(AudioCode, AudioColor, FileTypeCategory.Audio),
        [".ogg"] = new IconStyle(AudioCode, AudioColor, FileTypeCategory.Audio),

        // Video
        [".mp4"] = new IconStyle(VideoCode, VideoColor, FileTypeCategory.Video),
        [".mkv"] = new IconStyle(VideoCode, VideoColor, FileTypeCategory.Video),
        [".avi"] = new IconStyle(VideoCode, VideoColor, FileTypeCategory.Video),
        [".mov"] = new IconStyle(VideoCode, VideoColor, FileTypeCategory.Video),
        [".wmv"] = new IconStyle(VideoCode, VideoColor, FileTypeCategory.Video),
        [".webm"] = new IconStyle(VideoCode, VideoColor, FileTypeCategory.Video),

        // Archives / disk images
        [".zip"] = new IconStyle(ArchiveCode, ArchiveColor, FileTypeCategory.Archive),
        [".rar"] = new IconStyle(ArchiveCode, ArchiveColor, FileTypeCategory.Archive),
        [".7z"] = new IconStyle(ArchiveCode, ArchiveColor, FileTypeCategory.Archive),
        [".tar"] = new IconStyle(ArchiveCode, ArchiveColor, FileTypeCategory.Archive),
        [".gz"] = new IconStyle(ArchiveCode, ArchiveColor, FileTypeCategory.Archive),
        [".bz2"] = new IconStyle(ArchiveCode, ArchiveColor, FileTypeCategory.Archive),
        [".iso"] = new IconStyle(ArchiveCode, ArchiveColor, FileTypeCategory.Archive),

        // Executables / scripts / installers
        [".exe"] = new IconStyle(ExecutableCode, null, FileTypeCategory.Executable),
        [".msi"] = new IconStyle(ExecutableCode, null, FileTypeCategory.Executable),
        [".bat"] = new IconStyle(ExecutableCode, null, FileTypeCategory.Executable),
        [".cmd"] = new IconStyle(ExecutableCode, null, FileTypeCategory.Executable),
        [".ps1"] = new IconStyle(ExecutableCode, null, FileTypeCategory.Executable),
        [".sh"] = new IconStyle(ExecutableCode, null, FileTypeCategory.Executable),

        // Shortcuts
        [".lnk"] = new IconStyle(LinkCode, null, FileTypeCategory.Other),
        [".url"] = new IconStyle(LinkCode, null, FileTypeCategory.Other),

        // Code / config / data
        [".cs"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".cpp"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".c"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".h"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".js"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".ts"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".py"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".java"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".go"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".rs"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".php"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".rb"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".json"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".xml"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".html"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".css"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".yml"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".yaml"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".toml"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".ini"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".config"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),
        [".sql"] = new IconStyle(CodeCode, CodeColor, FileTypeCategory.Code),

        // Fonts
        [".ttf"] = new IconStyle(FontCode, null, FileTypeCategory.Font),
        [".otf"] = new IconStyle(FontCode, null, FileTypeCategory.Font),
    };

    public static string GetGlyph(FileSystemItem item)
    {
        var code = item.Kind switch
        {
            FileSystemItemKind.Drive => DriveCode,
            FileSystemItemKind.Directory => FolderCode,
            _ => ExtensionStyles.TryGetValue(item.Extension, out var style) ? style.Code : DocumentCode,
        };
        return G(code);
    }

    public static string? GetColorHex(FileSystemItem item)
    {
        return item.Kind switch
        {
            FileSystemItemKind.Drive => null,
            FileSystemItemKind.Directory => FolderColor,
            _ => ExtensionStyles.TryGetValue(item.Extension, out var style) ? style.ColorHex : null,
        };
    }

    public static FileTypeCategory GetCategory(FileSystemItem item)
    {
        if (item.IsDirectory) return FileTypeCategory.Folder;
        return ExtensionStyles.TryGetValue(item.Extension, out var style) ? style.Category : FileTypeCategory.Other;
    }
}
