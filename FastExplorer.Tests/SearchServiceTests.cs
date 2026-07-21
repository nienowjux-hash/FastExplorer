using FastExplorer.Models;
using FastExplorer.Services;
using Xunit;

namespace FastExplorer.Tests;

public class SearchServiceTests : IDisposable
{
    private readonly string _root;

    public SearchServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FastExplorerSearchTests_" + Guid.NewGuid());
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
    public async Task SearchByNameAsync_FindsMatchingFilesRecursively()
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        File.WriteAllText(Path.Combine(_root, "report.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "nested", "report-final.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "unrelated.txt"), "x");

        var matches = new List<FileSystemItem>();
        await SearchService.SearchByNameAsync(_root, "report", matches.Add, CancellationToken.None);

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, m => m.Name == "report.txt");
        Assert.Contains(matches, m => m.Name == "report-final.txt");
    }

    [Fact]
    public async Task SearchByContentAsync_FindsFilesContainingQuery()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "the quick brown fox");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "nothing relevant here");

        var matches = new List<FileSystemItem>();
        await SearchService.SearchByContentAsync(_root, "quick brown", matches.Add, CancellationToken.None);

        var match = Assert.Single(matches);
        Assert.Equal("a.txt", match.Name);
    }

    [Fact]
    public async Task SearchByNameAsync_CanBeCancelled()
    {
        for (var i = 0; i < 50; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"file{i}.txt"), "x");
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => SearchService.SearchByNameAsync(_root, "file", _ => { }, cts.Token));
    }
}
