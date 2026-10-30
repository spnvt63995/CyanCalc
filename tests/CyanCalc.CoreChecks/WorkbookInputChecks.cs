using CyanCalc.Controls;

internal static class WorkbookInputChecks
{
    internal static void Run()
    {
        var idle = new WorkbookInputContext(WorkbookEditMode.None, true, true);
        var edit = idle with { Mode = WorkbookEditMode.Edit };
        var accept = idle with { Mode = WorkbookEditMode.Accept };

        // Workbook commands must leave ordinary text editing to the platform, even
        // with primary/shift/alt modifiers. Text Undo is intercepted by the adapter.
        foreach (var key in new[] { WorkbookKey.Other, WorkbookKey.Left, WorkbookKey.Right,
                     WorkbookKey.Up, WorkbookKey.Down, WorkbookKey.Home, WorkbookKey.End,
                     WorkbookKey.PageUp, WorkbookKey.PageDown, WorkbookKey.Back, WorkbookKey.Delete,
                     WorkbookKey.A, WorkbookKey.B, WorkbookKey.C, WorkbookKey.X, WorkbookKey.V,
                     WorkbookKey.Z, WorkbookKey.Y })
        foreach (var modifiers in Modifiers())
            Native(key, modifiers, edit);
        foreach (var modifiers in Modifiers()) Native(WorkbookKey.Other, modifiers, idle);

        // Home/End switch accept input to text navigation without consuming the
        // native boundary movement or Shift selection. Already-editing input stays native.
        foreach (var key in new[] { WorkbookKey.Home, WorkbookKey.End })
        foreach (var modifiers in Modifiers())
            Check(key, modifiers, accept, WorkbookKeyDecision.NativeAfter(new WorkbookAction.EnterTextEditMode()));

        // Page movement stays with the native control without changing accept mode.
        foreach (var key in new[] { WorkbookKey.PageUp, WorkbookKey.PageDown })
        foreach (var modifiers in Modifiers()) Native(key, modifiers, accept);

        // Accept mode arrows commit the owner, while text-edit arrows stay native.
        foreach (var (key, direction) in new[]
        {
            (WorkbookKey.Left, WorkbookNavigation.Left), (WorkbookKey.Right, WorkbookNavigation.Right),
            (WorkbookKey.Up, WorkbookNavigation.Up), (WorkbookKey.Down, WorkbookNavigation.Down)
        })
        foreach (var modifiers in Modifiers())
            Action(key, modifiers, accept, new WorkbookAction.CommitEdit(
                modifiers.Shift ? null : direction, RestoreOwnerSheet: true));

        foreach (var context in new[] { edit, accept })
        foreach (var modifiers in Modifiers())
        {
            Action(WorkbookKey.Tab, modifiers, context, new WorkbookAction.CommitEdit(
                modifiers.Shift ? WorkbookNavigation.Left : WorkbookNavigation.Right));
            if (modifiers.Alt) Native(WorkbookKey.Enter, modifiers, context);
            else Action(WorkbookKey.Enter, modifiers, context, new WorkbookAction.CommitEdit(
                modifiers.Shift && !modifiers.Primary ? WorkbookNavigation.Up : WorkbookNavigation.Down,
                Array: modifiers.Primary && modifiers.Shift, FocusCell: true));
            Action(WorkbookKey.Escape, modifiers, context, new WorkbookAction.CancelEdit());
            Action(WorkbookKey.F4, modifiers, context, new WorkbookAction.CycleReference());
        }

        var primary = new WorkbookModifiers(true, false, false);
        var shifted = new WorkbookModifiers(false, true, false);
        var primaryShift = new WorkbookModifiers(true, true, false);
        var alternate = new WorkbookModifiers(false, false, true);
        foreach (var writable in new[] { true, false })
        {
            var context = idle with { CanEdit = writable };
            // Guarded UI operations stay guarded in the shared command executor.
            Action(WorkbookKey.Z, primary, context, new WorkbookAction.History(false));
            Action(WorkbookKey.Y, primary, context, new WorkbookAction.History(true));
            Action(WorkbookKey.Z, primaryShift, context, new WorkbookAction.History(true, IgnoreEditGuard: true));
            Consume(WorkbookKey.A, primary, context);
            Action(WorkbookKey.Space, shifted, context, new WorkbookAction.SelectRowAndReplace());
            Action(WorkbookKey.Space, primary, context, new WorkbookAction.SelectColumn());
            Action(WorkbookKey.Back, default, context, new WorkbookAction.BeginEdit(true));
            if (writable) Action(WorkbookKey.Back, shifted, context, new WorkbookAction.BeginEdit(true));
            else Native(WorkbookKey.Back, shifted, context);
        }
        Action(WorkbookKey.Tab, default, idle, new WorkbookAction.Navigate(WorkbookNavigation.Right));
        Action(WorkbookKey.Tab, shifted, idle, new WorkbookAction.Navigate(WorkbookNavigation.Left));
        Consume(WorkbookKey.Enter, shifted, idle);
        Consume(WorkbookKey.Enter, primary, idle);
        Action(WorkbookKey.F2, default, idle, new WorkbookAction.BeginEdit(false));
        Action(WorkbookKey.Escape, default, idle, new WorkbookAction.CancelSelection());
        Action(WorkbookKey.Home, default, idle, new WorkbookAction.RowEdge(false));
        Action(WorkbookKey.End, default, idle, new WorkbookAction.RowEdge(true));
        Action(WorkbookKey.Home, primary, idle, new WorkbookAction.NavigateToEdge(WorkbookNavigation.Home));
        Action(WorkbookKey.End, primary, idle, new WorkbookAction.NavigateToEdge(WorkbookNavigation.End));
        Action(WorkbookKey.Up, alternate, idle, new WorkbookAction.SwitchSheet(-1));
        Action(WorkbookKey.Down, alternate, idle, new WorkbookAction.SwitchSheet(1));
        Native(WorkbookKey.Down, alternate, idle with { HasSheet = false });

        // Unassigned modifier combinations must not become plain worksheet movement.
        foreach (var key in new[] { WorkbookKey.Left, WorkbookKey.Right, WorkbookKey.Up, WorkbookKey.Down,
                     WorkbookKey.Home, WorkbookKey.End, WorkbookKey.PageUp, WorkbookKey.PageDown })
        {
            Native(key, primaryShift, idle);
            Native(key, new(true, false, true), idle);
            Native(key, new(true, true, true), idle);
        }
    }

    private static IEnumerable<WorkbookModifiers> Modifiers()
    {
        for (var bits = 0; bits < 8; bits++) yield return new((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0);
    }

    private static void Native(WorkbookKey key, WorkbookModifiers modifiers, WorkbookInputContext context) =>
        Check(key, modifiers, context, WorkbookKeyDecision.Native);
    private static void Consume(WorkbookKey key, WorkbookModifiers modifiers, WorkbookInputContext context) =>
        Check(key, modifiers, context, WorkbookKeyDecision.Consume);
    private static void Action(WorkbookKey key, WorkbookModifiers modifiers, WorkbookInputContext context, WorkbookAction action) =>
        Check(key, modifiers, context, WorkbookKeyDecision.Execute(action));
    private static void Check(WorkbookKey key, WorkbookModifiers modifiers, WorkbookInputContext context, WorkbookKeyDecision expected)
    {
        var actual = WorkbookKeyBindings.Classify(key, modifiers, context);
        if (actual != expected)
            throw new InvalidOperationException($"Workbook key policy: {context} {key} {modifiers}: expected {expected}, got {actual}");
    }
}
