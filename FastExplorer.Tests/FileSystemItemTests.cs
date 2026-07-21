using FastExplorer.Models;
using Xunit;

namespace FastExplorer.Tests;

public class FileSystemItemTests
{
    [Fact]
    public void SizeDisplay_ForDriveWithUsageInfo_ShowsUsedOfTotal()
    {
        var item = new FileSystemItem
        {
            Name = "C:",
            FullPath = "C:\\",
            Kind = FileSystemItemKind.Drive,
            DriveTotalBytes = 500L * 1024 * 1024 * 1024,
            DriveFreeBytes = 120L * 1024 * 1024 * 1024,
        };

        Assert.Equal("380 GB used of 500 GB", item.SizeDisplay);
    }

    [Fact]
    public void SizeDisplay_ForDriveWithoutUsageInfo_IsEmpty()
    {
        var item = new FileSystemItem
        {
            Name = "D:",
            FullPath = "D:\\",
            Kind = FileSystemItemKind.Drive,
        };

        Assert.Equal(string.Empty, item.SizeDisplay);
    }

    [Fact]
    public void SizeDisplay_ForDirectory_IsEmpty()
    {
        var item = new FileSystemItem
        {
            Name = "folder",
            FullPath = @"C:\folder",
            Kind = FileSystemItemKind.Directory,
        };

        Assert.Equal(string.Empty, item.SizeDisplay);
    }

    [Fact]
    public void SizeDisplay_ForFile_FormatsBytes()
    {
        var item = new FileSystemItem
        {
            Name = "file.txt",
            FullPath = @"C:\file.txt",
            Kind = FileSystemItemKind.File,
            SizeBytes = 2048,
        };

        Assert.Equal("2 KB", item.SizeDisplay);
    }
}
