using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    private CellEditor ActiveEditor => State.EditSession?.Surface == EditorSurface.FormulaBar ? FormulaInput : CellInput;
    private EditorSurface SurfaceOf(CellEditor editor) => ReferenceEquals(editor, CellInput) ? EditorSurface.Cell : EditorSurface.FormulaBar;
    private readonly FormulaHelper _formulaHelper;

    private void InstallEditorInput()
    {
        CellInput.ScrollSelection = ScrollCellEditorSelection;
        // TextProperty changes are synchronous; TextChanged arrives after the native input cursor.
        CellInput.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) EditorTextChanged(CellInput); };
        FormulaInput.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) EditorTextChanged(FormulaInput); };
        CellInput.CompositionStarted += (_, _) => { if (State.Editing is null) BeginEdit(true, false); };
        FormulaInput.GotFocus += (_, _) => { if (!_syncing && State.Editing is null) BeginEdit(false, false); };
        foreach (var editor in new[] { CellInput, FormulaInput })
        {
            editor.TextChanged += (_, _) => State.EditSession?.CaptureInputCursor(SurfaceOf(editor), editor.SelectionStart, editor.SelectionEnd);
            editor.PropertyChanged += (_, e) => { if (e.Property == TextBox.CaretIndexProperty) QueueCompletion(editor); };
            editor.AddHandler(PointerPressedEvent, (_, _) => State.EditSession?.EnterEditMode(SurfaceOf(editor)), RoutingStrategies.Tunnel);
            editor.AddHandler(KeyDownEvent, EditorKeyDown, RoutingStrategies.Tunnel);
            editor.GotFocus += (_, _) =>
            {
                State.EditSession?.Activate(SurfaceOf(editor));
                QueueCompletion(editor);
            };
            editor.LostFocus += (_, _) => QueueEditorBlur();
        }
    }

    private void QueueEditorBlur()
    {
        if (!_attached) return;
        var attachmentVersion = _attachmentVersion;
        var edit = State.EditSession;
        var owner = edit?.Cell;
        Dispatcher.UIThread.Post(() =>
        {
            // A delayed blur belongs to the same attachment and draft, never a replacement
            // document or a view removed by its host during pointer cancellation.
            if (_attached && attachmentVersion == _attachmentVersion && edit is not null && ReferenceEquals(State.EditSession, edit) && ReferenceEquals(edit.Cell, owner)
                && !CellInput.IsFocused && !FormulaInput.IsFocused && !edit.IsSelectingReference
                && !_previewMenuPreservesEdit && !(_previewDrawerPreservesEdit && _drawer.IsVisible)) CommitEdit();
        });
    }

    private void QueueCompletion(CellEditor editor)
    {
        var edit = State.EditSession;
        if (edit is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(State.EditSession, edit) && editor.IsFocused) UpdateCompletion(editor);
        });
    }

    // Both native inputs display this draft. Their text/caret/IME mechanics stay in TextBox.
    private void RenderEditText()
    {
        if (State.EditSession is not { } edit) return;
        var syncing = _syncing; _syncing = true;
        try
        {
            if (CellInput.Text != edit.Text) CellInput.Text = edit.Text;
            if (FormulaInput.Text != edit.Text) FormulaInput.Text = edit.Text;
        }
        finally { _syncing = syncing; }
    }

    private void RenderIdleText(string text)
    {
        FormulaInput.Text = text;
        CellInput.Text = "";
        CellInput.ResetUndoHistory(); FormulaInput.ResetUndoHistory();
    }

    private EditingCell? EndEditSession()
    {
        // Cancel/document replacement must discard native conversion before clearing
        // the draft, otherwise macOS can deliver it to the next editor session.
        CellInput.FinishComposition(false); FormulaInput.FinishComposition(false);
        CellInput.StopSelectionScroll();
        var cell = State.EndEdit();
        CellInput.ResetUndoHistory(); FormulaInput.ResetUndoHistory();
        CellInput.References = []; FormulaInput.References = [];
        _formulaHelper.Reset();
        return cell;
    }

    public void BeginEdit(bool replace, bool focus = true) => BeginEdit(replace, focus, null);

    private void BeginEdit(bool replace, bool focus, CellEditor? input)
    {
        if (!CanEdit || Snapshot is not { } snapshot || Session is null) return;
        if (State.EditSession is null)
        {
            State.BeginEdit(new(snapshot.View.Sheet, snapshot.Worksheets[snapshot.View.Sheet].SheetId,
                snapshot.View.Row, snapshot.View.Column, snapshot.Cell.Content, replace, Session),
                input?.Text ?? (replace ? CellInput.Text ?? "" : snapshot.Cell.Content));
            if (input is not null)
            {
                // The first native input is already recorded in its source control.
                // Give its mirror the same baseline before copying that input.
                var mirror = ReferenceEquals(input, CellInput) ? FormulaInput : CellInput;
                var syncing = _syncing; _syncing = true;
                try { mirror.Text = replace ? "" : snapshot.Cell.Content; mirror.ResetUndoHistory(); }
                finally { _syncing = syncing; }
            }
            RenderEditText();
            if (input is null) { CellInput.ResetUndoHistory(); FormulaInput.ResetUndoHistory(); }
            RefreshFormulaTextColors();
            PositionEditor();
            if (!replace) CellInput.CaretIndex = CellInput.Text?.Length ?? 0;
        }
        if (focus) { State.EditSession!.Activate(EditorSurface.Cell); CellInput.Focus(); }
        Worksheet.InvalidateVisual();
    }

    internal void BeginDoubleClickEdit()
    {
        // The original preserves content but enters accept mode; F2 enters edit mode.
        BeginEdit(false);
        State.EditSession?.SetAcceptMode(true);
    }

    private void EditorTextChanged(CellEditor editor)
    {
        if (_syncing || !CanEdit || Session is null || Snapshot is null) return;
        if (State.EditSession is null)
        {
            if (ReferenceEquals(editor, CellInput) && string.IsNullOrEmpty(editor.Text)) return;
            BeginEdit(ReferenceEquals(editor, CellInput), false, editor);
        }
        State.EditSession!.InputText(SurfaceOf(editor), editor.Text ?? "");
        RenderEditText();
        PositionEditor();
        UpdateReferenceColors();
        QueueCompletion(editor);
        Worksheet.InvalidateVisual();
    }

    public bool CommitEdit(bool array = false)
    {
        if (!CanEdit)
        {
            if (State.Editing is not null) CancelEdit();
            return true;
        }
        if (State.Editing is not { } editing || Session is null) return true;
        CellInput.FinishComposition(true); FormulaInput.FinishComposition(true);
        var text = State.EditSession!.Text;
        try
        {
            // Close editor state before the model raises Changed, preserving only an error for correction.
            EndEditSession();
            if (array && Snapshot is not null)
            {
                var area = CellArea.FromView(Snapshot.View);
                Session.Execute("set_array_formula", new { sheet = editing.Sheet, row = editing.Row, column = editing.Column, width = area.Width, height = area.Height, input = text });
            }
            else if (text != editing.Original) Session.SetUserInput(editing.Sheet, editing.Row, editing.Column, text);
            // Like the original Editor, commit to the editing owner without
            // switching the viewed sheet. Enter/Tab navigate that sheet;
            // Escape below explicitly restores the editing owner's selection.
            Refresh(); return true;
        }
        catch (IronCalcException exception) { CancelEdit(editing); ShowWorkbookAlert(Text.Key("error_dialog.error_editing_formula"), Text.Compose(_ => exception.Message)); return false; }
    }

    public void CancelEdit() => CancelEdit(EndEditSession());

    private void CancelEdit(EditingCell? editing)
    {
        if (editing is { OwnerDeleted: false } && Session is { } session)
        {
            Run(() =>
            {
                var current = session.Snapshot();
                var owner = Array.FindIndex(current.Worksheets, sheet => sheet.SheetId == editing.SheetId &&
                    sheet.State.Equals("visible", StringComparison.OrdinalIgnoreCase));
                if (owner < 0) return;
                if (current.View.Sheet != owner) session.SetSelectedSheet(owner);
                if (current.View.Sheet != owner || current.View.Row != editing.Row || current.View.Column != editing.Column)
                    session.SetSelectedCell(editing.Row, editing.Column);
            });
        }
        Refresh(); FocusCell();
    }

    private void ApplyEditorHistory(CellEditor editor, bool redo)
    {
        var before = editor.Text;
        var syncing = _syncing; _syncing = true;
        try
        {
            // Both controls record the same text changes. Advance both native histories
            // together: copying an Undo result as Text would create a new Undo entry.
            if (redo) { CellInput.Redo(); FormulaInput.Redo(); }
            else { CellInput.Undo(); FormulaInput.Undo(); }
        }
        finally { _syncing = syncing; }
        if (editor.Text == before) return;
        EditorTextChanged(editor);
        State.EditSession?.CaptureInputCursor(SurfaceOf(editor), editor.SelectionStart, editor.SelectionEnd);
    }

    private bool TryStartReference(int row, int column)
    {
        if (State.EditSession is not { } edit || Snapshot is not { } snapshot) return false;
        if (!edit.TryStartReference(snapshot, row, column)) return false;
        RenderReference(edit);
        return true;
    }

    private void UpdateReference(int row, int column)
    {
        if (State.EditSession is not { } edit || Snapshot is not { } snapshot) return;
        edit.UpdateReference(snapshot, row, column);
        RenderReference(edit);
    }

    private void RenderReference(WorkbookEditSession edit)
    {
        RenderEditText();
        PositionEditor(); UpdateReferenceColors();
        var editor = ActiveEditor;
        editor.CaretIndex = edit.ReferenceCaret;
        QueueCompletion(editor);
        Worksheet.InvalidateVisual();
        editor.Focus();
    }

    internal void EndReferenceDrag(bool restoreFocus = true)
    {
        if (State.EditSession is not { } edit) return;
        edit.EndReferenceDrag();
        if (restoreFocus && edit.HasReference) ActiveEditor.Focus();
    }

    internal void PreserveReferenceFocusOnSheetChange()
    {
        // A host can change sheets while a reference pointer is captured. Move the
        // input target before hiding the cell editor so its deferred blur cannot
        // commit the draft as a side effect of cancelling the old gesture.
        if (State.EditSession is { IsSelectingReference: true } edit && CellInput.IsFocused &&
            Snapshot is { } snapshot && !edit.IsVisibleOnSheet(snapshot.View.Sheet))
        {
            edit.Activate(EditorSurface.FormulaBar);
            FormulaInput.Focus();
        }
    }

    private bool TryArrowReference(Key key, bool extend)
    {
        if (State.EditSession is not { } edit || Snapshot is not { } snapshot) return false;
        if (!edit.TryMoveReference(snapshot,
            key == Key.Up ? -1 : key == Key.Down ? 1 : 0,
            key == Key.Left ? -1 : key == Key.Right ? 1 : 0, extend)) return false;
        RenderReference(edit);
        return true;
    }

    // Text stays colored while idle and immediately after F2; active grid ranges
    // still belong to the edit session and only change through input/reference updates.
    private void RefreshFormulaTextColors()
    {
        FormulaReference[] references = [];
        if (CanEdit && Session is { } session && Snapshot is { } snapshot)
        {
            var edit = State.EditSession;
            Run(() => references = FormulaReferences.Parse(edit?.Cell.Model ?? session, snapshot,
                edit?.Cell.Sheet ?? snapshot.View.Sheet, edit?.Text ?? snapshot.Cell.Content));
        }
        FormulaInput.References = references;
        CellInput.References = State.Editing is null ? [] : references;
    }

    private void UpdateReferenceColors()
    {
        if (State.EditSession is not { } edit || Snapshot is not { } snapshot) return;
        Run(() =>
        {
            edit.RefreshReferences(snapshot);
            CellInput.References = State.References; FormulaInput.References = State.References;
        });
    }

    private bool IsSheetReferenceMode() => State.EditSession?.IsSheetReferenceMode() == true;

    private void SelectSheet(int sheet)
    {
        if (State.EditSession is { } edit && edit.IsSheetReferenceMode())
        {
            var cursor = ActiveEditor.CaretIndex;
            edit.SelectReferenceSheet(sheet);
            FormulaInput.Focus(); FormulaInput.CaretIndex = cursor;
        }
        else Command("select_sheet", new { sheet });
    }

    private void UpdateCompletion(CellEditor editor)
    {
        if (State.Editing is not { } editing || Session is null || editor.IsComposing) { _formulaHelper.Close(); return; }
        _formulaHelper.Update(Session, editing, editor);
    }
    internal static (int Start, int End) ReferenceCycleRequestOffsets(string text, int start, int end) =>
        (ScalarOffset(text, start), ScalarOffset(text, end));
    internal static void ApplyReferenceCycle(CellEditor editor, JsonElement result)
    {
        editor.Text = result[0].GetString();
        editor.SelectionStart = Utf16Offset(editor.Text!, result[1].GetInt32());
        editor.SelectionEnd = Utf16Offset(editor.Text!, result[2].GetInt32());
    }
    // TextBox offsets are UTF-16; the Rust reference/completion APIs use scalar
    // indices. Keep that representation conversion at the .NET input boundary.
    internal static int ScalarOffset(string text, int utf16) => EditorTextOffsets.Scalar(text, utf16);
    internal static int Utf16Offset(string text, int scalar) => EditorTextOffsets.Utf16(text, scalar);
}
