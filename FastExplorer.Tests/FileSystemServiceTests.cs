using System.Diagnostics;
using System.Runtime.InteropServices;
using FastExplorer.Models;
using FastExplorer.Services;
using Xunit;

namespace FastExplorer.Tests;

public class FileSystemServiceTests : IDisposable
{
    private readonly string _root;

    public FileSystemServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FastExplorerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task GetDrivesAsync_CompletesQuickly_EvenWithSlowOrMissingDrives()
    {
        var drives = new List<FileSystemItem>();
        var sw = Stopwatch.StartNew();

        await FileSystemService.GetDrivesAsync(drives.Add);

        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"GetDrivesAsync took {sw.Elapsed}, expected well under 5s.");
        Assert.NotEmpty(drives);
    }

    [Fact]
    public void CreateFolder_CreatesDirectoryOnDisk()
    {
        FileSystemService.CreateFolder(_root, "New folder");

        Assert.True(Directory.Exists(Path.Combine(_root, "New folder")));
    }

    [Fact]
    public void EnumerateDirectory_ReturnsFilesAndFolders()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));

        var items = FileSystemService.EnumerateDirectory(_root).ToList();

        Assert.Contains(items, i => i.Name == "a.txt" && !i.IsDirectory);
        Assert.Contains(items, i => i.Name == "sub" && i.IsDirectory);
    }

    [Fact]
    public void Rename_File_ChangesNameOnDisk()
    {
        var path = Path.Combine(_root, "old.txt");
        File.WriteAllText(path, "content");
        var item = FileSystemService.ToItem(new FileInfo(path));

        FileSystemService.Rename(item, "new.txt");

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(_root, "new.txt")));
    }

    [Fact]
    public void Delete_Permanently_RemovesFileFromDisk()
    {
        var path = Path.Combine(_root, "todelete.txt");
        File.WriteAllText(path, "content");
        var item = FileSystemService.ToItem(new FileInfo(path));

        FileSystemService.Delete(item, permanently: true);

        Assert.False(File.Exists(path));
    }

    // Verifies via real Shell.Application COM automation against the host machine's
    // actual Recycle Bin - see ShellContextMenuTests for why this needs a genuine
    // interactive desktop session and is excluded from CI (ci.yml/release.yml). Every
    // other test in this class is a plain filesystem test and stays unfiltered.
    [Trait("Category", "RequiresDesktop")]
    [Fact]
    public void Delete_ToRecycleBin_ItemAppearsInRecycleBin()
    {
        var uniqueName = $"recycle_verify_{Guid.NewGuid():N}.txt";
        var path = Path.Combine(_root, uniqueName);
        File.WriteAllText(path, "content");
        var item = FileSystemService.ToItem(new FileInfo(path));

        FileSystemService.Delete(item, permanently: false);

        Assert.False(File.Exists(path));
        Assert.True(IsInRecycleBin(uniqueName), $"Expected '{uniqueName}' to be found in the Recycle Bin.");
    }

    // Uses the Shell.Application COM automation object (independent of our own
    // IFileOperation interop) to independently verify the file actually landed in
    // the Recycle Bin rather than being silently hard-deleted.
    private static bool IsInRecycleBin(string fileName)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application")!;
        var shell = Activator.CreateInstance(shellType)!;
        try
        {
            var recycleBin = shellType.InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { 10 })!;
            var recycleBinType = recycleBin.GetType();
            var items = recycleBinType.InvokeMember("Items", System.Reflection.BindingFlags.InvokeMethod, null, recycleBin, null)!;
            var itemsType = items.GetType();
            var count = (int)itemsType.InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, items, null)!;
            for (var i = 0; i < count; i++)
            {
                var recycledItem = itemsType.InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, items, new object[] { i })!;
                var name = (string)recycledItem.GetType().InvokeMember("Name", System.Reflection.BindingFlags.GetProperty, null, recycledItem, null)!;
                if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }

    [Fact]
    public void Copy_File_CreatesCopyAtDestination()
    {
        var sourceDir = Path.Combine(_root, "source");
        var destDir = Path.Combine(_root, "dest");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);
        var path = Path.Combine(sourceDir, "file.txt");
        File.WriteAllText(path, "content");
        var item = FileSystemService.ToItem(new FileInfo(path));

        FileSystemService.Copy(item, destDir);

        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(destDir, "file.txt")));
    }

    [Fact]
    public void Copy_Directory_RecursivelyCopiesContents()
    {
        var sourceDir = Path.Combine(_root, "source");
        var destDir = Path.Combine(_root, "dest");
        Directory.CreateDirectory(Path.Combine(sourceDir, "nested"));
        Directory.CreateDirectory(destDir);
        File.WriteAllText(Path.Combine(sourceDir, "top.txt"), "content");
        File.WriteAllText(Path.Combine(sourceDir, "nested", "inner.txt"), "content");
        var item = FileSystemService.ToItem(new DirectoryInfo(sourceDir));

        FileSystemService.Copy(item, destDir);

        Assert.True(File.Exists(Path.Combine(destDir, "source", "top.txt")));
        Assert.True(File.Exists(Path.Combine(destDir, "source", "nested", "inner.txt")));
    }

    [Fact]
    public void Move_File_RemovesFromSourceAndAddsToDestination()
    {
        var sourceDir = Path.Combine(_root, "source");
        var destDir = Path.Combine(_root, "dest");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);
        var path = Path.Combine(sourceDir, "file.txt");
        File.WriteAllText(path, "content");
        var item = FileSystemService.ToItem(new FileInfo(path));

        FileSystemService.Move(item, destDir);

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(destDir, "file.txt")));
    }

    [Fact]
    public void Copy_WithOverwrite_ReplacesExistingFile()
    {
        var sourceDir = Path.Combine(_root, "source");
        var destDir = Path.Combine(_root, "dest");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);
        var path = Path.Combine(sourceDir, "file.txt");
        File.WriteAllText(path, "new content");
        File.WriteAllText(Path.Combine(destDir, "file.txt"), "old content");
        var item = FileSystemService.ToItem(new FileInfo(path));

        FileSystemService.Copy(item, destDir, overwrite: true);

        Assert.Equal("new content", File.ReadAllText(Path.Combine(destDir, "file.txt")));
    }

    [Fact]
    public void Copy_WithDestinationName_UsesThatNameInsteadOfOriginal()
    {
        var sourceDir = Path.Combine(_root, "source");
        var destDir = Path.Combine(_root, "dest");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);
        var path = Path.Combine(sourceDir, "file.txt");
        File.WriteAllText(path, "content");
        var item = FileSystemService.ToItem(new FileInfo(path));

        FileSystemService.Copy(item, destDir, destinationName: "file (2).txt");

        Assert.True(File.Exists(Path.Combine(destDir, "file (2).txt")));
        Assert.False(File.Exists(Path.Combine(destDir, "file.txt")));
    }

    [Fact]
    public void MakeUniqueName_ReturnsOriginalName_WhenNoConflict()
    {
        var name = FileSystemService.MakeUniqueName(_root, "file.txt");

        Assert.Equal("file.txt", name);
    }

    [Fact]
    public void MakeUniqueName_AppendsCounter_WhenNameAlreadyExists()
    {
        File.WriteAllText(Path.Combine(_root, "file.txt"), "content");

        var name = FileSystemService.MakeUniqueName(_root, "file.txt");

        Assert.Equal("file (2).txt", name);
    }

    [Fact]
    public void MakeUniqueName_SkipsExistingCounters()
    {
        File.WriteAllText(Path.Combine(_root, "file.txt"), "content");
        File.WriteAllText(Path.Combine(_root, "file (2).txt"), "content");

        var name = FileSystemService.MakeUniqueName(_root, "file.txt");

        Assert.Equal("file (3).txt", name);
    }
}
