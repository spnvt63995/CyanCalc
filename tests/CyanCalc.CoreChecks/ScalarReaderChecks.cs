using CyanCalc;

internal static class ScalarReaderChecks
{
    private static void Equal<T>(T expected, T actual, string reason)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{reason}: expected {expected}, got {actual}");
    }

    public static void Run()
    {
        using var model = new UserModel();
        Equal(null, model.GetCellValue(0, 1, 1), "empty raw value");
        Equal(null, model.GetCellFormula(0, 1, 1), "empty formula");
        model.SetUserInput(0, 1, 1, "12.5");
        model.SetUserInput(0, 1, 2, "문자");
        model.SetUserInput(0, 1, 3, "=TRUE()");
        model.SetUserInput(0, 1, 4, "=1/0");
        model.SetUserInput(0, 2, 1, "=A1*2");

        var revision = model.Revision;
        var changed = 0;
        model.Changed += (_, _) => changed++;
        var canUndo = model.CanUndo();
        var canRedo = model.CanRedo();
        var bytes = model.ToBytes();

        Equal(12.5d, model.GetCellValue(0, 1, 1), "number is a double");
        Equal("문자", model.GetCellValue(0, 1, 2), "text remains a string");
        Equal(true, model.GetCellValue(0, 1, 3), "boolean remains a bool");
        Equal("#DIV/0!", model.GetCellValue(0, 1, 4), "engine error is a string");
        Equal(25d, model.GetCellValue(0, 2, 1), "formula returns calculated number");
        Equal(12.5d, model.GetCellValueByRef("Sheet1!A1"), "qualified reference");
        Equal(25d, model.GetCellValueByRef("Sheet1!A2"), "formula reference");
        Equal("=A1*2", model.GetCellFormula(0, 2, 1), "formula text");
        Equal(null, model.GetCellFormula(0, 1, 1), "literal has no formula");
        try
        {
            model.GetCellValueByRef("missing!A1");
            throw new InvalidOperationException("Invalid reference was accepted");
        }
        catch (IronCalcException error)
        {
            Equal("get_cell_value_by_ref", error.Operation, "reference error origin");
        }

        Equal(revision, model.Revision, "readers do not change revision");
        Equal(0, changed, "readers do not emit change events");
        Equal(canUndo, model.CanUndo(), "readers preserve undo availability");
        Equal(canRedo, model.CanRedo(), "readers preserve redo availability");
        Equal(true, bytes.SequenceEqual(model.ToBytes()), "readers preserve workbook bytes");
        model.Undo();
        Equal(null, model.GetCellValue(0, 2, 1), "original undo step still works");
        model.Redo();
        Equal(25d, model.GetCellValue(0, 2, 1), "original redo step still works");
    }
}
