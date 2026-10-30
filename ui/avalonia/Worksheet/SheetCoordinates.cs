using Avalonia;

namespace CyanCalc.Controls;

// worksheetCanvas.getCoordinatesByCell / getBoundedRow / getBoundedColumn.
internal static class SheetCoordinates
{
    internal static double Sum(UserModel model, int sheet, bool rows, int start, int end)
    {
        double sum = 0;
        for (var i = start; i < end; i += 256)
        {
            var indices = Enumerable.Range(i, Math.Min(256, end - i)).ToArray();
            var sizes = model.Dimensions(sheet, rows ? indices : [], rows ? [] : indices);
            sum += (rows ? sizes.Rows : sizes.Columns).Sum();
        }
        return sum;
    }

    internal static Point Position(UserModel model, WorkbookSnapshot state, int row, int column)
    {
        double Axis(bool rows, int index, int frozen, int first, double header)
        {
            if (index <= frozen) return Math.Floor(header + Sum(model, state.View.Sheet, rows, 1, index));
            first = Math.Max(frozen + 1, first); // getVisibleCells clamps the scrolling pane past frozen cells.
            var freezeSize = frozen > 0 ? Sum(model, state.View.Sheet, rows, 1, frozen + 1) + 3 : 0;
            var distance = index >= first ? Sum(model, state.View.Sheet, rows, first, index) : -Sum(model, state.View.Sheet, rows, index, first);
            return Math.Floor(header + freezeSize + distance);
        }
        return new(Axis(false, column, state.FrozenColumns, state.View.LeftColumn, SheetLayout.RowHeader),
            Axis(true, row, state.FrozenRows, state.View.TopRow, SheetLayout.ColumnHeader));
    }

    internal static int Bounded(UserModel model, int sheet, bool rows, int frozen, double offset)
    {
        var last = rows ? CellAddress.LastRow : CellAddress.LastColumn;
        double total = 0;
        for (var i = frozen + 1; i <= last; i += 256)
        {
            var indices = Enumerable.Range(i, Math.Min(256, last - i + 1)).ToArray();
            var dimensions = model.Dimensions(sheet, rows ? indices : [], rows ? [] : indices);
            var values = rows ? dimensions.Rows : dimensions.Columns;
            for (var j = 0; j < values.Length; j++)
            {
                if (total + values[j] > offset) return indices[j];
                total += values[j];
            }
        }
        return last;
    }
}
