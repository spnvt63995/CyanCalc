using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace CyanCalc.Controls;

public sealed record FormulaReference(CellArea Area, int Start, int Length, string Color);

internal static class FormulaReferences
{
    private static readonly string[] Colors = ["#59B9BC", "#EC5753", "#3358B7", "#F8CD3C", "#3BB68A", "#523E93", "#A23C52", "#8CB354", "#D03627", "#1B717E"];
    public static FormulaReference[] Parse(UserModel session, WorkbookSnapshot snapshot, int editingSheet, string text)
    {
        if (!text.StartsWith('=')) return [];
        var tokens = session.Query("tokens", new { formula = text[1..] }).EnumerateArray().ToArray();
        var result = new List<FormulaReference>(); var colors = new Dictionary<CellArea, string>();
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].GetProperty("token");
            if (token.ValueKind != JsonValueKind.Object) continue;
            int row, column, endRow, endColumn; JsonElement reference;
            if (token.TryGetProperty("Reference", out reference))
            {
                row = endRow = reference.GetProperty("row").GetInt32(); column = endColumn = reference.GetProperty("column").GetInt32();
            }
            else if (token.TryGetProperty("Range", out reference))
            {
                row = reference.GetProperty("left").GetProperty("row").GetInt32(); column = reference.GetProperty("left").GetProperty("column").GetInt32();
                endRow = reference.GetProperty("right").GetProperty("row").GetInt32(); endColumn = reference.GetProperty("right").GetProperty("column").GetInt32();
            }
            else continue;
            var sheetName = reference.GetProperty("sheet").GetString();
            var sheet = sheetName is null ? editingSheet : Array.FindIndex(snapshot.Worksheets, x => x.Name == sheetName);
            if (sheet < 0 || row < 1 || column < 1) continue;
            int end = tokens[i].GetProperty("end").GetInt32();
            if (i + 1 < tokens.Length && tokens[i + 1].GetProperty("token").ValueKind == JsonValueKind.String && tokens[i + 1].GetProperty("token").GetString() == "Spill")
            {
                var structure = session.Cell(sheet, row, column).ArrayStructure;
                if (structure.ValueKind == JsonValueKind.Object && structure.TryGetProperty("DynamicAnchor", out var spill))
                { endColumn = column + spill[0].GetInt32() - 1; endRow = row + spill[1].GetInt32() - 1; end = tokens[i + 1].GetProperty("end").GetInt32(); }
            }
            var area = new CellArea(sheet, Math.Min(row, endRow), Math.Min(column, endColumn), Math.Abs(endColumn - column) + 1, Math.Abs(endRow - row) + 1);
            if (!colors.TryGetValue(area, out var color)) colors[area] = color = Colors[colors.Count % Colors.Length];
            var start = WorkbookView.Utf16Offset(text, tokens[i].GetProperty("start").GetInt32() + 1);
            var finish = WorkbookView.Utf16Offset(text, end + 1);
            result.Add(new(area, start, finish - start, color));
        }
        return result.ToArray();
    }
}

// The native TextBox still owns caret, text selection, scrolling and IME. This decoration only
// recolors reference glyphs at the presenter's actual shaped-text positions.
internal sealed class FormulaColorAdorner(TextPresenter presenter, CellEditor editor) : Control
{
    public FormulaReference[] References { get; set; } = [];
    public override void Render(DrawingContext context)
    {
        if (editor.IsComposing || References.Length == 0 || string.IsNullOrEmpty(presenter.Text)) return;
        var typeface = new Typeface(presenter.FontFamily, presenter.FontStyle, presenter.FontWeight, presenter.FontStretch);
        var offset = presenter.Bounds.Height < presenter.TextLayout.Height ? presenter.VerticalAlignment switch
        { VerticalAlignment.Center => (presenter.Bounds.Height - presenter.TextLayout.Height) / 2, VerticalAlignment.Bottom => presenter.Bounds.Height - presenter.TextLayout.Height, _ => 0 } : 0;
        foreach (var reference in References)
        {
            using var layout = new TextLayout(presenter.Text, typeface, presenter.FontSize, ConditionalRenderer.Brush(reference.Color),
                textAlignment: presenter.TextAlignment, textWrapping: presenter.TextWrapping,
                maxWidth: Math.Max(1, presenter.Bounds.Width), lineHeight: presenter.LineHeight, letterSpacing: presenter.LetterSpacing);
            foreach (var bounds in presenter.TextLayout.HitTestTextRange(reference.Start, reference.Length))
            {
                using var clip = context.PushClip(bounds.Translate(new Vector(0, offset)));
                layout.Draw(context, new Point(0, offset));
            }
        }
    }
}
