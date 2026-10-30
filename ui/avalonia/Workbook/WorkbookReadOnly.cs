namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    private bool _canEdit = true;

    /// <summary>
    /// Whether this host view accepts document edits. This is a UI setting, not
    /// an authorization boundary on the host-owned <see cref="Session"/>.
    /// </summary>
    public bool CanEdit
    {
        get => _canEdit;
        set
        {
            if (_canEdit == value) return;
            _canEdit = value;
            if (!value)
            {
                Worksheet.CancelPointerGesture();
                // A host may switch mode while an IME or formula edit is dirty.
                // Cancel the buffer before a deferred blur can commit it.
                if (State.Editing is not null) CancelEdit();
                CloseToolbarPopups(); _toolbarDialog?.Close();
                _drawer.IsVisible = false;
                _nameMenuPreservesEdit = false;
                State.CutRange = null;
                State.IsFormatPainter = false;
                _paintStyles = null;
            }
            CellInput.IsReadOnly = !value;
            FormulaInput.IsReadOnly = !value;
            Refresh();
        }
    }

    // Selection and viewport commands are still useful to a viewing host.
    // Keep this list explicit: newly added engine operations default to edits.
    internal bool AllowsWorkbookCommand(string operation) => CanEdit || operation is
        "navigate" or "navigate_edge" or "select_area" or "expand_selection" or
        "select_sheet" or "scroll";
}
