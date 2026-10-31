using CyanCalc;

internal static class FontSizeChecks
{
    internal static void Run()
    {
        foreach (var legacy in new[] { false, true })
        {
            using var model = new UserModel();
            model.SetUserInput(0, 1, 1, "A");
            model.SetUserInput(0, 1, 2, "B");
            model.UpdateRangeStyle(new(0, 1, 1, 1, 1), "font.size", "2");
            model.UpdateRangeStyle(new(0, 1, 2, 1, 1), "font.size", "1");
            var range = new CellArea(0, 1, 1, 2, 1);
            model.UpdateRangeStyle(range, "font.size", "20");
            model.Undo();
            var bytes = model.ToBytes();
            var revision = model.Revision;
            var notifications = 0;
            model.Changed += (_, _) => notifications++;
            try
            {
                if (legacy) model.Execute("style", new { area = range, path = "font.size_delta", value = "-1" });
                else model.UpdateRangeStyle(range, "font.size_delta", "-1");
                throw new InvalidOperationException("Invalid range decrement was accepted");
            }
            catch (IronCalcException) { }
            EngineChecks.Equal(true, bytes.SequenceEqual(model.ToBytes()), "failed decrement leaves complete workbook unchanged");
            EngineChecks.Equal(revision, model.Revision, "failed decrement preserves revision");
            EngineChecks.Equal(0, notifications, "failed decrement sends no notification");
            EngineChecks.Equal(true, model.CanRedo(), "failed decrement keeps redo");
            model.Redo();
            EngineChecks.Equal(20, model.Cell(0, 1, 2).ExtendedStyle.Style.Font.Sz, "old redo still works");
            model.Undo();
            EngineChecks.Equal(true, bytes.SequenceEqual(model.ToBytes()), "undo restores the mixed sizes");
            model.UpdateRangeStyle(range, "font.size_delta", "1");
            EngineChecks.Equal(3, model.Cell(0, 1, 1).ExtendedStyle.Style.Font.Sz, "successful delta first cell");
            EngineChecks.Equal(2, model.Cell(0, 1, 2).ExtendedStyle.Style.Font.Sz, "successful delta second cell");
            model.Undo();
            EngineChecks.Equal(2d, model.Cell(0, 1, 1).ExtendedStyle.Style.Font.Sz, "successful delta undo first cell");
            EngineChecks.Equal(1d, model.Cell(0, 1, 2).ExtendedStyle.Style.Font.Sz, "successful delta undo second cell");
        }
    }
}
