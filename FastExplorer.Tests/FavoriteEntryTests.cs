using System.Text.Json;
using FastExplorer.Services;
using Xunit;

namespace FastExplorer.Tests;

public class FavoriteEntryTests
{
    [Fact]
    public void RoundTrips_ThroughJson()
    {
        var favorites = new List<FavoriteEntry>
        {
            new() { Name = "Desktop", Path = @"C:\Users\test\Desktop" },
            new() { Name = "Documents", Path = @"C:\Users\test\Documents" },
        };

        var json = JsonSerializer.Serialize(favorites);
        var roundTripped = JsonSerializer.Deserialize<List<FavoriteEntry>>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(2, roundTripped!.Count);
        Assert.Equal("Desktop", roundTripped[0].Name);
        Assert.Equal(@"C:\Users\test\Desktop", roundTripped[0].Path);
    }
}
