using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace CyanCalc.Controls;

// A draw call receives values and cached geometry only: no workbook, engine or input control.
// Collections are read-only views of the canvas cache for this synchronous UI-thread draw.
internal readonly record struct WorksheetRenderState(
    Size Size, SheetPalette Palette, SheetLayout Layout, WorkbookSnapshot? Snapshot,
    IReadOnlyDictionary<(int, int), CellSnapshot> Cells,
    IReadOnlyDictionary<(int, int), MergedCell> MergeMap,
    IReadOnlyList<MergedCellRegion> MergedRegions,
    IReadOnlyList<(CellSnapshot Cell, AxisCell Column)> OverflowCells,
    bool IsEditing, bool SelectingRange, CellArea? CutRange, CellArea? FillPreview,
    IReadOnlyList<FormulaReference> References, Rect? FillHandle, (bool Row, double Position)? ResizeGuide);

// Drawing order and clipping follow the pinned worksheetCanvas.ts port.
internal sealed class WorksheetRenderer(WorksheetRenderState state, WorksheetRenderResources resources)
{
    private SheetPalette _palette => state.Palette;
    private SheetLayout Layout => state.Layout;
    private IReadOnlyDictionary<(int, int), CellSnapshot> _cells => state.Cells;
    private IReadOnlyDictionary<(int, int), MergedCell> _mergeMap => state.MergeMap;
    private IReadOnlyList<MergedCellRegion> _mergedRegions => state.MergedRegions;
    private IReadOnlyList<(CellSnapshot Cell, AxisCell Column)> _overflowCells => state.OverflowCells;
    private Pen GridPen => resources.GridPen(_palette);

