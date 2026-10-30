namespace CyanCalc.Controls;

// These values describe workbook input, independently of Avalonia events or controls.
internal enum WorkbookKey
{
    Other, Left, Right, Up, Down, Home, End, PageUp, PageDown,
    Enter, Tab, Escape, F2, F4, Back, Delete, Space, A, B, C, I, U, V, X, Y, Z
}
internal enum WorkbookNavigation { Left, Right, Up, Down, Home, End, PageUp, PageDown }
internal enum WorkbookEditMode { None, Accept, Edit }
internal enum WorkbookClipboard { Copy, Cut, Paste }
internal readonly record struct WorkbookModifiers(bool Primary, bool Shift, bool Alt);
internal readonly record struct WorkbookInputContext(WorkbookEditMode Mode, bool CanEdit, bool HasSheet);

internal abstract record WorkbookAction
{
    internal sealed record Navigate(WorkbookNavigation Direction) : WorkbookAction;
    internal sealed record NavigateToEdge(WorkbookNavigation Direction) : WorkbookAction;
    internal sealed record ExpandSelection(WorkbookNavigation Direction) : WorkbookAction;
    internal sealed record RowEdge(bool Last) : WorkbookAction;
    internal sealed record SelectColumn : WorkbookAction;
    internal sealed record SelectRowAndReplace : WorkbookAction;
    internal sealed record SwitchSheet(int Offset) : WorkbookAction;
    internal sealed record BeginEdit(bool Replace) : WorkbookAction;
    internal sealed record EnterTextEditMode : WorkbookAction;
    internal sealed record CommitEdit(WorkbookNavigation? Direction, bool Array = false,
        bool RestoreOwnerSheet = false, bool FocusCell = false) : WorkbookAction;
    internal sealed record CancelEdit : WorkbookAction;
    internal sealed record CancelSelection : WorkbookAction;
    internal sealed record CycleReference : WorkbookAction;
    internal sealed record ClearContents : WorkbookAction;
    // The pinned Ctrl+Shift+Z branch bypasses the ordinary canEdit command guard.
    internal sealed record History(bool Redo, bool IgnoreEditGuard = false) : WorkbookAction;
    internal sealed record ToggleFont(string Name) : WorkbookAction;
    internal sealed record Clipboard(WorkbookClipboard Operation) : WorkbookAction;
}

internal enum WorkbookKeyDisposition { NativeText, Consume, Command }
internal readonly record struct WorkbookKeyDecision(WorkbookKeyDisposition Disposition, WorkbookAction? Action = null)
{
    internal static WorkbookKeyDecision Native => default;
    internal static WorkbookKeyDecision Consume => new(WorkbookKeyDisposition.Consume);
    internal static WorkbookKeyDecision Execute(WorkbookAction action) => new(WorkbookKeyDisposition.Command, action);
    internal static WorkbookKeyDecision NativeAfter(WorkbookAction action) => new(WorkbookKeyDisposition.NativeText, action);
}

