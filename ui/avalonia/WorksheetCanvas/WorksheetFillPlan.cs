namespace CyanCalc.Controls;

internal enum WorksheetFillDirection { RowsDown, RowsUp, ColumnsRight, ColumnsLeft }

// One decision drives the outline, engine endpoint and successful final selection.
// Port of outlineHandle.ts and util.ts::snapFillExtent at the pinned upstream revision.
internal sealed record WorksheetFillPlan(CellArea Source, WorksheetFillDirection Direction, int To)
{
    internal bool ByRows => Direction is WorksheetFillDirection.RowsDown or WorksheetFillDirection.RowsUp;
    private int LastRow => Source.Row + Source.Height - 1;
    private int LastColumn => Source.Column + Source.Width - 1;

    internal CellArea Preview => Direction switch
    {
        WorksheetFillDirection.RowsDown => new(Source.Sheet, LastRow + 1, Source.Column, Source.Width, To - LastRow),
        // Upstream's reverse outlines include the source's first row/column boundary.
        WorksheetFillDirection.RowsUp => new(Source.Sheet, To, Source.Column, Source.Width, Source.Row - To + 1),
        WorksheetFillDirection.ColumnsRight => new(Source.Sheet, Source.Row, LastColumn + 1, To - LastColumn, Source.Height),
        _ => new(Source.Sheet, Source.Row, To, Source.Column - To + 1, Source.Height)
    };

    internal CellArea Selection => ByRows
        ? new(Source.Sheet, Math.Min(Source.Row, To), Source.Column, Source.Width, Math.Max(LastRow, To) - Math.Min(Source.Row, To) + 1)
        : new(Source.Sheet, Source.Row, Math.Min(Source.Column, To), Math.Max(LastColumn, To) - Math.Min(Source.Column, To) + 1, Source.Height);

    internal static WorksheetFillPlan? Update(CellArea source, MergedCell[] merges, int row, int column, WorksheetFillPlan? previous)
    {
        var lastRow = source.Row + source.Height - 1;
        var lastColumn = source.Column + source.Width - 1;
        var rows = Math.Max(source.Row - row, Math.Max(0, row - lastRow));
        var columns = Math.Max(source.Column - column, Math.Max(0, column - lastColumn));
        // Strict distance comparisons in upstream retain the last outline on ties,
        // including a move back inside the source. A fresh gesture still has none.
        if (rows == columns) return previous?.Source == source ? previous : null;
        var direction = rows > columns
            ? (row < source.Row ? WorksheetFillDirection.RowsUp : WorksheetFillDirection.RowsDown)
            : (column < source.Column ? WorksheetFillDirection.ColumnsLeft : WorksheetFillDirection.ColumnsRight);
        var extent = SnapExtent(source, merges, direction, Math.Max(rows, columns));
        if (extent == 0) return null;
        var to = direction switch
        {
            WorksheetFillDirection.RowsDown => lastRow + extent,
            WorksheetFillDirection.RowsUp => source.Row - extent,
            WorksheetFillDirection.ColumnsRight => lastColumn + extent,
            _ => source.Column - extent
        };
        return new(source, direction, to);
    }

    private static int SnapExtent(CellArea source, MergedCell[] merges, WorksheetFillDirection direction, int extent)
    {
        var byRows = direction is WorksheetFillDirection.RowsDown or WorksheetFillDirection.RowsUp;
        var towardStart = direction is WorksheetFillDirection.RowsUp or WorksheetFillDirection.ColumnsLeft;
        var period = byRows ? source.Height : source.Width;
        bool CutsMerge(int filled)
        {
            var boundary = towardStart ? period - filled : filled;
            foreach (var merge in merges)
            {
                if (merge.Row >= source.Row + source.Height || merge.LastRow < source.Row ||
                    merge.Column >= source.Column + source.Width || merge.LastColumn < source.Column) continue;
                var start = byRows ? merge.Row - source.Row : merge.Column - source.Column;
                var end = byRows ? merge.LastRow - source.Row : merge.LastColumn - source.Column;
                if (start < boundary && end >= boundary) return true;
            }
            return false;
        }
        while (extent > 0 && extent % period != 0 && CutsMerge(extent % period)) extent--;
        return extent;
    }
}
