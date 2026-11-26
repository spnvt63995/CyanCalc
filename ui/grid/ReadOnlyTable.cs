using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;

namespace CyanCalc.Tabular;

/// <summary>A display-only table. It has no paste, fill, formula, save, or database commands.</summary>
public sealed class ReadOnlyTable : Avalonia.Controls.Grid
{
    const double RowHeight = 27, HeaderHeight = 30, HeaderWidth = 58;
    readonly Surface surface;
    readonly ScrollBar vertical = new() { Orientation = Orientation.Vertical, Width = 15, SmallChange = RowHeight };
    readonly ScrollBar horizontal = new() { Orientation = Orientation.Horizontal, Height = 15, SmallChange = 40 };
    readonly GridTextResources resources = new();
    TableSnapshot? snapshot;
    double[] widths = [];
    GridAxisCell[] rows = [], columns = [];
    TableSelection? selection;
    readonly HashSet<TablePosition> highlights = [];
    IPointer? pointer;
    int resizeColumn = -1, frozenColumns;
    double resizeStart, resizeWidth;
    bool syncing;
    public event EventHandler<TableSelection?>? SelectionChanged;
    public event EventHandler<string>? Error;
    public TableSelection? Selection => selection;
    /// <summary>Display offsets only; database row identities belong to the consumer.</summary>
    public Vector ScrollOffset
    {
        get => new(horizontal.Value, vertical.Value);
        set
        {
            LayoutTable();
            horizontal.Value = double.IsFinite(value.X) ? Math.Clamp(value.X, 0, horizontal.Maximum) : 0;
            vertical.Value = double.IsFinite(value.Y) ? Math.Clamp(value.Y, 0, vertical.Maximum) : 0;
        }
    }
    public int FrozenColumns
    {
        get => frozenColumns;
        set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); frozenColumns = value; LayoutTable(); }
    }

    public TableSnapshot? Snapshot
    {
        get => snapshot;
        set
        {
            if (value is not null && (value.RowCount < 0 || value.Columns.Select(c => c.Id).Distinct().Count() != value.Columns.Count ||
                value.Columns.Any(c => string.IsNullOrEmpty(c.Id) || !double.IsFinite(c.Width) || c.Width < 24)))
                throw new ArgumentException("Invalid table dimensions or duplicate column IDs.");
            CancelGesture();
            var same = snapshot?.Identity == value?.Identity && snapshot?.Columns.SequenceEqual(value?.Columns ?? []) == true;
            snapshot = value;
            if (!same)
            {
                highlights.Clear();
                widths = value?.Columns.Select(c => c.Width).ToArray() ?? [];
                vertical.Value = horizontal.Value = 0;
                SetSelection(null); resources.Clear();
            }
            if (selection is { } s && (value is null || s.LastRow >= value.RowCount || s.LastColumn >= value.Columns.Count)) SetSelection(null);
            LayoutTable();
        }
    }

    public ReadOnlyTable()
    {
        RowDefinitions = new("*,15"); ColumnDefinitions = new("*,15");
        surface = new(this) { Focusable = true, ClipToBounds = true, Name = "DataTableSurface" };
        Children.Add(surface); SetColumn(vertical, 1); Children.Add(vertical); SetRow(horizontal, 1); Children.Add(horizontal);
        surface.SizeChanged += (_, _) => LayoutTable();
        foreach (var bar in new[] { vertical, horizontal })
            bar.PropertyChanged += (_, e) => { if (e.Property == ScrollBar.ValueProperty && !syncing) LayoutTable(); };
        ActualThemeVariantChanged += (_, _) => { resources.Clear(); surface.InvalidateVisual(); };
        DetachedFromVisualTree += (_, _) => { CancelGesture(); resources.Clear(); };
    }

    void LayoutTable()
    {
        if (syncing) return;
        syncing = true;
        try
        {
            if (snapshot is not { } data) { rows = []; columns = []; surface.InvalidateVisual(); return; }
            var size = surface.Bounds.Size; var frozen = Math.Min(frozenColumns, widths.Length);
            vertical.ViewportSize = Math.Max(0, size.Height - HeaderHeight);
            vertical.Maximum = Math.Max(0, data.RowCount * RowHeight - vertical.ViewportSize);
            horizontal.ViewportSize = Math.Max(0, size.Width - HeaderWidth - widths.Take(frozen).Sum() - (frozen > 0 ? 3 : 0));
            horizontal.Maximum = Math.Max(0, widths.Skip(frozen).Sum() - horizontal.ViewportSize);
            var firstRow = Math.Min(data.RowCount, ToLong(vertical.Value / RowHeight) + 1);
            rows = GridAxis.Build(0, Math.Max(1, firstRow), data.RowCount, HeaderHeight, size.Height,
                indices => indices.Select(_ => RowHeight).ToArray(), vertical.Value % RowHeight);
            var firstColumn = frozen; var remainder = horizontal.Value;
            while (firstColumn < widths.Length - 1 && remainder >= widths[firstColumn]) remainder -= widths[firstColumn++];
            columns = GridAxis.Build(frozen, firstColumn + 1, widths.Length, HeaderWidth, size.Width,
                indices => indices.Select(i => widths[checked((int)i - 1)]).ToArray(), remainder);
            resources.SetContext((data.Identity, ActualThemeVariant, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
            surface.InvalidateVisual();
        }
        finally { syncing = false; }
    }

    static long ToLong(double value) => value >= long.MaxValue - 1 ? long.MaxValue - 1 : (long)Math.Max(0, value);
    TablePosition? Hit(Point point)
    {
        if (point.X < HeaderWidth || point.Y < HeaderHeight) return null;
        var row = GridAxis.HitTest(rows, point.Y);
        // Frozen cells win where the first scrolling cell is clipped underneath.
        var column = columns.OrderByDescending(c => c.Frozen).FirstOrDefault(c => point.X >= c.Start && point.X < c.End)?.Index ?? 0;
        return row > 0 && column > 0 ? new(row - 1, checked((int)column - 1)) : null;
    }
    void SetSelection(TableSelection? value) { selection = value; SelectionChanged?.Invoke(this, value); surface.InvalidateVisual(); }
    public void ClearSelection()
    {
        if (selection.HasValue) SetSelection(null);
    }

    /// <summary>Emphasize independent cell positions without changing the user's copy range.</summary>
    public void HighlightCells(IEnumerable<TablePosition> positions)
    {
        highlights.Clear();
        highlights.UnionWith(positions);
        surface.InvalidateVisual();
    }
    public void Select(TablePosition position, bool extend = false)
    {
        if (snapshot is not { } data || position.Row < 0 || position.Row >= data.RowCount || position.Column < 0 || position.Column >= data.Columns.Count)
            throw new ArgumentOutOfRangeException(nameof(position));
        SetSelection(new(extend && selection is { } s ? s.Anchor : position, position));
    }

    void Press(PointerPressedEventArgs e)
    {
        if (snapshot is null || !e.GetCurrentPoint(surface).Properties.IsLeftButtonPressed) return;
        surface.Focus(); var p = e.GetPosition(surface);
        if (p.Y < HeaderHeight)
        {
            var column = columns.FirstOrDefault(c => Math.Abs(c.End - p.X) < 4);
            if (column is null) return;
            resizeColumn = checked((int)column.Index - 1); resizeStart = p.X; resizeWidth = widths[resizeColumn];
        }
        else if (Hit(p) is { } hit) Select(hit, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        else return;
        pointer = e.Pointer; pointer.Capture(surface); e.Handled = true;
    }
    void Move(PointerEventArgs e)
    {
        if (!ReferenceEquals(pointer, e.Pointer)) return;
        if (resizeColumn >= 0) { widths[resizeColumn] = Math.Clamp(resizeWidth + e.GetPosition(surface).X - resizeStart, 24, 4096); LayoutTable(); }
        else if (Hit(e.GetPosition(surface)) is { } hit) Select(hit, true);
        e.Handled = true;
    }
    void CancelGesture()
    {
        var captured = pointer; pointer = null; resizeColumn = -1;
        if (captured?.Captured == surface) captured.Capture(null);
    }
    void Wheel(PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) horizontal.Value -= e.Delta.Y * 40;
        else { vertical.Value -= e.Delta.Y * RowHeight * 3; horizontal.Value -= e.Delta.X * 40; }
        e.Handled = true;
    }
    async void Key(KeyEventArgs e)
    {
        if (snapshot is not { RowCount: > 0 } data || data.Columns.Count == 0) return;
        if (e.Key == Avalonia.Input.Key.C && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            e.Handled = true;
            try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(SelectionText()); }
            catch (Exception error) { Error?.Invoke(this, error.Message); }
            return;
        }
        var current = selection?.Active ?? new(0, 0); var next = current;
        switch (e.Key)
        {
            case Avalonia.Input.Key.Down: next = current with { Row = Math.Min(data.RowCount - 1, current.Row + 1) }; break;
            case Avalonia.Input.Key.Up: next = current with { Row = Math.Max(0, current.Row - 1) }; break;
            case Avalonia.Input.Key.Right: next = current with { Column = Math.Min(data.Columns.Count - 1, current.Column + 1) }; break;
            case Avalonia.Input.Key.Left: next = current with { Column = Math.Max(0, current.Column - 1) }; break;
            case Avalonia.Input.Key.Home: next = current with { Column = 0 }; break;
            case Avalonia.Input.Key.End: next = current with { Column = data.Columns.Count - 1 }; break;
            case Avalonia.Input.Key.PageDown: next = current with { Row = current.Row + Math.Min(data.RowCount - 1 - current.Row, Math.Max(1, rows.Length - 1)) }; break;
            case Avalonia.Input.Key.PageUp: next = current with { Row = Math.Max(0, current.Row - Math.Max(1, rows.Length - 1)) }; break;
            default: return;
        }
        Select(next, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        ScrollIntoView(next);
        e.Handled = true;
    }

    public void ScrollIntoView(TablePosition position)
    {
        if (snapshot is not { } data || position.Row < 0 || position.Row >= data.RowCount ||
            position.Column < 0 || position.Column >= data.Columns.Count || surface.Bounds.Size == default) return;
        if (!rows.Any(r => r.Index == position.Row + 1 && r.Start >= HeaderHeight && r.End <= surface.Bounds.Height)) vertical.Value = position.Row * RowHeight;
        var frozenRight = columns.LastOrDefault(c => c.Frozen)?.End + 3 ?? HeaderWidth;
        if (position.Column >= frozenColumns && !columns.Any(c => c.Index == position.Column + 1 && c.Start >= frozenRight && c.End <= surface.Bounds.Width))
            horizontal.Value = widths.Skip(Math.Min(frozenColumns, widths.Length)).Take(Math.Max(0, position.Column - frozenColumns)).Sum();
    }

    public string SelectionText()
    {
        if (snapshot is not { } data || selection is not { } s) return "";
        if (s.LastRow - s.FirstRow + 1 > 100_000 / (s.LastColumn - s.FirstColumn + 1))
            throw new InvalidOperationException("Copy at most 100,000 cells; use an export operation for a larger result.");
        var text = new StringBuilder();
        for (var r = s.FirstRow; r <= s.LastRow; r++)
        {
            if (r != s.FirstRow) text.AppendLine();
            for (var c = s.FirstColumn; c <= s.LastColumn; c++)
            {
                if (c != s.FirstColumn) text.Append('\t');
                var cell = data.Cell(r, c);
                if (cell.Kind is TableCellKind.Loading or TableCellKind.Error) throw new InvalidOperationException("Only available cells can be copied.");
                var value = cell.Kind == TableCellKind.Null ? "NULL" : cell.Text;
                if (value.Length > 4_000_000 || text.Length + (long)value.Length * 2 > 8_000_000)
                    throw new InvalidOperationException("Copy is limited to 8 million characters; use an export operation.");
                text.Append(value.IndexOfAny(['\t', '\r', '\n', '"']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value);
            }
        }
        return text.ToString();
    }

    sealed class Surface(ReadOnlyTable table) : Control
    {
        public override void Render(Avalonia.Media.DrawingContext context)
        {
            if (table.snapshot is not { } data) return;
            table.resources.SetContext((data.Identity, table.ActualThemeVariant, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
            TableRenderer.Draw(context, new(Bounds.Size, data, table.rows, table.columns, table.selection,
                table.ActualThemeVariant == ThemeVariant.Dark ? TablePalette.Dark : TablePalette.Light, HeaderWidth, HeaderHeight)
                { HighlightedCells = table.highlights }, table.resources);
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e) => table.Press(e);
        protected override void OnPointerMoved(PointerEventArgs e) => table.Move(e);
        protected override void OnPointerReleased(PointerReleasedEventArgs e) { if (ReferenceEquals(table.pointer, e.Pointer)) table.CancelGesture(); }
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => table.CancelGesture();
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) => table.Wheel(e);
        protected override void OnKeyDown(KeyEventArgs e) => table.Key(e);
    }
}