    internal void Draw(DrawingContext context)
    {
        context.FillRectangle(_palette.Background, new Rect(state.Size));
        if (state.Snapshot is not { } snapshot) return;
        var area = CellArea.FromView(snapshot.View);
        foreach (var row in Layout.Rows)
        foreach (var column in Layout.Columns)
        {
            if (!_cells.TryGetValue((row.Index, column.Index), out var cell)) continue;
            if (_mergeMap.ContainsKey((row.Index, column.Index))) continue;
            var rect = new Rect(column.Start, row.Start, column.Size, row.Size);
            if (cell.ExtendedStyle.Style.Fill.Color is { } fill) context.FillRectangle(ConditionalRenderer.Brush(fill, "#FFFFFF"), rect);
            if (snapshot.ShowGridLines) context.DrawRectangle(null, GridPen, rect);
            if (cell.ExtendedStyle.DataBar is { } bar) ConditionalRenderer.DataBar(context, rect, bar);
        }
        foreach (var region in _mergedRegions)
        {
            using var clip = context.PushClip(region.Clip);
            context.FillRectangle(region.Anchor.ExtendedStyle.Style.Fill.Color is { } fill
                ? ConditionalRenderer.Brush(fill) : _palette.Background, region.Bounds);
            if (snapshot.ShowGridLines) context.DrawRectangle(null, GridPen, region.Bounds);
            if (region.Anchor.ExtendedStyle.DataBar is { } bar) ConditionalRenderer.DataBar(context, region.Bounds, bar);
        }
        foreach (var overflow in _overflowCells)
        {
            var row = Layout.Rows.First(x => x.Index == overflow.Cell.Row);
            DrawCell(context, overflow.Cell, new Rect(overflow.Column.Start, row.Start, overflow.Column.Size, row.Size), overflow.Column);
        }
        foreach (var row in Layout.Rows)
        foreach (var column in Layout.Columns)
        {
            if (!_cells.TryGetValue((row.Index, column.Index), out var cell)) continue;
            var rect = new Rect(column.Start, row.Start, column.Size, row.Size);
            if (_mergeMap.TryGetValue((row.Index, column.Index), out var merge))
            {
                var border = cell.ExtendedStyle.Style.Border;
                if (row.Index == merge.Row) ConditionalRenderer.Border(context, border.Top, rect.TopLeft, rect.TopRight);
                if (row.Index == merge.LastRow) ConditionalRenderer.Border(context, border.Bottom, rect.BottomLeft, rect.BottomRight);
                if (column.Index == merge.Column) ConditionalRenderer.Border(context, border.Left, rect.TopLeft, rect.BottomLeft);
                if (column.Index == merge.LastColumn) ConditionalRenderer.Border(context, border.Right, rect.TopRight, rect.BottomRight);
            }
            else
            {
                DrawCell(context, cell, rect, column);
                DrawBorders(context, rect, cell.ExtendedStyle.Style.Border);
            }
        }
        foreach (var region in _mergedRegions)
        {
            using var clip = context.PushClip(region.Clip);
            DrawCell(context, region.Anchor, region.Bounds, region.Column, false);
            var border = region.Anchor.ExtendedStyle.Style.Border;
            if (border.DiagonalDown) ConditionalRenderer.Border(context, border.Diagonal, region.Bounds.TopLeft, region.Bounds.BottomRight);
            if (border.DiagonalUp) ConditionalRenderer.Border(context, border.Diagonal, region.Bounds.BottomLeft, region.Bounds.TopRight);
        }
        var selectedMerge = snapshot.MergedCells.FirstOrDefault(x => x.Contains(snapshot.View.Row, snapshot.View.Column));
        var isSingleCell = area.Width == 1 && area.Height == 1 || selectedMerge?.Area(snapshot.View.Sheet) == area;
        // Match upstream drawCellOutline + worksheet.css: the active cell is
        // outlined first; the translucent area overlay is only for a range.
        if (!state.IsEditing)
        {
            if (selectedMerge is not null)
            {
                foreach (var region in _mergedRegions.Where(x => x.Merge == selectedMerge))
                {
                    using var clip = context.PushClip(region.Clip);
                    DrawActiveCellOutline(context, region.Bounds);
                }
            }
            else if (Layout.CellRect(snapshot.View.Row, snapshot.View.Column) is { } active)
                DrawActiveCellOutline(context, active);
        }
        if (!isSingleCell)
        {
            foreach (var rect in Layout.AreaRects(area))
            {
                context.FillRectangle(_palette.Selection, rect);
                if (!state.SelectingRange) context.DrawRectangle(null, new Pen(_palette.Accent, 1), rect.Deflate(.5), 1, 1);
            }
        }
        DrawRange(context, state.CutRange, new Pen(_palette.Accent, 1, new DashStyle([4, 3], 0)));
        DrawRange(context, state.FillPreview, new Pen(_palette.Accent, 1, new DashStyle([3, 2], 0)));
        foreach (var reference in state.References)
            DrawReference(context, reference);
        DrawArrayOutline(context, snapshot.Cell);
        if (state.FillHandle is { } handle) context.FillRectangle(_palette.Accent, handle);
        DrawHeaders(context, area);
        if (Layout.FrozenRight > SheetLayout.RowHeader) context.DrawLine(new Pen(_palette.Separator, 3), new Point(Layout.FrozenRight - 1.5, 0), new Point(Layout.FrozenRight - 1.5, state.Size.Height));
        if (Layout.FrozenBottom > SheetLayout.ColumnHeader) context.DrawLine(new Pen(_palette.Separator, 3), new Point(0, Layout.FrozenBottom - 1.5), new Point(state.Size.Width, Layout.FrozenBottom - 1.5));
        if (state.ResizeGuide is { } resize)
        {
            var position = resize.Position;
            context.DrawLine(new Pen(_palette.Accent, 1), resize.Row ? new Point(0, position) : new Point(position, 0),
                resize.Row ? new Point(state.Size.Width, position) : new Point(position, state.Size.Height));
        }
    }

    private void DrawActiveCellOutline(DrawingContext context, Rect rect)
    {
        if (rect.Width <= 2 || rect.Height <= 2) return;
        context.DrawRectangle(null, new Pen(_palette.Accent, 2), rect.Deflate(1), 3, 3);
        // CSS: box-shadow: inset 0 0 0 1px white, inside the 2px border.
        if (rect.Width > 6 && rect.Height > 6)
            context.DrawRectangle(null, new Pen(Brushes.White, 1), rect.Deflate(2.5), 1, 1);
    }

