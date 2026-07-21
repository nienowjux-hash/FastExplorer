using FastExplorer.Services;
using Xunit;

namespace FastExplorer.Tests;

public class PropertiesServiceTests : IDisposable
{
    private readonly string _root;

    public PropertiesServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FastExplorerPropsTests_" + Guid.NewGuid());
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
    public async Task CalculateFolderSizeAsync_SumsAllFilesRecursively()
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        File.WriteAllBytes(Path.Combine(_root, "a.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_root, "nested", "b.bin"), new byte[50]);

        var size = await PropertiesService.CalculateFolderSizeAsync(_root, CancellationToken.None);

        Assert.Equal(150, size);
    }

    [Fact]
    public async Task CalculateFolderSizeAsync_CanBeCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => PropertiesService.CalculateFolderSizeAsync(_root, cts.Token));
    }

    [Fact]
    public void SetAttributes_ThenGetAttributes_RoundTrips()
    {
        var path = Path.Combine(_root, "file.txt");
        File.WriteAllText(path, "content");

        PropertiesService.SetAttributes(path, readOnly: true, hidden: true);
        var (readOnly, hidden) = PropertiesService.GetAttributes(path);

        Assert.True(readOnly);
        Assert.True(hidden);

        // Clear ReadOnly again so Dispose() can delete the temp directory afterward.
        PropertiesService.SetAttributes(path, readOnly: false, hidden: false);
    }

    [Fact]
    public void GetAccessInfo_ReturnsOwnerAndAtLeastOneRule()
    {
        var path = Path.Combine(_root, "file.txt");
        File.WriteAllText(path, "content");

        var (owner, rules) = PropertiesService.GetAccessInfo(path, isDirectory: false);

        Assert.NotEqual("Unavailable", owner);
        Assert.NotEmpty(rules);
    }
}
