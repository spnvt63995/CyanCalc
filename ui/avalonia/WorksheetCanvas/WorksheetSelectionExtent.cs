using Avalonia;

namespace CyanCalc.Controls;

// drawCellOutline in worksheetCanvas.ts leaves the area outline's geometry in
// the DOM when hiding it. CSS visibility/clip do not remove its scroll overflow.
// These bounds belong to the worksheet element, including across sheet/document
// changes; only a subsequent range selection replaces the retained area.
internal sealed class WorksheetSelectionExtent
{
    private Size _areaOverflow;
    private (Guid Session, long Revision, int Sheet)? _dimensionsContext;
    private readonly List<double> _rows = [0], _columns = [0];

    internal Size Measure(UserModel model, WorkbookSnapshot snapshot, bool selecting, bool canEdit)
    {
        var context = (model.Identity, model.Revision, snapshot.View.Sheet);
        if (_dimensionsContext != context)
        {
            _dimensionsContext = context;
            _rows.Clear(); _rows.Add(0); _columns.Clear(); _columns.Add(0);
        }
        var area = CellArea.FromView(snapshot.View);
        var merge = snapshot.MergedCells.FirstOrDefault(x => x.Contains(snapshot.View.Row, snapshot.View.Column));
        var single = area.Width == 1 && area.Height == 1 || merge?.Area(snapshot.View.Sheet) == area;
        double handleX, handleY;
        if (single)
        {
            var row = merge?.LastRow ?? snapshot.View.Row;
            var column = merge?.LastColumn ?? snapshot.View.Column;
            handleX = Position(false, column) + Dimension(false, column);
            handleY = Position(true, row) + Dimension(true, row);
        }
        else
        {
            var lastRow = area.Row + area.Height - 1;
            var lastColumn = area.Column + area.Width - 1;
            handleX = FarEdge(false, area.Column, lastColumn);
            handleY = FarEdge(true, area.Row, lastRow);
            var omitRight = area.Column <= snapshot.FrozenColumns && lastColumn > snapshot.FrozenColumns && lastColumn < Math.Max(snapshot.FrozenColumns + 1, snapshot.View.LeftColumn) - 1;
            var omitBottom = area.Row <= snapshot.FrozenRows && lastRow > snapshot.FrozenRows && lastRow < Math.Max(snapshot.FrozenRows + 1, snapshot.View.TopRow) - 1;
            // padding=-1, content width=size-1, plus two 1px borders. While
            // selecting, the extra content pixel compensates for absent borders.
            _areaOverflow = new(Math.Max(0, handleX + (selecting || omitRight ? 0 : 1)),
                Math.Max(0, handleY + (selecting || omitBottom ? 0 : 1)));
        }
        // The 7px handle is centred at end-1px, including its 1px border.
        // Read-only CSS uses display:none; editing/scroll clipping only hides it.
        return new(Round(Math.Max(_areaOverflow.Width, canEdit ? handleX + 2.5 : 0)),
            Round(Math.Max(_areaOverflow.Height, canEdit ? handleY + 2.5 : 0)));

        double Prefix(bool rows, int count)
        {
            var values = rows ? _rows : _columns;
            while (values.Count <= count)
            {
                var indices = Enumerable.Range(values.Count, Math.Min(1024, count - values.Count + 1)).ToArray();
                var sizes = model.Dimensions(snapshot.View.Sheet, rows ? indices : [], rows ? [] : indices);
                foreach (var size in rows ? sizes.Rows : sizes.Columns) values.Add(values[^1] + Round(size));
            }
            return values[count];
        }
        double Dimension(bool rows, int index) => Prefix(rows, index) - Prefix(rows, index - 1);
        double Position(bool rows, int index)
        {
            var frozen = rows ? snapshot.FrozenRows : snapshot.FrozenColumns;
            var first = Math.Max(frozen + 1, rows ? snapshot.View.TopRow : snapshot.View.LeftColumn);
            var header = rows ? SheetLayout.ColumnHeader : SheetLayout.RowHeader;
            if (index <= frozen) return header + Prefix(rows, index - 1);
            return header + (frozen > 0 ? Prefix(rows, frozen) + 3 : 0) + Prefix(rows, index - 1) - Prefix(rows, first - 1);
        }
        double FarEdge(bool rows, int start, int end)
        {
            var origin = Position(rows, start);
            var far = Position(rows, end) + Dimension(rows, end);
            var frozen = rows ? snapshot.FrozenRows : snapshot.FrozenColumns;
            if (start <= frozen && end > frozen) far = Math.Max(far, Position(rows, frozen) + Dimension(rows, frozen));
            return origin + Math.Abs(far - origin);
        }
    }

    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
}
