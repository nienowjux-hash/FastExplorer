namespace FastExplorer.Models;

public readonly record struct TreemapRect(double X, double Y, double Width, double Height);

// Squarified treemap layout (Bruls/Huizing/van Wijk, "Squarified Treemaps", 2000) -
// lays out a set of non-negative weights into a rectangle such that resulting cell
// aspect ratios stay close to square. This is what makes a treemap actually usable:
// a naive proportional slice-and-dice layout produces long, unreadable slivers for
// very large size ranges (a folder with one huge file and many tiny ones), which is
// exactly the kind of input Controls/DiskUsageView feeds it.
public static class TreemapLayout
{
    // Returns (original index into `sizes`, laid-out rect) pairs, one per non-zero
    // entry - indices with a zero/negative size are dropped since they'd occupy zero
    // area and only complicate the aspect-ratio math for no visual benefit.
    public static IReadOnlyList<(int Index, TreemapRect Rect)> Compute(IReadOnlyList<double> sizes, TreemapRect bounds)
    {
        var result = new List<(int Index, TreemapRect Rect)>();
        if (bounds.Width <= 0 || bounds.Height <= 0) return result;

        var indices = Enumerable.Range(0, sizes.Count)
            .Where(i => sizes[i] > 0)
            .OrderByDescending(i => sizes[i])
            .ToList();
        if (indices.Count == 0) return result;

        var totalSize = indices.Sum(i => sizes[i]);
        var area = bounds.Width * bounds.Height;
        // Scale weights so their sum equals the rect's area, matching the algorithm's
        // assumption that Sum(weights) == Width * Height.
        var scale = area / totalSize;

        Squarify(indices, sizes, scale, bounds, new List<int>(), result);
        return result;
    }

    private static void Squarify(List<int> remaining, IReadOnlyList<double> sizes, double scale, TreemapRect rect, List<int> row, List<(int, TreemapRect)> result)
    {
        while (true)
        {
            if (remaining.Count == 0)
            {
                if (row.Count > 0) LayoutRow(row, sizes, scale, rect, result);
                return;
            }

            var shortSide = Math.Min(rect.Width, rect.Height);
            var next = remaining[0];
            var rowWithNext = new List<int>(row) { next };

            if (row.Count == 0 || Worst(row, sizes, scale, shortSide) >= Worst(rowWithNext, sizes, scale, shortSide))
            {
                remaining.RemoveAt(0);
                row = rowWithNext;
                continue;
            }

            rect = LayoutRow(row, sizes, scale, rect, result);
            row = new List<int>();
        }
    }

    // The "worst" (highest, i.e. furthest from square) aspect ratio among the
    // rectangles a row would produce if laid out along `shortSide` right now.
    private static double Worst(List<int> row, IReadOnlyList<double> sizes, double scale, double shortSide)
    {
        var areas = row.Select(i => sizes[i] * scale).ToList();
        var sum = areas.Sum();
        var max = areas.Max();
        var min = areas.Min();
        var s2 = shortSide * shortSide;
        return Math.Max(s2 * max / (sum * sum), sum * sum / (s2 * min));
    }

    // Lays out `row` as a single strip along the shorter side of `rect` (a column if
    // rect is wider than tall, a row of cells if rect is taller than wide), appends
    // each cell's rect to `result`, and returns whatever of `rect` is left over.
    private static TreemapRect LayoutRow(List<int> row, IReadOnlyList<double> sizes, double scale, TreemapRect rect, List<(int, TreemapRect)> result)
    {
        var rowArea = row.Sum(i => sizes[i] * scale);

        if (rect.Width >= rect.Height)
        {
            var stripWidth = rect.Height > 0 ? rowArea / rect.Height : 0;
            var y = rect.Y;
            foreach (var i in row)
            {
                var h = stripWidth > 0 ? (sizes[i] * scale) / stripWidth : 0;
                result.Add((i, new TreemapRect(rect.X, y, stripWidth, h)));
                y += h;
            }
            return new TreemapRect(rect.X + stripWidth, rect.Y, Math.Max(0, rect.Width - stripWidth), rect.Height);
        }
        else
        {
            var stripHeight = rect.Width > 0 ? rowArea / rect.Width : 0;
            var x = rect.X;
            foreach (var i in row)
            {
                var w = stripHeight > 0 ? (sizes[i] * scale) / stripHeight : 0;
                result.Add((i, new TreemapRect(x, rect.Y, w, stripHeight)));
                x += w;
            }
            return new TreemapRect(rect.X, rect.Y + stripHeight, rect.Width, Math.Max(0, rect.Height - stripHeight));
        }
    }
}
