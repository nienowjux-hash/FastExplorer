using FastExplorer.Services;
using Xunit;

namespace FastExplorer.Tests;

// Exercises real COM interop (IShellFolder/IContextMenu) against the host machine's
// actual shell - needs a genuine interactive desktop session to behave, unlike every
// other test in this project. Tagged so CI (ci.yml/release.yml) can exclude it: these
// calls were observed to hang indefinitely (not fail) on a GitHub Actions Windows
// runner, apparently because it lacks whatever a real logged-in desktop session
// provides for STA-affine shell COM objects to complete cross-apartment calls.
[Trait("Category", "RequiresDesktop")]
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
