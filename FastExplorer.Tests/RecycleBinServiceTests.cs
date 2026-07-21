using FastExplorer.Services;
using Xunit;

namespace FastExplorer.Tests;

public class RecycleBinServiceTests : IDisposable
{
    private readonly string _root;

    public RecycleBinServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FastExplorerRestoreTests_" + Guid.NewGuid());
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
    public void TryRestore_AfterDelete_BringsFileBack()
    {
        var uniqueName = $"undo_verify_{Guid.NewGuid():N}.txt";
        var path = Path.Combine(_root, uniqueName);
        File.WriteAllText(path, "content");
        var item = FileSystemService.ToItem(new FileInfo(path));

        FileSystemService.Delete(item, permanently: false);
        Assert.False(File.Exists(path));

        var restored = WaitForRestore(path, TimeSpan.FromSeconds(5));

        Assert.True(restored, $"Expected '{path}' to be restored from the Recycle Bin.");
        Assert.True(File.Exists(path));
    }

    private static bool WaitForRestore(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (RecycleBinService.TryRestore(path))
            {
                // InvokeVerb("undelete") can complete asynchronously - give the
                // shell a brief moment to actually move the file back.
                for (var i = 0; i < 20 && !File.Exists(path); i++)
                {
                    Thread.Sleep(100);
                }
                return File.Exists(path);
            }
            Thread.Sleep(200);
        }
        return false;
    }
}
