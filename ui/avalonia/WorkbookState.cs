namespace CyanCalc.Controls;

// Port of components/workbookState.ts. Persistent selection/history live in Rust UserModel.
public sealed class WorkbookState
{
    internal WorkbookEditSession? EditSession { get; private set; }
    public EditingCell? Editing => EditSession?.Cell;

    internal WorkbookEditSession BeginEdit(EditingCell cell, string text)
    {
        EndEdit();
        return EditSession = new WorkbookEditSession(cell, text);
    }

    internal EditingCell? EndEdit()
    {
        var cell = Editing;
        EditSession?.Dispose();
        EditSession = null;
        return cell;
    }

    public CellArea? CutRange { get; internal set; }
    public CellArea? FillPreview => FillPlan?.Preview;
    internal WorksheetFillPlan? FillPlan { get; set; }
    public bool IsFormatPainter { get; internal set; }
    public FormulaReference[] References => EditSession?.References ?? [];
}

public sealed record EditingCell(int Sheet, int SheetId, int Row, int Column, string Original,
    bool AcceptMode, UserModel Model)
{
    // Like workbookState.ts, the draft retains its measured extent until editing ends.
    internal double EditorWidth { get; set; }
    internal double EditorHeight { get; set; }
    private long _lastRevision = Model.Revision;
    private volatile bool _ownerDeleted;
    public bool OwnerDeleted => _ownerDeleted;

    internal void Attach() => Model.Changed += ObserveOwner;
    internal void Detach() => Model.Changed -= ObserveOwner;

    private void ObserveOwner(object? sender, EventArgs args)
    {
        if (_ownerDeleted || Model.Revision == _lastRevision) return;
        _lastRevision = Model.Revision;
        try
        {
            // The engine can reuse a deleted highest SheetId. Remember the missing interval.
            if (!Model.Snapshot().Worksheets.Any(sheet => sheet.SheetId == SheetId)) _ownerDeleted = true;
        }
        catch (Exception error) when (error is InvalidOperationException or IronCalcException)
        { _ownerDeleted = true; }
    }
}
