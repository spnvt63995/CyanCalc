using System.Text.Json;
using CyanCalc;

internal static class EngineChecks
{
    internal static void Equal<T>(T expected, T actual, string reason)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{reason}: expected {expected}, got {actual}");
    }

    public static void Run()
    {
        FontSizeChecks.Run();
        DeletionHistoryChecks.Run();
        using var model = UserModel.CreateSample();
        Equal(UpstreamVersion.Commit, model.Query("info").GetProperty("upstream").GetString(), "loaded release engine");
        Equal(false, model.Snapshot().CanUndo, "sample history");
        Equal("32,000", model.Cell(0, 4, 4).Display, "initial formula");
        model.SetUserInput(0, 2, 2, "20");
        Equal("44,000", model.Cell(0, 4, 4).Display, "dependent recalculation");
        model.Execute("undo");
        Equal("32,000", model.Cell(0, 4, 4).Display, "undo");
        model.Execute("redo");
        Equal("44,000", model.Cell(0, 4, 4).Display, "redo");

        model.SetSelectedCell(2, 1);
        model.SetSelectedRange(2, 1, 2, 4);
        var clipboard = model.Query("copy");
        model.SetSelectedCell(6, 1);
        model.Execute("paste", new { source_sheet = 0, source_range = clipboard.GetProperty("range"), data = clipboard.GetProperty("data"), cut = false });
        Equal("노트", model.Cell(0, 6, 1).Content, "unicode clipboard");
        Equal("=B6*C6", model.Cell(0, 6, 4).Content, "relative formula paste");
        Equal("30,000", model.Cell(0, 6, 4).Display, "pasted number format");
        model.Execute("undo");
        Equal("", model.Cell(0, 6, 1).Content, "paste is one history step");

        model.Execute("new_defined_name", new { name = "Quantity", scope = (int?)null, formula = "Sheet1!$B$2" });
        model.SetUserInput(0, 8, 1, "=Quantity*2");
        Equal("40", model.Cell(0, 8, 1).Display, "defined name calculation");
        model.Execute("add_cf", new
        {
            sheet = 0, range = "B2:B3", rule = new
            {
                type = "CellIs", @operator = "GreaterThan", formula = "10", formula2 = (string?)null,
                format = new { fill = new { color = "#FFCC00" } }, stop_if_true = false
            }
        });
        Equal("#FFCC00", model.Cell(0, 2, 2).ExtendedStyle.Style.Fill.Color, "conditional formatting");
        model.Execute("undo");
        Equal(false, model.Cell(0, 2, 2).ExtendedStyle.Style.Fill.Color == "#FFCC00", "CF undo");

        var bytes = model.ToBytes();
        using var restored = new UserModel();
        restored.LoadBytes(bytes);
        Equal("44,000", restored.Cell(0, 4, 4).Display, "ic round trip");
        Equal("40", restored.Cell(0, 8, 1).Display, "ic defined names");
        Equal(false, restored.Snapshot().CanUndo, "ic has no edit history");
        using var xlsx = new UserModel();
        xlsx.LoadXlsx(model.ToXlsx(), "sample.xlsx");
        Equal("44,000", xlsx.Cell(0, 4, 4).Display, "xlsx round trip");
        Equal("노트", xlsx.Cell(0, 2, 1).Content, "xlsx Unicode");

        model.Execute("new_sheet");
        Equal(2, model.Snapshot().Worksheets.Length, "new sheet");
        model.Execute("rename_sheet", new { sheet = 1, name = "한글 시트" });
        model.Execute("move_sheet", new { sheet = 1, index = 0 });
        Equal("한글 시트", model.Snapshot().Worksheets[0].Name, "move sheet");
        Equal("44,000", model.Cell(1, 4, 4).Display, "sheet move preserves model");
        try { model.SetUserInput(0, 0, 1, "invalid"); throw new Exception("Invalid row was accepted"); }
        catch (IronCalcException) { }
        Equal("44,000", model.Cell(1, 4, 4).Display, "error does not corrupt workbook");

        var released = new UserModel();
        released.Dispose(); released.Dispose();
        try { released.Snapshot(); throw new Exception("Disposed session was callable"); }
        catch (ObjectDisposedException) { }
        for (int index = 0; index < 100; index++)
        {
            using var transient = new UserModel();
            transient.SetUserInput(0, 1, 1, index.ToString());
            Equal(index.ToString(), transient.Cell(0, 1, 1).Display, "independent session lifetime");
        }
        Equal("XFD", CellAddress.ColumnName(CellAddress.LastColumn), "last column");
        Equal(true, CellAddress.TryParse("$XFD$1048576", out var row, out var column), "address boundary");
        Equal(CellAddress.LastRow, row, "last row");
        Equal(CellAddress.LastColumn, column, "last column parse");
        Equal(false, CellAddress.TryParse("XFE1", out _, out _), "out-of-range address");
    }
}
