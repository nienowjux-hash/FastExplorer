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

    private sealed record IconStyle(int Code, string? ColorHex);

    private static readonly Dictionary<string, IconStyle> ExtensionStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documents - reuse the plain document glyph, tinted per app so the list
        // reads by color the way Explorer's real icons do, without extracting them.
        [".pdf"] = new IconStyle(PdfCode, PdfColor),
        [".doc"] = new IconStyle(DocumentCode, WordColor),
        [".docx"] = new IconStyle(DocumentCode, WordColor),
        [".docm"] = new IconStyle(DocumentCode, WordColor),
        [".rtf"] = new IconStyle(DocumentCode, WordColor),
        [".odt"] = new IconStyle(DocumentCode, WordColor),
        [".xls"] = new IconStyle(DocumentCode, ExcelColor),
        [".xlsx"] = new IconStyle(DocumentCode, ExcelColor),
        [".xlsm"] = new IconStyle(DocumentCode, ExcelColor),
        [".csv"] = new IconStyle(DocumentCode, ExcelColor),
        [".ods"] = new IconStyle(DocumentCode, ExcelColor),
        [".ppt"] = new IconStyle(DocumentCode, PowerPointColor),
        [".pptx"] = new IconStyle(DocumentCode, PowerPointColor),
        [".pptm"] = new IconStyle(DocumentCode, PowerPointColor),
        [".odp"] = new IconStyle(DocumentCode, PowerPointColor),

        // Images
        [".jpg"] = new IconStyle(PictureCode, PictureColor),
        [".jpeg"] = new IconStyle(PictureCode, PictureColor),
        [".png"] = new IconStyle(PictureCode, PictureColor),
        [".gif"] = new IconStyle(PictureCode, PictureColor),
        [".bmp"] = new IconStyle(PictureCode, PictureColor),
        [".svg"] = new IconStyle(PictureCode, PictureColor),
        [".webp"] = new IconStyle(PictureCode, PictureColor),
        [".ico"] = new IconStyle(PictureCode, PictureColor),
        [".heic"] = new IconStyle(PictureCode, PictureColor),

        // Audio
        [".mp3"] = new IconStyle(AudioCode, AudioColor),
        [".wav"] = new IconStyle(AudioCode, AudioColor),
        [".flac"] = new IconStyle(AudioCode, AudioColor),
        [".aac"] = new IconStyle(AudioCode, AudioColor),
        [".m4a"] = new IconStyle(AudioCode, AudioColor),
        [".ogg"] = new IconStyle(AudioCode, AudioColor),

        // Video
        [".mp4"] = new IconStyle(VideoCode, VideoColor),
        [".mkv"] = new IconStyle(VideoCode, VideoColor),
        [".avi"] = new IconStyle(VideoCode, VideoColor),
        [".mov"] = new IconStyle(VideoCode, VideoColor),
        [".wmv"] = new IconStyle(VideoCode, VideoColor),
        [".webm"] = new IconStyle(VideoCode, VideoColor),

        // Archives / disk images
        [".zip"] = new IconStyle(ArchiveCode, ArchiveColor),
        [".rar"] = new IconStyle(ArchiveCode, ArchiveColor),
        [".7z"] = new IconStyle(ArchiveCode, ArchiveColor),
        [".tar"] = new IconStyle(ArchiveCode, ArchiveColor),
        [".gz"] = new IconStyle(ArchiveCode, ArchiveColor),
        [".bz2"] = new IconStyle(ArchiveCode, ArchiveColor),
        [".iso"] = new IconStyle(ArchiveCode, ArchiveColor),

        // Executables / scripts / installers
        [".exe"] = new IconStyle(ExecutableCode, null),
        [".msi"] = new IconStyle(ExecutableCode, null),
        [".bat"] = new IconStyle(ExecutableCode, null),
        [".cmd"] = new IconStyle(ExecutableCode, null),
        [".ps1"] = new IconStyle(ExecutableCode, null),
        [".sh"] = new IconStyle(ExecutableCode, null),

        // Shortcuts
        [".lnk"] = new IconStyle(LinkCode, null),
        [".url"] = new IconStyle(LinkCode, null),

        // Code / config / data
        [".cs"] = new IconStyle(CodeCode, CodeColor),
        [".cpp"] = new IconStyle(CodeCode, CodeColor),
        [".c"] = new IconStyle(CodeCode, CodeColor),
        [".h"] = new IconStyle(CodeCode, CodeColor),
        [".js"] = new IconStyle(CodeCode, CodeColor),
        [".ts"] = new IconStyle(CodeCode, CodeColor),
        [".py"] = new IconStyle(CodeCode, CodeColor),
        [".java"] = new IconStyle(CodeCode, CodeColor),
        [".go"] = new IconStyle(CodeCode, CodeColor),
        [".rs"] = new IconStyle(CodeCode, CodeColor),
        [".php"] = new IconStyle(CodeCode, CodeColor),
        [".rb"] = new IconStyle(CodeCode, CodeColor),
        [".json"] = new IconStyle(CodeCode, CodeColor),
        [".xml"] = new IconStyle(CodeCode, CodeColor),
        [".html"] = new IconStyle(CodeCode, CodeColor),
        [".css"] = new IconStyle(CodeCode, CodeColor),
        [".yml"] = new IconStyle(CodeCode, CodeColor),
        [".yaml"] = new IconStyle(CodeCode, CodeColor),
        [".toml"] = new IconStyle(CodeCode, CodeColor),
        [".ini"] = new IconStyle(CodeCode, CodeColor),
        [".config"] = new IconStyle(CodeCode, CodeColor),
        [".sql"] = new IconStyle(CodeCode, CodeColor),

        // Fonts
        [".ttf"] = new IconStyle(FontCode, null),
        [".otf"] = new IconStyle(FontCode, null),
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
}
