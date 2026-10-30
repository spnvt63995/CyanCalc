using Avalonia;

namespace CyanCalc.Controls;

public sealed record AxisCell(int Index, double Start, double Size, bool Frozen)
{
    public double End => Start + Size;
}

// Geometry counterpart of worksheetCanvas.ts. No worksheet values or selection are stored here.
public sealed class SheetLayout
{
    public const double RowHeader = 30;
    public const double ColumnHeader = 28;
    public AxisCell[] Rows { get; private init; } = [];
    public AxisCell[] Columns { get; private init; } = [];
    public double FrozenBottom => Rows.LastOrDefault(x => x.Frozen) is { } row ? row.End + 3 : ColumnHeader;
    public double FrozenRight => Columns.LastOrDefault(x => x.Frozen) is { } column ? column.End + 3 : RowHeader;

    public static SheetLayout Create(UserModel session, WorkbookSnapshot state, Size size)
    {
        var sheet = state.View.Sheet;
        return new()
        {
            Rows = BuildAxis(state.FrozenRows, state.View.TopRow, CellAddress.LastRow, ColumnHeader, size.Height,
                indices => session.Dimensions(sheet, indices, []).Rows),
            Columns = BuildAxis(state.FrozenColumns, state.View.LeftColumn, CellAddress.LastColumn, RowHeader, size.Width,
                indices => session.Dimensions(sheet, [], indices).Columns)
        };
    }

    private static AxisCell[] BuildAxis(int frozen, int start, int last, double offset, double limit, Func<int[], double[]> dimensions)
    {
        var result = new List<AxisCell>();
        Append(1, Math.Min(frozen, last), true);
        if (frozen > 0) offset += 3;
        Append(Math.Max(frozen + 1, start), last, false);
        return result.ToArray();

        void Append(int from, int to, bool isFrozen)
        {
            // Batches skip hidden runs without creating a control per row/column.
            for (int index = from; index <= to && offset < limit;)
            {
                var indices = Enumerable.Range(index, Math.Min(256, to - index + 1)).ToArray();
                var values = dimensions(indices);
                for (int i = 0; i < values.Length && offset < limit; i++)
                {
                    var extent = values[i];
                    if (extent > 0)
                    {
                        result.Add(new(indices[i], offset, extent, isFrozen));
                        offset += extent;
                    }
                }
                index += indices.Length;
            }
        }
    }

    public (int Row, int Column) HitTest(Point point) =>
        (Rows.FirstOrDefault(x => point.Y >= x.Start && point.Y < x.End)?.Index ?? 0,
         Columns.FirstOrDefault(x => point.X >= x.Start && point.X < x.End)?.Index ?? 0);

    // Captured cell/fill drags can reach cells outside the visible axes. Header
    // clicks and resize handles still use HitTest, where zero identifies a header.
    internal (int Row, int Column) HitTestDrag(UserModel session, WorkbookSnapshot state, Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return (0, 0);
        var hit = HitTest(point);
        return (hit.Row != 0 ? hit.Row : DragIndex(session, state.View.Sheet, true, Rows,
                    state.FrozenRows, state.View.TopRow, point.Y),
                hit.Column != 0 ? hit.Column : DragIndex(session, state.View.Sheet, false, Columns,
                    state.FrozenColumns, state.View.LeftColumn, point.X));
    }

    private static int DragIndex(UserModel session, int sheet, bool rows, AxisCell[] visible,
        int frozen, int start, double position)
    {
        var last = rows ? CellAddress.LastRow : CellAddress.LastColumn;
        var header = rows ? ColumnHeader : RowHeader;
        start = Math.Max(frozen + 1, start);
        var offset = header;
        if (position < header)
        {
            // Upstream walks backwards from the scrolling origin, even when the
            // pointer is over the header of a frozen pane. Exact preceding edges
            // belong to the previous cell in this backwards walk.
            for (var end = Math.Min(start - 1, last); end >= 1;)
            {
                var from = Math.Max(1, end - 255);
                var sizes = ReadSizes(from, end);
                for (var i = sizes.Length - 1; i >= 0; i--)
                {
                    offset -= sizes[i];
                    if (offset < position) return from + i;
                }
                end = from - 1;
            }
            return 1;
        }

        // Resume at cached edges so normal visible drags need no model queries,
        // and a pointer just outside the viewport reads only the next batch.
        var frozenEdge = visible.LastOrDefault(cell => cell.Frozen);
        offset = frozenEdge?.End ?? header;
        if (position < offset) return 0;
        var index = (frozenEdge?.Index ?? 0) + 1;
        var hit = Forward(index, frozen);
        if (hit != 0) return hit;
        if (frozen > 0) offset += 3;
        if (position < offset) return 0; // The frozen separator is not a cell.

        var scrollingEdge = visible.LastOrDefault(cell => !cell.Frozen);
        if (scrollingEdge is not null && position >= scrollingEdge.End)
        {
            offset = scrollingEdge.End;
            start = scrollingEdge.Index + 1;
        }
        hit = Forward(start, last);
        // Match getCellByCoordinates: rows stop at the final row; columns beyond
        // XFD have no target. Neither case can issue an out-of-range model query.
        return hit != 0 ? hit : rows ? last : 0;

        double[] ReadSizes(int from, int to)
        {
            var indices = Enumerable.Range(from, to - from + 1).ToArray();
            var dimensions = session.Dimensions(sheet, rows ? indices : [], rows ? [] : indices);
            return rows ? dimensions.Rows : dimensions.Columns;
        }

        int Forward(int from, int to)
        {
            while (from <= to)
            {
                var end = Math.Min(to, from + 255);
                var sizes = ReadSizes(from, end);
                for (var i = 0; i < sizes.Length; i++)
                {
                    offset += sizes[i];
                    if (position < offset) return from + i;
                }
                from = end + 1;
            }
            return 0;
        }
    }

    public Rect? CellRect(int row, int column)
    {
        var r = Rows.FirstOrDefault(x => x.Index == row);
        var c = Columns.FirstOrDefault(x => x.Index == column);
        return r is null || c is null ? null : new Rect(c.Start, r.Start, c.Size, r.Size);
    }

    public IEnumerable<Rect> AreaRects(CellArea area)
    {
        // Split at the frozen/scrolling boundary; selection outlines must never bridge a hidden gap.
        foreach (var rowGroup in Rows.Where(x => x.Index >= area.Row && x.Index < area.Row + area.Height).GroupBy(x => x.Frozen))
        foreach (var columnGroup in Columns.Where(x => x.Index >= area.Column && x.Index < area.Column + area.Width).GroupBy(x => x.Frozen))
            yield return new Rect(columnGroup.First().Start, rowGroup.First().Start,
                columnGroup.Last().End - columnGroup.First().Start, rowGroup.Last().End - rowGroup.First().Start);
    }
}
