using Avalonia;

namespace CyanCalc.Tabular;

// Indices are one-based display positions, never row identities. Int64 positions
// and caller-supplied bounds avoid importing worksheet limits into DB results.
public sealed record GridAxisCell(long Index, double Start, double Size, bool Frozen)
{
    public double End => Start + Size;
}

public static class GridAxis
{
    public static GridAxisCell[] Build(long frozen, long start, long last, double offset, double limit,
        Func<long[], double[]> dimensions, double scrollOffset = 0)
    {
        if (frozen < 0 || start < 1 || last < 0 || !double.IsFinite(offset) || !double.IsFinite(limit) || !double.IsFinite(scrollOffset) || scrollOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(start));
        var result = new List<GridAxisCell>();
        Append(1, Math.Min(frozen, last), true);
        if (frozen > 0) offset += 3;
        offset -= scrollOffset;
        if (frozen < long.MaxValue) Append(Math.Max(frozen + 1, start), last, false);
        return result.ToArray();

        void Append(long from, long to, bool isFrozen)
        {
            for (var index = from; index <= to && offset < limit;)
            {
                var count = (int)Math.Min(256, to - index + 1);
                var indices = Enumerable.Range(0, count).Select(n => index + n).ToArray();
                var values = dimensions(indices);
                if (values.Length != count) throw new ArgumentException("The dimension source must return one size per index.");
                for (var i = 0; i < values.Length && offset < limit; i++)
                {
                    var extent = values[i];
                    if (!double.IsFinite(extent) || extent < 0) throw new ArgumentException("Axis sizes must be finite and nonnegative.");
                    if (extent > 0) { result.Add(new(indices[i], offset, extent, isFrozen)); offset += extent; }
                }
                if (to - index < count) break;
                index += count;
            }
        }
    }

    public static long HitTest(IReadOnlyList<GridAxisCell> cells, double position) =>
        cells.FirstOrDefault(x => position >= x.Start && position < x.End)?.Index ?? 0;

    public static IEnumerable<Rect> Rectangles(IReadOnlyList<GridAxisCell> rows, IReadOnlyList<GridAxisCell> columns,
        long firstRow, long lastRow, long firstColumn, long lastColumn)
    {
        var frozenRight = columns.LastOrDefault(c => c.Frozen)?.End + 3;
        var frozenBottom = rows.LastOrDefault(r => r.Frozen)?.End + 3;
        foreach (var rg in rows.Where(x => x.Index >= firstRow && x.Index <= lastRow).GroupBy(x => x.Frozen))
        foreach (var cg in columns.Where(x => x.Index >= firstColumn && x.Index <= lastColumn).GroupBy(x => x.Frozen))
        {
            var left = cg.Key ? cg.First().Start : Math.Max(cg.First().Start, frozenRight ?? cg.First().Start);
            var top = rg.Key ? rg.First().Start : Math.Max(rg.First().Start, frozenBottom ?? rg.First().Start);
            if (cg.Last().End > left && rg.Last().End > top)
                yield return new Rect(left, top, cg.Last().End - left, rg.Last().End - top);
        }
    }
}
