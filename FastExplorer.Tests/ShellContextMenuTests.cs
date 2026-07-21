using FastExplorer.Services;
using Xunit;

namespace FastExplorer.Tests;

public class ShellContextMenuTests : IDisposable
{
    private readonly string _root;

    public ShellContextMenuTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FastExplorerShellMenuTests_" + Guid.NewGuid());
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
    public void TryCreate_ForOrdinaryFile_DoesNotThrowAndDisposesCleanly()
    {
        var path = Path.Combine(_root, "sample.txt");
        File.WriteAllText(path, "content");

        using var menu = ShellContextMenu.TryCreate(_root, "sample.txt", IntPtr.Zero);

        // Whether this machine has extra shell extensions registered or not, the
        // call must complete without throwing/crashing - that's what's under test.
        if (menu is not null)
        {
            Assert.NotEmpty(menu.Entries);
        }
    }

    [Fact]
    public void TryCreate_ForMissingItem_ReturnsNullInsteadOfThrowing()
    {
        using var menu = ShellContextMenu.TryCreate(_root, "does-not-exist.txt", IntPtr.Zero);

        Assert.Null(menu);
    }
}
