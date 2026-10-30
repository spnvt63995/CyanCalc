using CyanCalc;

internal static class ReferenceUtilsChecks
{
    private static void Equal<T>(T expected, T actual, string reason)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{reason}: expected {expected}, got {actual}");
    }

    private static void EngineError(string operation, string message, Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException($"{operation}: invalid input was accepted");
        }
        catch (IronCalcException error)
        {
            Equal(operation, error.Operation, "error operation");
            Equal(message, error.Message, "pinned engine error");
        }
    }

    public static void Run()
    {
        using var model = new UserModel();
        model.SetUserInput(0, 1, 1, "42");
        var revision = model.Revision;
        var changed = 0;
        model.Changed += (_, _) => changed++;
        var canUndo = model.CanUndo();
        var canRedo = model.CanRedo();
        var bytes = model.ToBytes();

        foreach (var (number, name) in new[] { (1, "A"), (26, "Z"), (27, "AA"), (16384, "XFD") })
        {
            Equal(name, ReferenceUtils.ColumnNameFromNumber(number), "column number to name");
            Equal(number, ReferenceUtils.ColumnNumberFromName(name), "column name to number");
        }
        foreach (var number in new[] { -1, 0, 16385 })
            EngineError("columnNameFromNumber", "Invalid column number", () => ReferenceUtils.ColumnNameFromNumber(number));
        foreach (var (name, message) in new[]
        {
            ("", "Invalid column name"),
            ("a", "Invalid column name"),
            ("A1", "Invalid column name"),
            ("É", "Invalid column name"),
            ("XFE", "Invalid column name")
        })
            EngineError("columnNumberFromName", message, () => ReferenceUtils.ColumnNumberFromName(name));

        foreach (var (name, quoted) in new[]
        {
            ("", ""), ("Sheet1", "Sheet1"), ("My Sheet", "'My Sheet'"),
            ("O'Brien", "'O''Brien'"), ("A1", "'A1'"), ("R1C1", "'R1C1'"),
            ("B1048576", "'B1048576'"), ("B1048577", "B1048577"),
            ("Été", "Été")
        })
            Equal(quoted, ReferenceUtils.QuoteName(name), "sheet name quoting");

        Equal(true, CellAddress.TryParse("a1", out _, out _), "CellAddress permits lowercase full addresses");
        try { CellAddress.ColumnName(0); throw new InvalidOperationException("CellAddress accepted zero"); }
        catch (ArgumentOutOfRangeException) { }

        Equal(revision, model.Revision, "utility calls do not change revision");
        Equal(0, changed, "utility calls do not emit change events");
        Equal(canUndo, model.CanUndo(), "utility calls preserve undo availability");
        Equal(canRedo, model.CanRedo(), "utility calls preserve redo availability");
        Equal(true, bytes.SequenceEqual(model.ToBytes()), "utility calls preserve workbook bytes");
        model.Undo();
        Equal(null, model.GetCellValue(0, 1, 1), "original undo step still works");
        model.Redo();
        Equal(42d, model.GetCellValue(0, 1, 1), "original redo step still works");
    }
}
