using FastExplorer.Services;
using Xunit;

namespace FastExplorer.Tests;

public class NetworkDriveServiceTests
{
    [Fact]
    public void GetAvailableDriveLetters_ExcludesLettersAlreadyInUse()
    {
        var used = new HashSet<char>(DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])));

        var available = NetworkDriveService.GetAvailableDriveLetters();

        Assert.All(available, letter => Assert.DoesNotContain(letter[0], used));
    }

    [Fact]
    public void GetAvailableDriveLetters_ReturnsWellFormedDriveLetters()
    {
        var available = NetworkDriveService.GetAvailableDriveLetters();

        Assert.All(available, letter => Assert.Matches("^[A-Z]:$", letter));
    }
}
