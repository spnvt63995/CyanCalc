using Avalonia;

namespace CyanCalc.Controls;

// One rectangle per visible pane, using the complete merge's dimensions even
// when its anchor is offscreen. Only the pane clip changes while scrolling.
internal sealed record MergedCellRegion(MergedCell Merge, CellSnapshot Anchor, Rect Bounds, Rect Clip, AxisCell Column);

internal static class MergedCellLayout
{
    internal static List<MergedCellRegion> Create(UserModel session, WorkbookSnapshot snapshot, SheetLayout layout, Size size)
    {
        var result = new List<MergedCellRegion>();
        var heights = layout.Rows.ToDictionary(x => x.Index, x => x.Size);
        var widths = layout.Columns.ToDictionary(x => x.Index, x => x.Size);
        foreach (var merge in snapshot.MergedCells)
        {
            var rows = layout.Rows.Where(x => x.Index >= merge.Row && x.Index <= merge.LastRow).ToArray();
            var columns = layout.Columns.Where(x => x.Index >= merge.Column && x.Index <= merge.LastColumn).ToArray();
            if (rows.Length == 0 || columns.Length == 0) continue;
            var cell = session.Cell(snapshot.View.Sheet, merge.Row, merge.Column);
            var width = Span(false, merge.Column, merge.LastColumn);
            var height = Span(true, merge.Row, merge.LastRow);
            foreach (var rg in rows.GroupBy(x => x.Frozen))
            foreach (var cg in columns.GroupBy(x => x.Frozen))
            {
                var r = rg.First(); var c = cg.First();
                var rect = new Rect(c.Start - Span(false, merge.Column, c.Index - 1),
                    r.Start - Span(true, merge.Row, r.Index - 1), width, height);
                var left = c.Frozen ? SheetLayout.RowHeader : layout.FrozenRight;
                var top = r.Frozen ? SheetLayout.ColumnHeader : layout.FrozenBottom;
                var right = c.Frozen ? layout.FrozenRight : size.Width;
                var bottom = r.Frozen ? layout.FrozenBottom : size.Height;
                result.Add(new(merge, cell, rect, new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)), c));
            }
        }
        return result;

        double Span(bool row, int from, int to)
        {
            if (to < from) return 0;
            var cache = row ? heights : widths;
            double total = 0;
            foreach (var indices in Enumerable.Range(from, to - from + 1).Chunk(256))
            {
                var missing = indices.Where(x => !cache.ContainsKey(x)).ToArray();
                if (missing.Length != 0)
                {
                    var dimensions = session.Dimensions(snapshot.View.Sheet, row ? missing : [], row ? [] : missing);
                    var values = row ? dimensions.Rows : dimensions.Columns;
                    for (var i = 0; i < missing.Length; i++) cache[missing[i]] = values[i];
                }
                foreach (var index in indices) total += cache[index];
            }
            return total;
        }
    }
}
