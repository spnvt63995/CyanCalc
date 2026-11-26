using Avalonia;
using Avalonia.Media;

namespace CyanCalc.Tabular;

public enum TableCellKind { Text, Number, Null, Loading, Error }
public sealed record TableCell(string Text, TableCellKind Kind = TableCellKind.Text, bool Modified = false)
{
    /// <summary>An optional compact rendering. Copy continues to use Text.</summary>
    public string? DisplayText { get; init; }
}
public sealed record TableColumn(string Id, string Name, string? Type = null, double Width = 160);

/// <summary>An already available display snapshot. Cell must only read cached
/// display data; fetching, raw values, database keys and query state belong to its adapter.</summary>
public sealed record TableSnapshot(string Identity, IReadOnlyList<TableColumn> Columns, long RowCount,
    Func<long, int, TableCell> Cell, bool CountIsExact = true);

public readonly record struct TablePosition(long Row, int Column);
public readonly record struct TableSelection(TablePosition Anchor, TablePosition Active)
{
    public long FirstRow => Math.Min(Anchor.Row, Active.Row);
    public long LastRow => Math.Max(Anchor.Row, Active.Row);
    public int FirstColumn => Math.Min(Anchor.Column, Active.Column);
    public int LastColumn => Math.Max(Anchor.Column, Active.Column);
}

public sealed record TablePalette(IBrush Background, IBrush Text, IBrush Header, IBrush HeaderText,
    IBrush Grid, IBrush Accent, IBrush Selection, IBrush Muted, IBrush Error)
{
    static IBrush B(string color) => new SolidColorBrush(Color.Parse(color));
    public static readonly TablePalette Light = new(B("#FFFFFF"), B("#202838"), B("#F4F6F9"), B("#3D4A5C"),
        B("#DFE5EE"), B("#0891B2"), B("#220891B2"), B("#67778A"), B("#BB2525"));
    public static readonly TablePalette Dark = new(B("#1E232B"), B("#E5EAF1"), B("#292F39"), B("#D6DFEA"),
        B("#424C59"), B("#38C7E9"), B("#3338C7E9"), B("#A6B4C6"), B("#FF9292"));
}

public sealed record TableRenderState(Size Size, TableSnapshot Snapshot, GridAxisCell[] Rows,
    GridAxisCell[] Columns, TableSelection? Selection, TablePalette Palette, double RowHeader, double ColumnHeader)
{
    public IReadOnlySet<TablePosition>? HighlightedCells { get; init; }
}

public static class TableRenderer
{
    static readonly GridFont Font = new("Arial", 12);
    static readonly GridFont HeaderFont = new("Arial", 12, Bold: true);

    public static void Draw(DrawingContext context, TableRenderState state, GridTextResources resources)
    {
        var p = state.Palette; var snapshot = state.Snapshot; var grid = new Pen(p.Grid, 1);
        var highlight = new Pen(p.Accent, 2);
        context.FillRectangle(p.Background, new Rect(state.Size));
        var frozenRight = state.Columns.LastOrDefault(c => c.Frozen)?.End + 3 ?? state.RowHeader;
        foreach (var row in state.Rows)
        foreach (var column in state.Columns)
        {
            var rect = new Rect(column.Start, row.Start, column.Size, row.Size);
            var pane = new Rect(column.Frozen ? state.RowHeader : frozenRight, state.ColumnHeader,
                Math.Max(0, (column.Frozen ? frozenRight - 3 : state.Size.Width) - (column.Frozen ? state.RowHeader : frozenRight)),
                Math.Max(0, state.Size.Height - state.ColumnHeader));
            var clip = rect.Intersect(pane);
            if (clip.Width <= 0 || clip.Height <= 0) continue;
            using var paneClip = context.PushClip(pane);
            context.DrawRectangle(null, grid, rect);
            var cell = snapshot.Cell(row.Index - 1, checked((int)column.Index - 1));
            if (state.HighlightedCells?.Contains(new(row.Index - 1, checked((int)column.Index - 1))) == true)
                context.DrawRectangle(p.Selection, highlight, rect.Deflate(2), 3, 3);
            if (cell.Modified) context.FillRectangle(p.Accent, new Rect(rect.X + 1, rect.Y + 1, 3, Math.Max(0, rect.Height - 2)));
            var foreground = cell.Kind switch { TableCellKind.Null or TableCellKind.Loading => p.Muted, TableCellKind.Error => p.Error, _ => p.Text };
            var display = cell.DisplayText ?? (cell.Kind == TableCellKind.Null ? "NULL" : cell.Text);
            using var text = resources.Text(display, Font, foreground, false, rect.Width);
            GridCellPainter.Text(context, text.Layout, rect.Deflate(new Thickness(4, 1)), clip,
                cell.Kind == TableCellKind.Number ? "right" : "left", "center");
        }
        if (state.Selection is { } selection)
        {
            foreach (var r in GridAxis.Rectangles(state.Rows, state.Columns,
                         selection.FirstRow + 1, selection.LastRow + 1, selection.FirstColumn + 1, selection.LastColumn + 1))
            {
                var clipped = r.Intersect(new Rect(state.RowHeader, state.ColumnHeader,
                    Math.Max(0, state.Size.Width - state.RowHeader), Math.Max(0, state.Size.Height - state.ColumnHeader)));
                context.FillRectangle(p.Selection, clipped);
                context.DrawRectangle(null, new Pen(p.Accent, 1), clipped.Deflate(.5));
            }
        }
        context.FillRectangle(p.Header, new Rect(0, 0, state.Size.Width, state.ColumnHeader));
        context.FillRectangle(p.Header, new Rect(0, 0, state.RowHeader, state.Size.Height));
        // Draw scrolling headers first, then frozen headers on top.
        foreach (var column in state.Columns.OrderBy(c => c.Frozen))
        {
            var rect = new Rect(column.Start, 0, column.Size, state.ColumnHeader);
            var paneLeft = column.Frozen ? state.RowHeader : frozenRight;
            using var clip = context.PushClip(new Rect(paneLeft, 0, Math.Max(0, state.Size.Width - paneLeft), state.ColumnHeader));
            context.DrawRectangle(p.Header, grid, rect);
            using var text = resources.Text(snapshot.Columns[checked((int)column.Index - 1)].Name, HeaderFont, p.HeaderText, false, rect.Width);
            GridCellPainter.Header(context, text.Layout, rect);
        }
        foreach (var row in state.Rows)
        {
            var rect = new Rect(0, row.Start, state.RowHeader, row.Size);
            using var clip = context.PushClip(new Rect(0, state.ColumnHeader, state.RowHeader, Math.Max(0, state.Size.Height - state.ColumnHeader)));
            context.DrawRectangle(p.Header, grid, rect);
            using var text = resources.Text(row.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), Font, p.HeaderText, false, rect.Width);
            GridCellPainter.Header(context, text.Layout, rect);
        }
        context.DrawRectangle(p.Header, grid, new Rect(0, 0, state.RowHeader, state.ColumnHeader));
        if (frozenRight > state.RowHeader)
            context.DrawLine(new Pen(p.Grid, 3), new(frozenRight - 1.5, 0), new(frozenRight - 1.5, state.Size.Height));
    }
}
