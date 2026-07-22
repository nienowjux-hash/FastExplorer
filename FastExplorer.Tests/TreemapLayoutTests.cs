using FastExplorer.Models;
using Xunit;

namespace FastExplorer.Tests;

public class TreemapLayoutTests
{
    private const double Tolerance = 0.01;

    [Fact]
    public void Compute_SingleItem_FillsEntireBounds()
    {
        var bounds = new TreemapRect(0, 0, 200, 100);

        var result = TreemapLayout.Compute(new double[] { 42 }, bounds);

        var rect = Assert.Single(result).Rect;
        Assert.Equal(bounds.Width, rect.Width, Tolerance);
        Assert.Equal(bounds.Height, rect.Height, Tolerance);
    }

    [Fact]
    public void Compute_MultipleItems_TotalAreaMatchesBoundsArea()
    {
        var bounds = new TreemapRect(0, 0, 300, 200);
        var sizes = new double[] { 500, 300, 120, 80, 40, 10 };

        var result = TreemapLayout.Compute(sizes, bounds);

        var totalArea = result.Sum(r => r.Rect.Width * r.Rect.Height);
        Assert.Equal(bounds.Width * bounds.Height, totalArea, 1.0);
    }

    [Fact]
    public void Compute_MultipleItems_EveryRectStaysWithinBounds()
    {
        var bounds = new TreemapRect(0, 0, 300, 200);
        var sizes = new double[] { 500, 300, 120, 80, 40, 10 };

        var result = TreemapLayout.Compute(sizes, bounds);

        foreach (var (_, rect) in result)
        {
            Assert.True(rect.X >= bounds.X - Tolerance);
            Assert.True(rect.Y >= bounds.Y - Tolerance);
            Assert.True(rect.X + rect.Width <= bounds.X + bounds.Width + Tolerance);
            Assert.True(rect.Y + rect.Height <= bounds.Y + bounds.Height + Tolerance);
        }
    }

    [Fact]
    public void Compute_ZeroAndNegativeSizes_AreDropped()
    {
        var bounds = new TreemapRect(0, 0, 300, 200);
        var sizes = new double[] { 100, 0, -5, 50 };

        var result = TreemapLayout.Compute(sizes, bounds);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(r.Index is 0 or 3));
    }

    [Fact]
    public void Compute_AllZeroSizes_ReturnsEmpty()
    {
        var bounds = new TreemapRect(0, 0, 300, 200);

        var result = TreemapLayout.Compute(new double[] { 0, 0 }, bounds);

        Assert.Empty(result);
    }

    [Fact]
    public void Compute_ZeroSizedBounds_ReturnsEmpty()
    {
        var result = TreemapLayout.Compute(new double[] { 10, 20 }, new TreemapRect(0, 0, 0, 100));

        Assert.Empty(result);
    }

    [Fact]
    public void Compute_LargeSizeSpread_KeepsCellsReasonablySquare()
    {
        // One huge item plus many tiny ones is exactly the shape a real folder
        // produces (one big video next to a pile of small config files) - the whole
        // point of squarifying is that even here, no cell degenerates into an
        // unreadable sliver.
        var bounds = new TreemapRect(0, 0, 400, 300);
        var sizes = new List<double> { 10_000 };
        sizes.AddRange(Enumerable.Repeat(5.0, 20));

        var result = TreemapLayout.Compute(sizes, bounds);

        Assert.Equal(21, result.Count);
        foreach (var (_, rect) in result)
        {
            var aspect = Math.Max(rect.Width, rect.Height) / Math.Min(rect.Width, rect.Height);
            Assert.True(aspect < 20, $"Expected a roughly square-ish cell, got aspect ratio {aspect} for rect {rect}");
        }
    }
}
