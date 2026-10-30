using Avalonia;
using Avalonia.Input;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    private bool ScrollCellEditorSelection(PointerEventArgs e)
    {
        if (!CanEdit || State.EditSession is not { } edit ||
            Snapshot is not { } snapshot || !edit.IsVisibleOnSheet(snapshot.View.Sheet)) return false;

        var point = e.GetPosition(Worksheet);
        var size = Worksheet.Bounds.Size;
        var editor = CellInput.Bounds;
        var x = Step(point.X, size.Width);
        var y = Step(point.Y, size.Height);
        if (edit.Cell.Column <= snapshot.FrozenColumns || x > 0 && editor.Right <= size.Width) x = 0;
        if (edit.Cell.Row <= snapshot.FrozenRows || y > 0 && editor.Bottom <= size.Height) y = 0;
        if (x == 0 && y == 0) return false;

        var before = Worksheet.ScrollOffset;
        // Use the viewport adapter directly: a worksheet command would commit the draft.
        Worksheet.SetScrollPosition(before + new Vector(x, y));
        return Worksheet.ScrollOffset != before;

        static double Step(double position, double extent) => position < 0
            ? Math.Clamp(position, -40, -8)
            : position > extent ? Math.Clamp(position - extent, 8, 40) : 0;
    }
}
