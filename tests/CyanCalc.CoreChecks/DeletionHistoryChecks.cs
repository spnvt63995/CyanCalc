using CyanCalc;
using static EngineChecks;

internal static class DeletionHistoryChecks
{
    public static void Run()
    {
        foreach (var rows in new[] { false, true })
        {
            using var setup = new UserModel("Deletion history");
            for (var row = 1; row <= 3; row++)
                for (var column = 1; column <= 3; column++)
                    setup.SetUserInput(0, row, column, (row * 10 + column).ToString());
            setup.SetUserInput(0, 1, 4, "=SUM(B1:B3)");
            setup.NewSheet();
            setup.SetUserInput(1, 1, 1, "=SUM(Sheet1!B1:B3)");
            using var model = UserModel.FromBytes(setup.ToBytes(), "en");
            using var peer = UserModel.FromBytes(setup.ToBytes(), "en");
            if (rows) model.DeleteRows(0, 2, 1);
            else model.DeleteColumns(0, 2, 1);
            peer.ApplyExternalDiffs(model.FlushSendQueue());
            var deletedFormula = model.GetCellContent(1, 1, 1);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                model.Undo();
                peer.ApplyExternalDiffs(model.FlushSendQueue());
                Equal(false, model.CanUndo(), "deletion is one history step");
                foreach (var current in new[] { model, peer })
                {
                    Equal("22", current.GetCellContent(0, 2, 2), "restored numeric cell");
                    Equal("=SUM(B1:B3)", current.GetCellContent(0, 1, 4), "restored formula");
                    Equal("=SUM(Sheet1!B1:B3)", current.GetCellContent(1, 1, 1), "restored cross-sheet formula");
                    Equal("66", current.GetFormattedCellValue(1, 1, 1), "restored cross-sheet result");
                }
                using var loaded = UserModel.FromBytes(model.ToBytes(), "en");
                Equal("66", loaded.GetFormattedCellValue(0, 1, 4), "restored IC round trip");
                using var xlsx = new UserModel();
                xlsx.LoadXlsx(model.ToXlsx(), "restored.xlsx");
                Equal("=SUM(B1:B3)", xlsx.GetCellContent(0, 1, 4), "restored XLSX formula");
                model.Redo();
                peer.ApplyExternalDiffs(model.FlushSendQueue());
                Equal(deletedFormula, model.GetCellContent(1, 1, 1), "redo formula");
                Equal(deletedFormula, peer.GetCellContent(1, 1, 1), "remote redo formula");
            }
        }
    }
}