/// <summary>
/// Application key policy only. Native text history, reference selection and completion
/// get first refusal in the input adapter. This classifier never reads or changes UI/model state.
/// </summary>
internal static class WorkbookKeyBindings
{
    internal static WorkbookKeyDecision Classify(WorkbookKey key, WorkbookModifiers modifiers, WorkbookInputContext context)
    {
        var (primary, shift, alt) = modifiers;
        var direction = Direction(key);
        if (context.Mode != WorkbookEditMode.None)
        {
            return key switch
            {
                WorkbookKey.Escape => Run(new WorkbookAction.CancelEdit()),
                WorkbookKey.F4 => Run(new WorkbookAction.CycleReference()),
                WorkbookKey.Home or WorkbookKey.End when context.Mode == WorkbookEditMode.Accept =>
                    WorkbookKeyDecision.NativeAfter(new WorkbookAction.EnterTextEditMode()),
                WorkbookKey.Enter when !alt => Run(new WorkbookAction.CommitEdit(
                    shift && !primary ? WorkbookNavigation.Up : WorkbookNavigation.Down,
                    Array: primary && shift, FocusCell: true)),
                WorkbookKey.Tab => Run(new WorkbookAction.CommitEdit(
                    shift ? WorkbookNavigation.Left : WorkbookNavigation.Right)),
                _ when context.Mode == WorkbookEditMode.Accept && IsArrow(key) =>
                    Run(new WorkbookAction.CommitEdit(shift ? null : direction, RestoreOwnerSheet: true)),
                _ => WorkbookKeyDecision.Native
            };
        }

        // Keep the pinned Workbook/useKeyboardNavigation.ts precedence. In particular,
        // Ctrl+A is consumed, Ctrl+Shift+Z is unguarded and Shift+Space also starts input.
        if (primary && !shift && !alt)
        {
            return key switch
            {
                WorkbookKey.C => Run(new WorkbookAction.Clipboard(WorkbookClipboard.Copy)),
                WorkbookKey.X => Run(new WorkbookAction.Clipboard(WorkbookClipboard.Cut)),
                WorkbookKey.V => Run(new WorkbookAction.Clipboard(WorkbookClipboard.Paste)),
                WorkbookKey.Z => Run(new WorkbookAction.History(false)),
                WorkbookKey.Y => Run(new WorkbookAction.History(true)),
                WorkbookKey.B => Run(new WorkbookAction.ToggleFont("b")),
                WorkbookKey.I => Run(new WorkbookAction.ToggleFont("i")),
                WorkbookKey.U => Run(new WorkbookAction.ToggleFont("u")),
                WorkbookKey.A => WorkbookKeyDecision.Consume,
                WorkbookKey.Space => Run(new WorkbookAction.SelectColumn()),
                _ when IsArrow(key) || key is WorkbookKey.Home or WorkbookKey.End =>
                    Run(new WorkbookAction.NavigateToEdge(direction)),
                _ => EnterOrNative(key)
            };
        }
        if (primary && shift && !alt)
            return key == WorkbookKey.Z
                ? Run(new WorkbookAction.History(true, IgnoreEditGuard: true))
                : EnterOrNative(key);
        if (shift && !alt && !primary)
        {
            if (key == WorkbookKey.Space) return Run(new WorkbookAction.SelectRowAndReplace());
            if (IsArrow(key)) return Run(new WorkbookAction.ExpandSelection(direction));
            if (key == WorkbookKey.Tab) return Run(new WorkbookAction.Navigate(WorkbookNavigation.Left));
        }
        if (alt && !primary && !shift)
            return context.HasSheet && key is WorkbookKey.Up or WorkbookKey.Down
                ? Run(new WorkbookAction.SwitchSheet(key == WorkbookKey.Down ? 1 : -1))
                : EnterOrNative(key);
        if (primary || alt) return EnterOrNative(key);
        if (key == WorkbookKey.Back && context.CanEdit) return Run(new WorkbookAction.BeginEdit(true));
        if (shift) return EnterOrNative(key);
        return key switch
        {
            WorkbookKey.Left or WorkbookKey.Right or WorkbookKey.Up or WorkbookKey.Down or
                WorkbookKey.PageUp or WorkbookKey.PageDown => Run(new WorkbookAction.Navigate(direction)),
            WorkbookKey.Tab => Run(new WorkbookAction.Navigate(WorkbookNavigation.Right)),
            WorkbookKey.Enter => Run(new WorkbookAction.Navigate(WorkbookNavigation.Down)),
            WorkbookKey.F2 => Run(new WorkbookAction.BeginEdit(false)),
            WorkbookKey.Back => Run(new WorkbookAction.BeginEdit(true)),
            WorkbookKey.Delete => Run(new WorkbookAction.ClearContents()),
            WorkbookKey.Home => Run(new WorkbookAction.RowEdge(false)),
            WorkbookKey.End => Run(new WorkbookAction.RowEdge(true)),
            WorkbookKey.Escape => Run(new WorkbookAction.CancelSelection()),
            _ => WorkbookKeyDecision.Native
        };
    }

    private static WorkbookKeyDecision Run(WorkbookAction action) => WorkbookKeyDecision.Execute(action);
    private static WorkbookKeyDecision EnterOrNative(WorkbookKey key) =>
        key == WorkbookKey.Enter ? WorkbookKeyDecision.Consume : WorkbookKeyDecision.Native;
    internal static bool IsArrow(WorkbookKey key) =>
        key is WorkbookKey.Left or WorkbookKey.Right or WorkbookKey.Up or WorkbookKey.Down;
    private static WorkbookNavigation Direction(WorkbookKey key) => key switch
    {
        WorkbookKey.Left => WorkbookNavigation.Left, WorkbookKey.Right => WorkbookNavigation.Right,
        WorkbookKey.Up => WorkbookNavigation.Up, WorkbookKey.Down => WorkbookNavigation.Down,
        WorkbookKey.Home => WorkbookNavigation.Home, WorkbookKey.End => WorkbookNavigation.End,
        WorkbookKey.PageUp => WorkbookNavigation.PageUp, WorkbookKey.PageDown => WorkbookNavigation.PageDown,
        _ => default
    };
}
