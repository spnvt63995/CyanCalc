using System.Text;
using System.Text.Json;

namespace CyanCalc.Controls;

internal enum EditorSurface { Cell, FormulaBar }

// One draft owns its input history and reference selection. No controls, focus calls,
// dispatcher work or rendering live here; WorkbookView adapts native input to this session.
internal sealed class WorkbookEditSession : IDisposable
{
    private sealed record ReferenceSelection(string Prefix, string Suffix, int Sheet,
        (int Row, int Column) Anchor, (int Row, int Column) Start, (int Row, int Column) End, int Caret)
    {
        // A merged region can grow either endpoint beyond the original pointer anchor.
        internal (int Row, int Column) Active => (
            Anchor.Row == Start.Row ? End.Row : Start.Row,
            Anchor.Column == Start.Column ? End.Column : Start.Column);
    }
    private ReferenceSelection? _reference;
    private (EditorSurface Surface, EditingCell Owner)? _pendingInputCursor;

    internal WorkbookEditSession(EditingCell cell, string text)
    {
        Cell = cell; Text = text; InputCursor = _inputSelectionEnd = text.Length;
        cell.Attach();
    }

    internal EditingCell Cell { get; private set; }
    internal string Text { get; private set; }
    internal EditorSurface Surface { get; private set; }
    // Upstream onChange stores this cursor; caret-only moves and F4 do not replace it.
    internal int InputCursor { get; private set; }
    private int _inputSelectionEnd;
    internal bool IsSelectingReference { get; private set; }
    internal bool HasReference => _reference is not null;
    internal int ReferenceCaret => _reference?.Caret ?? InputCursor;
    internal FormulaReference[] References { get; private set; } = [];

    internal void Activate(EditorSurface surface) => Surface = surface;
    internal void EnterEditMode(EditorSurface surface)
    {
        Activate(surface);
        SetAcceptMode(false);
    }
    internal void SetAcceptMode(bool accept)
    {
        Cell.Detach();
        Cell = Cell with { AcceptMode = accept };
        Cell.Attach();
    }

    internal void InputText(EditorSurface surface, string text)
    {
        Text = text; Surface = surface; _reference = null;
        if (surface == EditorSurface.Cell && text.Length == 0 && !Cell.AcceptMode)
            SetAcceptMode(true);
        _pendingInputCursor = (surface, Cell);
    }

    internal void CaptureInputCursor(EditorSurface surface, int start, int end)
    {
        if (_pendingInputCursor is not { } pending || pending.Surface != surface) return;
        _pendingInputCursor = null;
        if (ReferenceEquals(Cell, pending.Owner))
        {
            InputCursor = Math.Min(start, end);
            _inputSelectionEnd = Math.Max(start, end);
        }
    }

    internal void FinishReferenceCycle() => _pendingInputCursor = null;

    internal bool TryStartReference(WorkbookSnapshot snapshot, int row, int column) =>
        TryStartReference(snapshot, snapshot.View.Sheet, row, column, _inputSelectionEnd);

    private bool TryStartReference(WorkbookSnapshot snapshot, int sheet, int row, int column,
        int contextCursor)
    {
        if (!Text.StartsWith('=') || row < 1 || column < 1) return false;
        (row, column) = ResolveMergedCell(snapshot, sheet, row, column);
        var acceptsRange = AcceptsReference(Cell.Sheet, Cell.Row, Cell.Column, Text, contextCursor);
        if (!acceptsRange && _reference is null) return false;
        var anchor = (Row: row, Column: column);
        // Reference insertion follows the last text-input cursor, not a later
        // native caret move or text selection. The original preserves the suffix.
        var cursor = Math.Clamp(InputCursor, 0, Text.Length);
        var next = _reference is { } reference
            ? reference with { Sheet = sheet, Anchor = anchor, Start = anchor, End = anchor }
            : new(Text[..cursor], Text[cursor..], sheet, anchor, anchor, anchor, cursor);
        IsSelectingReference = true;
        SetReference(snapshot, next);
        return true;
    }

