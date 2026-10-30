using System.Text.Json;
using CyanCalc;

internal static class HyperlinkChecks
{
    private static void Equal<T>(T expected, T actual, string reason)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{reason}: expected {expected}, got {actual}");
    }

    private static void EngineError(string operation, Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException($"{operation}: invalid link request was accepted");
        }
        catch (IronCalcException error)
        {
            Equal(operation, error.Operation, "link error origin");
        }
    }

    public static void Run()
    {
        var external = JsonSerializer.SerializeToElement(new
        {
            type = "External", target = "https://example.com/path", tooltip = (string?)"Example"
        });
        var replacement = JsonSerializer.SerializeToElement(new
        {
            type = "External", target = "https://replacement.example/"
        });
        var internalLink = JsonSerializer.SerializeToElement(new
        {
            type = "Internal", location = "Sheet1!A1", tooltip = (string?)null
        });
        using var model = new UserModel();
        Equal<JsonElement?>(null, model.GetCellLink(0, 2, 2), "missing worksheet link");
        Equal(0, model.GetLinks(0).Length, "empty link list");

        model.SetUserInput(0, 1, 1, "=HYPERLINK(\"https://dynamic.example/\", \"Dynamic\")");
        model.SetCellLink(0, 5, 1, external, "Example");
        model.SetCellLink(0, 2, 2, internalLink);
        Equal("Example", model.GetCellContent(0, 5, 1), "label becomes cell content");
        Equal(true, model.GetExtendedCellStyle(0, 5, 1).Style.Font.U, "new link is underlined");
        Equal("Internal", model.GetCellLink(0, 2, 2)!.Value.GetProperty("type").GetString(), "internal link type");
        Equal("Sheet1!A1", model.GetCellLink(0, 2, 2)!.Value.GetProperty("location").GetString(), "internal location");
        Equal<JsonElement?>(null, model.GetCellLink(0, 1, 1), "formula link is not editable metadata");

        var revision = model.Revision;
        var changed = 0;
        model.Changed += (_, _) => changed++;
        var canUndo = model.CanUndo();
        var canRedo = model.CanRedo();
        var bytes = model.ToBytes();
        var links = model.GetLinks(0);
        Equal(3, links.Length, "dynamic and worksheet links are listed");
        Equal((1, 1), (links[0].GetProperty("row").GetInt32(), links[0].GetProperty("column").GetInt32()), "list sorted first");
        Equal(true, links[0].GetProperty("dynamic").GetBoolean(), "formula link is dynamic");
        Equal("https://dynamic.example/", links[0].GetProperty("target").GetString(), "formula target");
        Equal((2, 2), (links[1].GetProperty("row").GetInt32(), links[1].GetProperty("column").GetInt32()), "list sorted second");
        Equal(false, links[1].GetProperty("dynamic").GetBoolean(), "internal link is static");
        Equal((5, 1), (links[2].GetProperty("row").GetInt32(), links[2].GetProperty("column").GetInt32()), "list sorted third");
        Equal("Example", links[2].GetProperty("tooltip").GetString(), "tooltip preserved");
        Equal("https://example.com/path", model.GetCellLink(0, 5, 1)!.Value.GetProperty("target").GetString(), "single link target");
        Equal(revision, model.Revision, "readers do not change revision");
        Equal(0, changed, "readers do not emit change events");
        Equal(canUndo, model.CanUndo(), "readers preserve undo availability");
        Equal(canRedo, model.CanRedo(), "readers preserve redo availability");
        Equal(true, bytes.SequenceEqual(model.ToBytes()), "readers preserve workbook bytes");

        model.SetCellLink(0, 1, 1, replacement);
        Equal(false, model.GetLinks(0)[0].GetProperty("dynamic").GetBoolean(), "worksheet link takes precedence");
        Equal("https://replacement.example/", model.GetLinks(0)[0].GetProperty("target").GetString(), "worksheet target takes precedence");
        model.DeleteCellLink(0, 1, 1);
        Equal(true, model.GetLinks(0)[0].GetProperty("dynamic").GetBoolean(), "formula link reappears after delete");

        EngineError("getCellLink", () => model.GetCellLink(0, 0, 1));
        EngineError("getLinks", () => model.GetLinks(99));
        EngineError("setCellLink", () => model.SetCellLink(0, 1, 1,
            JsonSerializer.SerializeToElement(new { type = "Unknown", target = "x" })));
        EngineError("deleteCellLink", () => model.DeleteCellLink(0, 1, 0));

        using var history = new UserModel();
        history.SetCellLink(0, 2, 2, external, "Example");
        history.Undo();
        Equal<JsonElement?>(null, history.GetCellLink(0, 2, 2), "one undo removes link");
        Equal("", history.GetCellContent(0, 2, 2), "one undo removes label");
        history.Redo();
        Equal("Example", history.GetCellContent(0, 2, 2), "redo restores label");
        history.DeleteCellLink(0, 2, 2);
        Equal<JsonElement?>(null, history.GetCellLink(0, 2, 2), "delete removes link");
        Equal("Example", history.GetCellContent(0, 2, 2), "delete keeps cell content");
        Equal(true, history.GetExtendedCellStyle(0, 2, 2).Style.Font.U, "delete keeps style");
        history.DeleteCellLink(0, 2, 2);
        history.Undo();
        Equal(true, history.GetCellLink(0, 2, 2).HasValue, "empty delete adds no native undo step");
        history.SetCellLink(0, 2, 2, external);
        history.Undo();
        Equal<JsonElement?>(null, history.GetCellLink(0, 2, 2), "identical set adds no native undo step");
    }
}
