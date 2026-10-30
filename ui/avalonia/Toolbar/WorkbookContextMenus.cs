using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    // Menu groups and targets follow Worksheet/ContextMenus and Worksheet.tsx at v0.9.0.
    internal ContextMenu BuildCellContextMenu(bool columnHeader, bool rowHeader)
    {
        if (columnHeader || rowHeader) return WorkbookMenus.Create(AxisMenu(rowHeader));
        if (Snapshot is not { } snapshot) return WorkbookMenus.Create();
        var view = snapshot.View;
        var items = new Control[]
        {
            IconMenuItem(Text.Key("context_menu.cell.cut"), "Scissors", () => _ = CopyAsync(true), CanEdit),
            IconMenuItem(Text.Key("context_menu.cell.copy"), "Copy", () => _ = CopyAsync()),
            IconMenuItem(Text.Key("context_menu.cell.paste"), "Clipboard", () => _ = PasteAsync(), CanEdit),
            new Separator(),
            Text.Key("context_menu.cell.insert").Bind(new MenuItem { Icon = new WorkbookIcon("Plus"), IsEnabled = CanEdit, ItemsSource = new Control[]
            {
                MenuItem(Text.Key("context_menu.cell.insert_column_left"), () => InsertAxis(false, view.Column, 1)),
                MenuItem(Text.Key("context_menu.cell.insert_column_right"), () => InsertAxis(false, view.Range[3] + 1, 1)), new Separator(),
                MenuItem(Text.Key("context_menu.cell.insert_row_above"), () => InsertAxis(true, view.Row, 1)),
                MenuItem(Text.Key("context_menu.cell.insert_row_below"), () => InsertAxis(true, view.Range[2] + 1, 1))
            }}, global::Avalonia.Controls.MenuItem.HeaderProperty),
            Text.Key("context_menu.cell.delete").Bind(new MenuItem { Icon = new WorkbookIcon("Trash2"), IsEnabled = CanEdit, ItemsSource = new Control[]
            {
                MenuItem(Text.Key("context_menu.cell.delete_column", ("column", CellAddress.ColumnName(view.Column))), () => Command("delete_columns", new { column = view.Column, count = 1 })),
                MenuItem(Text.Key("context_menu.cell.delete_row", ("row", view.Row)), () => Command("delete_rows", new { row = view.Row, count = 1 }))
            }}, global::Avalonia.Controls.MenuItem.HeaderProperty)
        };
        return WorkbookMenus.Create(items);
    }

    private void InsertAxis(bool rows, int first, int count) =>
        Command(rows ? "insert_rows" : "insert_columns", rows ? new { row = first, count } : (object)new { column = first, count });

    private Control[] AxisMenu(bool rows)
    {
        if (Snapshot is not { } snapshot) return [];
        var area = CellArea.FromView(snapshot.View);
        var first = rows ? area.Row : area.Column;
        var count = rows ? area.Height : area.Width;
        var last = first + count - 1;
        var axis = rows ? "rows" : "columns";
        var firstLabel = rows ? first.ToString(CultureInfo.InvariantCulture) : CellAddress.ColumnName(first);
        var lastLabel = rows ? last.ToString(CultureInfo.InvariantCulture) : CellAddress.ColumnName(last);
        var prefix = rows ? "context_menu.row_header." : "context_menu.column_header.";
        LocalizedText Label(string suffix) => Text.Key(prefix + suffix, ("count", count),
            (rows ? "row" : "column", firstLabel), (rows ? "rowStart" : "columnStart", firstLabel), (rows ? "rowEnd" : "columnEnd", lastLabel));
        var items = new List<Control>
        {
            IconMenuItem(Label(rows ? "insert_rows_above" : "insert_columns_before"), "Plus",
                () => InsertAxis(rows, rows ? snapshot.View.Row : snapshot.View.Column, count)),
            IconMenuItem(Label(rows ? "insert_rows_below" : "insert_columns_after"), "Plus",
                () => InsertAxis(rows, rows ? snapshot.View.Row + 1 : last + 1, count)),
            new Separator(),
            IconMenuItem(Label(rows ? "move_rows_up" : "move_columns_left"), rows ? "ArrowUp" : "ArrowLeft", () => AxisCommand(rows, "move", -1)),
            IconMenuItem(Label(rows ? "move_rows_down" : "move_columns_right"), rows ? "ArrowDown" : "ArrowRight", () => AxisCommand(rows, "move", 1)),
            new Separator(),
            IconMenuItem(Label(rows ? "set_row_height" : "set_column_width"), rows ? "MoveVertical" : "MoveHorizontal", () => SetAxisSize(rows)),
            new Separator(),
            IconMenuItem(Label(count == 1 ? rows ? "hide_row" : "hide_column" : "hide_" + axis), "EyeOff", () => AxisCommand(rows, "hide"))
        };
        var hidden = rows || Session!.Dimensions(snapshot.View.Sheet, [], Enumerable.Range(first, count).ToArray()).Columns.Any(width => width == 0);
        if (hidden) items.Add(IconMenuItem(Label("show_hidden_" + axis), "Eye", () => AxisCommand(rows, "unhide")));
        items.Add(new Separator());
        items.Add(IconMenuItem(Label("freeze_" + axis), "Snowflake",
            () => Command("freeze_" + axis, new { count = rows ? snapshot.View.Row : snapshot.View.Column })));
        if ((rows ? snapshot.FrozenRows : snapshot.FrozenColumns) > 0)
            items.Add(IconMenuItem(Label("unfreeze_" + axis), "Snowflake", () => Command("freeze_" + axis, new { count = 0 })));
        items.Add(new Separator());
        var delete = IconMenuItem(Label(count == 1 ? rows ? "delete_row" : "delete_column" : "delete_" + axis), "Trash2", () => AxisCommand(rows, "delete"));
        delete.Classes.Add("destructive"); items.Add(delete);
        foreach (var item in items.OfType<MenuItem>()) item.IsEnabled = CanEdit;
        return items.ToArray();
    }

    private void SetAxisSize(bool rows)
    {
        if (Snapshot is not { } snapshot || Session is not { } model) return;
        var area = CellArea.FromView(snapshot.View);
        var dimensions = model.Dimensions(area.Sheet, rows ? [area.Row] : [], rows ? [] : [area.Column]);
        var initial = (rows ? dimensions.Rows[0] : dimensions.Columns[0]).ToString(CultureInfo.InvariantCulture);
        ShowInputDialog(Text.Key(rows ? "context_menu.row_header.set_row_height" : "context_menu.column_header.set_column_width"), initial, text =>
        {
            // The web Prompt closes on submit, but applies only finite, nonnegative pixels.
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) || !double.IsFinite(size) || size < 0) return;
            Command(rows ? "row_height" : "column_width", rows
                ? new { row = area.Row, end = area.Row + area.Height - 1, height = size }
                : (object)new { column = area.Column, end = area.Column + area.Width - 1, width = size });
        }, pixels: true);
    }

    private void AxisCommand(bool rows, string kind, int delta = 0)
    {
        if (Snapshot is null) return;
        var area = CellArea.FromView(Snapshot.View);
        var count = rows ? area.Height : area.Width;
        var first = rows ? area.Row : area.Column;
        Command((kind == "unhide" ? "hide" : kind) + (rows ? "_rows" : "_columns"),
            new { row = area.Row, column = area.Column, count, end = first + count - 1, hidden = kind == "hide", delta });
    }
}