    private void DrawCell(DrawingContext context, CellSnapshot cell, Rect rect, AxisCell column, bool allowOverflow = true)
    {
        var style = cell.ExtendedStyle.Style;
        var extended = cell.ExtendedStyle;
        var iconSize = style.Font.Sz > 0 ? style.Font.Sz * 1.2 : 16;
        using (context.PushClip(rect))
        {
            if (extended.Icon is { } icon) ConditionalRenderer.Icon(context, icon.Icon, icon.Color, new Point(rect.X + 3, rect.Y + (rect.Height - iconSize) / 2), iconSize);
            if (extended.Rating is { } rating) ConditionalRenderer.Rating(context, rect, rating, iconSize);
        }
        if (cell.Display.Length == 0 || extended.Icon?.ShowValue == false || extended.DataBar?.ShowValue == false || extended.Rating?.ShowValue == false) return;
        var alignment = style.Alignment ?? new();
        var horizontal = alignment.Horizontal.ToLowerInvariant();
        if (horizontal == "general") horizontal = cell.CellType == 1 ? "right" : cell.CellType is 4 or 16 ? "center" : "left";
        var textArea = rect.Deflate(new Thickness(4, 1));
        if (extended.Icon is not null) textArea = new Rect(textArea.X + iconSize + 4, textArea.Y, Math.Max(0, textArea.Width - iconSize - 4), textArea.Height);
        if (extended.Rating is { } r) textArea = new Rect(textArea.X + r.Count * iconSize, textArea.Y, Math.Max(0, textArea.Width - r.Count * iconSize), textArea.Height);
        if (textArea.Width <= 0) return;
        var clip = rect;
        if (allowOverflow && !alignment.WrapText)
        {
            foreach (var next in Layout.Columns.Where(x => x.Index > column.Index && x.Frozen == column.Frozen))
            {
                if (_mergeMap.ContainsKey((cell.Row, next.Index)) || _cells.TryGetValue((cell.Row, next.Index), out var adjacent) && adjacent.Display.Length != 0) break;
                clip = new Rect(clip.X, clip.Y, next.End - clip.X, clip.Height);
            }
            foreach (var previous in Layout.Columns.Where(x => x.Index < column.Index && x.Frozen == column.Frozen).Reverse())
            {
                if (_mergeMap.ContainsKey((cell.Row, previous.Index)) || _cells.TryGetValue((cell.Row, previous.Index), out var adjacent) && adjacent.Display.Length != 0) break;
                clip = new Rect(previous.Start, clip.Y, clip.Right - previous.Start, clip.Height);
            }
        }
        var paneLeft = column.Frozen ? SheetLayout.RowHeader : Layout.FrozenRight;
        var paneRight = column.Frozen ? Layout.FrozenRight : state.Size.Width;
        clip = clip.Intersect(new Rect(paneLeft, rect.Y, Math.Max(0, paneRight - paneLeft), rect.Height));
        // Conditional decorations reserve their own space even when the value
        // is right-aligned or overflows an adjacent empty cell.
        if (extended.Icon is not null || extended.Rating is not null)
            clip = clip.Intersect(new Rect(textArea.X, rect.Y, Math.Max(0, rect.Right - textArea.X), rect.Height));
        if (clip.Width <= 1) return;
        using var text = resources.CellText(cell.Display, style, _palette, alignment.WrapText, textArea.Width);
        var layout = text.Layout;
        var x = horizontal switch { "right" => textArea.Right - layout.Width, "center" or "centercontinuous" => textArea.X + (textArea.Width - layout.Width) / 2, _ => textArea.X };
        var y = alignment.Vertical.ToLowerInvariant() switch { "top" => textArea.Y, "center" => textArea.Y + (textArea.Height - layout.Height) / 2, _ => textArea.Bottom - layout.Height };
        using var clipping = context.PushClip(clip.Deflate(.5));
        layout.Draw(context, new Point(x, y));
    }

    private static void DrawBorders(DrawingContext context, Rect rect, CellBorder border)
    {
        ConditionalRenderer.Border(context, border.Top, rect.TopLeft, rect.TopRight);
        ConditionalRenderer.Border(context, border.Bottom, rect.BottomLeft, rect.BottomRight);
        ConditionalRenderer.Border(context, border.Left, rect.TopLeft, rect.BottomLeft);
        ConditionalRenderer.Border(context, border.Right, rect.TopRight, rect.BottomRight);
        if (border.DiagonalDown) ConditionalRenderer.Border(context, border.Diagonal, rect.TopLeft, rect.BottomRight);
        if (border.DiagonalUp) ConditionalRenderer.Border(context, border.Diagonal, rect.BottomLeft, rect.TopRight);
    }