    internal void UpdateReference(WorkbookSnapshot snapshot, int row, int column)
    {
        if (_reference is not { } reference) return;
        SetReference(snapshot, GrowReference(snapshot, reference with
        {
            Start = reference.Anchor,
            End = (row, column)
        }));
    }

    private void SetReference(WorkbookSnapshot snapshot, ReferenceSelection reference)
    {
        var first = (Row: Math.Min(reference.Start.Row, reference.End.Row),
            Column: Math.Min(reference.Start.Column, reference.End.Column));
        var last = (Row: Math.Max(reference.Start.Row, reference.End.Row),
            Column: Math.Max(reference.Start.Column, reference.End.Column));
        var address = CellAddress.Format(first.Row, first.Column);
        if (first != last)
            address += ":" + CellAddress.Format(last.Row, last.Column);
        if (reference.Sheet != Cell.Sheet)
            address = ReferenceUtils.QuoteName(snapshot.Worksheets[reference.Sheet].Name) + "!" + address;
        Text = reference.Prefix + address + reference.Suffix;
        _reference = reference with { Caret = reference.Prefix.Length + address.Length };
    }

    private ReferenceSelection GrowReference(WorkbookSnapshot snapshot, ReferenceSelection reference)
    {
        var minRow = Math.Min(reference.Start.Row, reference.End.Row);
        var maxRow = Math.Max(reference.Start.Row, reference.End.Row);
        var minColumn = Math.Min(reference.Start.Column, reference.End.Column);
        var maxColumn = Math.Max(reference.Start.Column, reference.End.Column);
        var merges = reference.Sheet == snapshot.View.Sheet
            ? snapshot.MergedCells : Cell.Model.GetMergedCells(reference.Sheet);
        bool changed;
        do
        {
            changed = false;
            foreach (var merge in merges)
            {
                var lastRow = merge.Row + merge.Height - 1;
                var lastColumn = merge.Column + merge.Width - 1;
                if (merge.Row > maxRow || lastRow < minRow || merge.Column > maxColumn || lastColumn < minColumn)
                    continue;
                if (merge.Row < minRow) { minRow = merge.Row; changed = true; }
                if (lastRow > maxRow) { maxRow = lastRow; changed = true; }
                if (merge.Column < minColumn) { minColumn = merge.Column; changed = true; }
                if (lastColumn > maxColumn) { maxColumn = lastColumn; changed = true; }
            }
        } while (changed);

        // Preserve drag orientation while including every intersecting merge, including chains.
        var rowsAscending = reference.Start.Row <= reference.End.Row;
        var columnsAscending = reference.Start.Column <= reference.End.Column;
        return reference with
        {
            Start = (rowsAscending ? minRow : maxRow, columnsAscending ? minColumn : maxColumn),
            End = (rowsAscending ? maxRow : minRow, columnsAscending ? maxColumn : minColumn)
        };
    }

