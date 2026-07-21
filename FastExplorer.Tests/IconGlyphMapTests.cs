using FastExplorer.Models;
using Xunit;

namespace FastExplorer.Tests;

public class IconGlyphMapTests
{
    private static FileSystemItem MakeFile(string name) => new()
    {
        Name = name,
        FullPath = @"C:\" + name,
        Kind = FileSystemItemKind.File,
        Extension = Path.GetExtension(name),
    };

    [Fact]
    public void GetColorHex_ForWordDocument_ReturnsWordBlue()
    {
        var item = MakeFile("report.docx");

        Assert.Equal("#2B579A", item.IconColorHex);
    }

    [Fact]
    public void GetColorHex_ForExcelDocument_ReturnsExcelGreen()
    {
        var item = MakeFile("budget.xlsx");

        Assert.Equal("#217346", item.IconColorHex);
    }

    [Fact]
    public void GetColorHex_ForUnknownExtension_ReturnsNull()
    {
        var item = MakeFile("data.xyz123");

        Assert.Null(item.IconColorHex);
    }

    [Fact]
    public void GetColorHex_ForDirectory_ReturnsFolderColor()
    {
        var item = new FileSystemItem { Name = "folder", FullPath = @"C:\folder", Kind = FileSystemItemKind.Directory };

        Assert.Equal("#FFCA28", item.IconColorHex);
    }

    [Fact]
    public void GetGlyph_ForUnknownExtension_FallsBackToDocumentGlyph()
    {
        var known = MakeFile("readme.txt");
        var unknown = MakeFile("data.xyz123");

        Assert.Equal(known.IconGlyph, unknown.IconGlyph);
    }
}