    private void DrawHeaders(DrawingContext context, CellArea area)
    {
        context.FillRectangle(_palette.Header, new Rect(0, 0, state.Size.Width, SheetLayout.ColumnHeader));
        context.FillRectangle(_palette.Header, new Rect(0, 0, SheetLayout.RowHeader, state.Size.Height));
        var border = new Pen(_palette.HeaderBorder, 1);
        foreach (var column in Layout.Columns)
        {
            var rect = new Rect(column.Start, 0, column.Size, SheetLayout.ColumnHeader);
            var selected = column.Index >= area.Column && column.Index < area.Column + area.Width;
            var full = area.Row == 1 && area.Height == CellAddress.LastRow;
            if (selected) context.FillRectangle(full ? _palette.Accent : _palette.HeaderSelected, rect);
            context.DrawRectangle(null, border, rect);
            if (selected) context.DrawLine(new Pen(_palette.Accent, 1), rect.BottomLeft, rect.BottomRight);
            Label(context, CellAddress.ColumnName(column.Index), rect,
                selected ? full ? _palette.AccentText : _palette.HeaderSelectedText : _palette.HeaderText);
        }
        foreach (var row in Layout.Rows)
        {
            var rect = new Rect(0, row.Start, SheetLayout.RowHeader, row.Size);
            var selected = row.Index >= area.Row && row.Index < area.Row + area.Height;
            var full = area.Column == 1 && area.Width == CellAddress.LastColumn;
            if (selected) context.FillRectangle(full ? _palette.Accent : _palette.HeaderSelected, rect);
            context.DrawRectangle(null, border, rect);
            if (selected) context.DrawLine(new Pen(_palette.Accent, 1), rect.TopRight, rect.BottomRight);
            Label(context, row.Index.ToString(CultureInfo.InvariantCulture), rect,
                selected ? full ? _palette.AccentText : _palette.HeaderSelectedText : _palette.HeaderText);
        }
        context.DrawRectangle(_palette.HeaderCorner, border, new Rect(0, 0, SheetLayout.RowHeader, SheetLayout.ColumnHeader));
    }

    private void Label(DrawingContext context, string text, Rect rect, IBrush foreground)
    {
        using var textLayout = resources.HeaderText(text, foreground);
        var formatted = textLayout.Layout;
        using var clip = context.PushClip(rect);
        formatted.Draw(context, new Point(rect.X + (rect.Width - formatted.Width) / 2, rect.Y + (rect.Height - formatted.Height) / 2));
    }

    private void DrawReference(DrawingContext context, FormulaReference reference)
    {
        if (reference.Area.Sheet != state.Snapshot?.View.Sheet) return;
        var brush = ConditionalRenderer.Brush(reference.Color);
        var pen = new Pen(brush, 1, new DashStyle([2, 2], 0));
        foreach (var rect in Layout.AreaRects(reference.Area))
        {
            // Upstream drawActiveRanges strokes the cell boundary, then adds a 10% fill.
            context.DrawRectangle(null, pen, rect);
            using (context.PushOpacity(0.1))
                context.FillRectangle(brush, rect);
        }
    }

    private void DrawRange(DrawingContext context, CellArea? area, Pen pen)
    {
        if (area is null || area.Sheet != state.Snapshot?.View.Sheet) return;
        foreach (var rect in Layout.AreaRects(area)) context.DrawRectangle(null, pen, rect.Deflate(1));
    }

    private void DrawArrayOutline(DrawingContext context, CellSnapshot cell)
    {
        var structure = cell.ArrayStructure;
        if (structure.ValueKind != System.Text.Json.JsonValueKind.Object || state.Snapshot is not { } snapshot) return;
        var item = structure.EnumerateObject().First();
        if (item.Name is "DynamicAnchor" or "ArrayAnchor")
            DrawRange(context, new(snapshot.View.Sheet, cell.Row, cell.Column, item.Value[0].GetInt32(), item.Value[1].GetInt32()), new Pen(ConditionalRenderer.Brush("#658FC3"), 1.5));
        else if (item.Name is "DynamicChild" or "ArrayChild")
            DrawRange(context, new(snapshot.View.Sheet, item.Value[0].GetInt32(), item.Value[1].GetInt32(), item.Value[2].GetInt32(), item.Value[3].GetInt32()), new Pen(ConditionalRenderer.Brush("#658FC3"), 1.5));
    }

}