    internal bool TryMoveReference(WorkbookSnapshot snapshot, int rows, int columns, bool extend)
    {
        var source = _reference?.Active ?? (Row: Cell.Row, Column: Cell.Column);
        var row = source.Row + rows;
        var column = source.Column + columns;
        if (!extend || _reference is null)
        {
            var sheet = _reference?.Sheet ?? Cell.Sheet;
            var merges = sheet == snapshot.View.Sheet ? snapshot.MergedCells : Cell.Model.GetMergedCells(sheet);
            if (merges.FirstOrDefault(x => x.Contains(source.Row, source.Column)) is { } leaving)
            {
                // Step beyond the source merge before resolving the destination merge.
                // Otherwise Right/Down land inside the same merge and return to its anchor.
                if (rows != 0) row = rows > 0 ? leaving.Row + leaving.Height : leaving.Row - 1;
                if (columns != 0) column = columns > 0 ? leaving.Column + leaving.Width : leaving.Column - 1;
            }
        }
        if (row < 1 || row > CellAddress.LastRow || column < 1 || column > CellAddress.LastColumn)
        {
            // Reference arrows outside the sheet are handled without changing the draft or range.
            // Ordinary input must still reach the editor's commit/navigation path.
            return _reference is not null ||
                (Text.StartsWith('=') && AcceptsReference(Cell.Sheet, Cell.Row, Cell.Column, Text, InputCursor));
        }
        if (_reference is null)
        {
            // Keyboard references start on the editing sheet; pointer references start on the viewed sheet.
            if (!TryStartReference(snapshot, Cell.Sheet, row, column, InputCursor)) return false;
        }
        else
        {
            var reference = _reference;
            if (extend)
            {
                var start = reference.Start;
                var end = reference.End;
                if (rows != 0)
                {
                    start.Row = Math.Min(reference.Anchor.Row, row);
                    end.Row = Math.Max(reference.Anchor.Row, row);
                }
                if (columns != 0)
                {
                    start.Column = Math.Min(reference.Anchor.Column, column);
                    end.Column = Math.Max(reference.Anchor.Column, column);
                }
                SetReference(snapshot, GrowReference(snapshot, reference with { Start = start, End = end }));
            }
            else
            {
                (row, column) = ResolveMergedCell(snapshot, reference.Sheet, row, column);
                var target = (Row: row, Column: column);
                SetReference(snapshot, reference with { Anchor = target, Start = target, End = target });
            }
        }
        EndReferenceDrag();
        return true;
    }

    internal void EndReferenceDrag() => IsSelectingReference = false;

    internal bool IsSheetReferenceMode()
    {
        var text = _reference is { } reference ? reference.Prefix + reference.Suffix : Text;
        var cursor = _reference?.Prefix.Length ?? InputCursor;
        if (!text.StartsWith('=')) return false;
        var (sheet, row, column) = Cell.Model.GetSelectedCell();
        try { return AcceptsReference(sheet, row, column, text, cursor); }
        catch (IronCalcException) { return false; }
    }

    internal void SelectReferenceSheet(int sheet)
    {
        IsSelectingReference = true;
        try { Cell.Model.SetSelectedSheet(sheet); Surface = EditorSurface.FormulaBar; }
        finally { IsSelectingReference = false; }
    }

    internal void RefreshReferences(WorkbookSnapshot snapshot) =>
        References = FormulaReferences.Parse(Cell.Model, snapshot, Cell.Sheet, Text);

    internal bool IsVisibleOnSheet(int sheet) => Cell.Sheet == sheet;

    private bool AcceptsReference(int sheet, int row, int column, string text, int cursor)
    {
        var context = Cell.Model.Query("completion",
            new { sheet, row, column, formula = text, cursor = EditorTextOffsets.Scalar(text, cursor) });
        return context.GetProperty("expecting").EnumerateArray()
            .Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == "Range");
    }

    private (int Row, int Column) ResolveMergedCell(WorkbookSnapshot snapshot, int sheet, int row, int column) =>
        (sheet == snapshot.View.Sheet ? snapshot.MergedCells : Cell.Model.GetMergedCells(sheet))
            .FirstOrDefault(x => x.Contains(row, column)) is { } merge
            ? (merge.Row, merge.Column) : (row, column);

    public void Dispose() { Cell.Detach(); _pendingInputCursor = null; }
}

internal static class EditorTextOffsets
{
    internal static int Scalar(string text, int utf16)
    {
        var count = 0;
        foreach (var rune in text.AsSpan(0, Math.Clamp(utf16, 0, text.Length)).EnumerateRunes()) count++;
        return count;
    }

    internal static int Utf16(string text, int scalar)
    {
        var offset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (scalar-- <= 0) break;
            offset += rune.Utf16SequenceLength;
        }
        return offset;
    }
}
