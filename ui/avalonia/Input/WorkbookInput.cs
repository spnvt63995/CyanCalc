using Avalonia;
using Avalonia.Input;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not CellEditor editor || editor.IsComposing) return;
        State.EditSession?.Activate(SurfaceOf(editor));
        var editing = State.Editing;
        var key = WorkbookKeyOf(e.Key);
        var modifiers = new WorkbookModifiers(
            (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0,
            e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Alt));

        // Preserve precedence: native text history, accept-mode references, completion,
        // workbook policy, then TextBox. These first three need live control/session state.
        if (editing is not null)
        {
            if (Application.Current?.PlatformSettings?.HotkeyConfiguration is { } shortcuts)
            {
                var undo = shortcuts.Undo.Any(gesture => gesture.Matches(e));
                var redo = shortcuts.Redo.Any(gesture => gesture.Matches(e));
                if ((undo || redo) && editor.IsUndoEnabled)
                { ApplyEditorHistory(editor, redo); e.Handled = true; return; }
            }
            if (WorkbookKeyBindings.IsArrow(key) && editing.AcceptMode && TryArrowReference(e.Key, modifiers.Shift))
            { e.Handled = true; return; }
            if (_formulaHelper.HandleKey(e, editor)) { e.Handled = true; return; }
        }

        var mode = editing is null ? WorkbookEditMode.None
            : editing.AcceptMode ? WorkbookEditMode.Accept : WorkbookEditMode.Edit;
        var decision = WorkbookKeyBindings.Classify(key, modifiers, new(mode, CanEdit, Snapshot is not null));
        if (decision.Action is { } action) ExecuteWorkbookAction(action);
        if (decision.Disposition != WorkbookKeyDisposition.NativeText) e.Handled = true;
    }

    private static WorkbookKey WorkbookKeyOf(Key key) => key switch
    {
        Key.Left => WorkbookKey.Left, Key.Right => WorkbookKey.Right,
        Key.Up => WorkbookKey.Up, Key.Down => WorkbookKey.Down,
        Key.Home => WorkbookKey.Home, Key.End => WorkbookKey.End,
        Key.PageUp => WorkbookKey.PageUp, Key.PageDown => WorkbookKey.PageDown,
        Key.Enter => WorkbookKey.Enter, Key.Tab => WorkbookKey.Tab, Key.Escape => WorkbookKey.Escape,
        Key.F2 => WorkbookKey.F2, Key.F4 => WorkbookKey.F4,
        Key.Back => WorkbookKey.Back, Key.Delete => WorkbookKey.Delete, Key.Space => WorkbookKey.Space,
        Key.A => WorkbookKey.A, Key.B => WorkbookKey.B, Key.C => WorkbookKey.C, Key.I => WorkbookKey.I,
        Key.U => WorkbookKey.U, Key.V => WorkbookKey.V, Key.X => WorkbookKey.X, Key.Y => WorkbookKey.Y,
        Key.Z => WorkbookKey.Z, _ => WorkbookKey.Other
    };
}
