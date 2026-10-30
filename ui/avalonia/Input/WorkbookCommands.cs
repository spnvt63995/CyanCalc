namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    // Execute application intent through the same operations used by toolbar/menu
    // callbacks. Native text history and completion remain in the input adapter.
    private void ExecuteWorkbookAction(WorkbookAction action)
    {
        switch (action)
        {
            case WorkbookAction.Navigate(var direction):
                Command("navigate", new { key = direction.ToString() }); break;
            case WorkbookAction.NavigateToEdge(var direction):
                Run(() => Session?.OnNavigateToEdgeInDirection(
                    direction is WorkbookNavigation.Home or WorkbookNavigation.End
                        ? direction.ToString() : "Arrow" + direction)); break;
            case WorkbookAction.ExpandSelection(var direction):
                Command("expand_selection", new { key = "Arrow" + direction }); break;
            case WorkbookAction.RowEdge(var last):
                MoveToRowEdge(last); break;
            case WorkbookAction.SelectColumn:
                if (Snapshot is { } column)
                    Run(() => Session?.SetSelectedRange(1, column.View.Column, CellAddress.LastRow, column.View.Column));
                break;
            case WorkbookAction.SelectRowAndReplace:
                if (Snapshot is { } row)
                    Run(() => Session?.SetSelectedRange(row.View.Row, 1, row.View.Row, CellAddress.LastColumn));
                // Preserve the upstream Shift+Space fallthrough to text input.
                if (CanEdit) { BeginEdit(true); CellInput.Text = " "; }
                break;
            case WorkbookAction.SwitchSheet(var offset):
                if (Snapshot is { } current)
                {
                    var count = current.Worksheets.Length;
                    SelectSheet((current.View.Sheet + (offset > 0 ? 1 : count - 1)) % count);
                }
                break;
            case WorkbookAction.BeginEdit(var replace):
                BeginEdit(replace); break;
            case WorkbookAction.EnterTextEditMode:
                State.EditSession?.SetAcceptMode(false); break;
            case WorkbookAction.CommitEdit(var direction, var array, var restoreOwnerSheet, var focusCell):
                var ownerSheet = State.Editing?.Sheet;
                if (CommitEdit(array))
                {
                    // Accept-mode arrows restore the draft's sheet; Enter/Tab navigate
                    // the viewed sheet. Shift+accept arrows commit without navigation.
                    if (restoreOwnerSheet && ownerSheet is { } sheet) Command("select_sheet", new { sheet });
                    if (direction is { } next) Command("navigate", new { key = next.ToString() });
                    if (focusCell) FocusCell();
                }
                break;
            case WorkbookAction.CancelEdit:
                CancelEdit(); break;
            case WorkbookAction.CancelSelection:
                State.CutRange = null; State.IsFormatPainter = false; Worksheet.InvalidateVisual(); break;
            case WorkbookAction.CycleReference:
                CycleEditorReference(); break;
            case WorkbookAction.ClearContents:
                Clear("contents"); break;
            case WorkbookAction.History(var redo, var ignoreEditGuard):
                if (ignoreEditGuard) Run(() => Session?.Redo());
                else if (redo) Redo(); else Undo();
                break;
            case WorkbookAction.ToggleFont(var name):
                ToggleFont(name); break;
            case WorkbookAction.Clipboard(var operation):
                if (operation == WorkbookClipboard.Paste) _ = PasteAsync();
                else _ = CopyAsync(operation == WorkbookClipboard.Cut);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown workbook action");
        }
    }

    private void CycleEditorReference()
    {
        var editor = ActiveEditor;
        Run(() =>
        {
            var text = editor.Text ?? "";
            var offsets = ReferenceCycleRequestOffsets(text, editor.SelectionStart, editor.SelectionEnd);
            var result = Session!.Query("cycle_reference", new { formula = text, start = offsets.Start, end = offsets.End });
            ApplyReferenceCycle(editor, result);
            State.EditSession?.FinishReferenceCycle();
        });
    }

    private void MoveToRowEdge(bool last)
    {
        if (Snapshot is not { } snapshot || Session is null) return;
        Run(() =>
        {
            Session.SetSelectedCell(snapshot.View.Row, last ? CellAddress.LastColumn : 1);
            ScrollTo(snapshot.View.TopRow, last ? CellAddress.LastColumn - 5 : 1);
            FocusCell();
        });
    }
}
