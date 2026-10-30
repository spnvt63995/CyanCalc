namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    // Pinned upstream Toolbar.tsx uses the active cell's alignment, including
    // for a mixed selection, then applies one target to the entire range.
    private string HorizontalAlignmentTarget(string requested) =>
        Snapshot?.Cell.ExtendedStyle.Style.Alignment?.Horizontal == requested
            ? "general" : requested;
}
