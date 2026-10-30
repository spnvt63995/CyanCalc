using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace CyanCalc.Controls;

// Owns transient gesture state. All document/selection/edit commands stay in WorkbookView;
// hit testing and fill-handle geometry use the canvas's existing shared layout.
internal sealed class WorksheetPointerController(WorksheetCanvas canvas)
{
    private WorkbookView? Workbook => canvas.Workbook;
    private SheetLayout Layout => canvas.Layout;
    private readonly record struct GestureOwner(Guid Session, int SheetId);
    private GestureOwner? _gestureOwner;
    private IPointer? _capturedPointer;
    private bool _dragging;
    private bool _selectingRange;
    private bool _filling;
    private bool _selectRows;
    private bool _selectColumns;
    private int _headerAnchor;
    private readonly record struct CellClickOwner(Guid Session, int SheetId, int Row, int Column);
    private CellClickOwner? _completedCellClick;
    private (CellClickOwner Owner, Rect Bounds, bool DoubleClick)? _cellClick;
    private (bool Row, int Index, double Start, double Size)? _resize;
    private Point _lastPointer;
    private DispatcherTimer? _autoScrollTimer;

    internal bool IsSelectingRange => _selectingRange;
    internal (bool Row, double Position)? ResizeGuide => _resize is { } resize
        ? (resize.Row, resize.Row ? _lastPointer.Y : _lastPointer.X) : null;

    private GestureOwner? CurrentOwner => Workbook?.Session is { } session && Workbook.Snapshot is { } snapshot
        ? new(session.Identity, snapshot.Worksheets[snapshot.View.Sheet].SheetId) : null;

    private bool OwnsGesture(IPointer pointer) =>
        ReferenceEquals(_capturedPointer, pointer) && ReferenceEquals(pointer.Captured, canvas) &&
        _gestureOwner is { } owner && owner == CurrentOwner;

    internal void SyncContext()
    {
        // Observe each sheet transition, even when the host switches away and back
        // before the next pointer event or timer tick.
        if (_capturedPointer is { } pointer && !OwnsGesture(pointer))
        {
            Workbook?.PreserveReferenceFocusOnSheetChange();
            Cancel();
        }
    }

    internal void Cancel()
    {
        _autoScrollTimer?.Stop();
        var pointer = _capturedPointer;
        // Clear ownership before releasing capture, which raises CaptureLost synchronously.
        _capturedPointer = null; _gestureOwner = null;
        _dragging = _selectingRange = _filling = _selectRows = _selectColumns = false;
        _headerAnchor = 0;
        _resize = null; _cellClick = null; _completedCellClick = null;
        if (Workbook is { } workbook)
        {
            workbook.State.FillPlan = null;
            workbook.EndReferenceDrag(restoreFocus: false);
        }
        if (ReferenceEquals(pointer?.Captured, canvas)) pointer!.Capture(null);
        canvas.Cursor = null;
        canvas.InvalidateVisual();
    }

    internal void Pressed(PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(canvas).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsRightButtonPressed) return;
        var previousClick = _completedCellClick;
        Cancel();
        if (Workbook?.Session is null || Workbook.Snapshot is null) return;
        var point = e.GetPosition(canvas);
        _lastPointer = point;
        var hit = Layout.HitTest(point);
        if (properties.IsRightButtonPressed)
        {
            if (!Workbook.CanEdit) { e.Handled = true; return; }
            Workbook.SelectAt(hit.Row, hit.Column, false, true);
            canvas.ContextMenu = Workbook.BuildCellContextMenu(hit.Row == 0, hit.Column == 0);
            canvas.ContextMenu.Open(canvas); e.Handled = true; return;
        }
        if (!properties.IsLeftButtonPressed) return;
        _selectingRange = false;
        if (Workbook.CanEdit && point.X < SheetLayout.RowHeader && Layout.Rows.FirstOrDefault(x => Math.Abs(x.End - point.Y) < 4) is { } row)
            _resize = (true, row.Index, point.Y, row.Size);
        else if (Workbook.CanEdit && point.Y < SheetLayout.ColumnHeader && Layout.Columns.FirstOrDefault(x => Math.Abs(x.End - point.X) < 4) is { } column)
            _resize = (false, column.Index, point.X, column.Size);
        else if (canvas.FillHandle()?.Inflate(3).Contains(point) == true)
            _filling = true;
        else
        {
            var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            Workbook.SelectAt(hit.Row, hit.Column, extend, false);
            // Shift extends selection once. Reference insertion has its own drag
            // lifecycle and remains draggable with Shift held.
            if (extend && Workbook.State.EditSession?.IsSelectingReference != true)
            {
                // Avalonia can capture the pressed control before this handler.
                if (ReferenceEquals(e.Pointer.Captured, canvas)) e.Pointer.Capture(null);
                Workbook.FocusCell();
                e.Handled = true;
                return;
            }
            _selectRows = hit.Column == 0 && hit.Row != 0;
            _selectColumns = hit.Row == 0 && hit.Column != 0;
            _headerAnchor = _selectRows ? hit.Row : hit.Column;
            _dragging = true;
            if (hit.Row > 0 && hit.Column > 0 && Workbook.State.Editing is null &&
                TopLevel.GetTopLevel(canvas)?.PlatformSettings is { } settings && Workbook.Snapshot is { } selected)
            {
                var owner = new CellClickOwner(Workbook.Session.Identity, selected.Worksheets[selected.View.Sheet].SheetId,
                    selected.View.Row, selected.View.Column);
                var size = settings.GetTapSize(e.Pointer.Type);
                _cellClick = (owner, new Rect(point.X - size.Width / 2, point.Y - size.Height / 2, size.Width, size.Height),
                    e.ClickCount == 2 && previousClick == owner);
            }
        }
        _gestureOwner = CurrentOwner;
        _capturedPointer = e.Pointer;
        e.Pointer.Capture(canvas);
        e.Handled = true;
    }

    internal void Moved(PointerEventArgs e)
    {
        if (_capturedPointer is not null)
        {
            if (!ReferenceEquals(_capturedPointer, e.Pointer)) return;
            if (!OwnsGesture(e.Pointer) || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed)
            {
                Cancel();
                return;
            }
        }
        _lastPointer = e.GetPosition(canvas);
        // Upstream keeps selecting during the second press and opens the editor on release,
        // even when that press has dragged beyond the platform tap bounds.
        if (_cellClick is { } click && !click.DoubleClick && !click.Bounds.Contains(_lastPointer)) _cellClick = null;
        if (_resize is not null) { canvas.InvalidateVisual(); return; }
        if (_dragging || _filling)
        {
            UpdateDrag();
            UpdateAutoScroll();
        }
        else canvas.Cursor = new Cursor(canvas.FillHandle()?.Inflate(3).Contains(_lastPointer) == true ? StandardCursorType.Cross : StandardCursorType.Arrow);
    }

    private void UpdateAutoScroll()
    {
        if (_capturedPointer is not { } pointer || !OwnsGesture(pointer) || AutoScrollStep() == default)
        {
            _autoScrollTimer?.Stop();
            return;
        }
        if (_autoScrollTimer is null)
        {
            _autoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _autoScrollTimer.Tick += (_, _) => AutoScroll();
        }
        if (!_autoScrollTimer.IsEnabled) _autoScrollTimer.Start();
    }

    private Vector AutoScrollStep()
    {
        if ((!_dragging && !_filling) || _resize is not null) return default;
        var offset = canvas.ScrollOffset;
        return new(_selectRows ? 0 : Step(_lastPointer.X, canvas.Bounds.Width, offset.X, Layout.Columns, CellAddress.LastColumn),
            _selectColumns ? 0 : Step(_lastPointer.Y, canvas.Bounds.Height, offset.Y, Layout.Rows, CellAddress.LastRow));

        static double Step(double point, double extent, double offset, AxisCell[] cells, int last)
        {
            if (!double.IsFinite(point) || extent <= 0) return 0;
            // Frozen cells never scroll. An unfilled scrolling pane has reached the
            // last visible cell (possibly followed by hidden rows/columns).
            var edge = cells.LastOrDefault(cell => !cell.Frozen);
            if (edge is null) return 0;
            if (point < 0) return offset > 0 ? Math.Clamp(point, -40, -8) : 0;
            if (point <= extent || edge.End < extent || edge.Index == last && edge.End <= extent) return 0;
            return Math.Clamp(point - extent, 8, 40);
        }
    }

    private void AutoScroll()
    {
        if (_capturedPointer is not { } pointer || !OwnsGesture(pointer) ||
            !canvas.IsEffectivelyVisible || !canvas.IsEffectivelyEnabled || TopLevel.GetTopLevel(canvas) is null)
        {
            Cancel();
            return;
        }
        var step = AutoScrollStep();
        if (step == default) { _autoScrollTimer?.Stop(); return; }
        var before = canvas.ScrollOffset;
        // Move only the viewport: a workbook command could commit a reference draft.
        canvas.SetScrollPosition(before + step, () => OwnsGesture(pointer));
        // Scroll callbacks can replace the document, change sheets or end capture.
        if (!OwnsGesture(pointer)) { Cancel(); return; }
        if (canvas.ScrollOffset == before) { _autoScrollTimer?.Stop(); return; }
        UpdateDrag();
        UpdateAutoScroll();
    }

    private void UpdateDrag()
    {
        if (Workbook?.Snapshot is not { } snapshot || Workbook.Session is not { } session) return;
        // Whole-row/column drags retain their header hit policy. Cell selection,
        // references and fill use the actual captured pointer, including offscreen cells.
        var hit = _selectRows || _selectColumns
            ? Layout.HitTest(new Point(
                Math.Clamp(_lastPointer.X, SheetLayout.RowHeader + 1, Math.Max(SheetLayout.RowHeader + 1, canvas.Bounds.Width - 1)),
                Math.Clamp(_lastPointer.Y, SheetLayout.ColumnHeader + 1, Math.Max(SheetLayout.ColumnHeader + 1, canvas.Bounds.Height - 1))))
            : Layout.HitTestDrag(session, snapshot, _lastPointer);
        if (hit.Row == 0 || hit.Column == 0) return;
        if (_filling)
        {
            var area = CellArea.FromView(snapshot.View);
            Workbook.State.FillPlan = WorksheetFillPlan.Update(area, snapshot.MergedCells, hit.Row, hit.Column, Workbook.State.FillPlan);
            canvas.InvalidateVisual();
        }
        else if (_selectRows || _selectColumns)
            Workbook.ExtendHeaderSelection(_headerAnchor, _selectRows ? hit.Row : hit.Column, _selectRows);
        else
        {
            // Upstream enters selecting on pointer movement, not on the initial
            // click. Formula-reference dragging has its own overlays.
            _selectingRange = Workbook.State.Editing is null;
            Workbook.ExtendSelection(hit.Row, hit.Column);
        }
    }

    internal bool Released(PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(_capturedPointer, e.Pointer) || e.InitialPressMouseButton != MouseButton.Left)
            return false;
        if (!OwnsGesture(e.Pointer))
        {
            Cancel();
            return false;
        }
        // Stop before commit/focus callbacks can pump another dispatcher tick.
        _autoScrollTimer?.Stop();
        var owner = _gestureOwner;
        var click = _cellClick;
        var completedClick = (CellClickOwner?)null;
        var doubleClick = false;
        if (Workbook is { } workbook)
        {
            if (_resize is { } resize)
            {
                var size = Math.Max(1, resize.Size + (resize.Row ? _lastPointer.Y : _lastPointer.X) - resize.Start);
                workbook.ResizeAxis(resize.Row, resize.Index, size);
            }
            else if (_filling) workbook.CommitFill(() => OwnsGesture(e.Pointer));
            // A host can replace Session in response to the completed document command.
            if (OwnsGesture(e.Pointer))
            {
                workbook.EndReferenceDrag();
                // An active draft owns its input surface, including reference drags
                // started from the formula bar. Only idle selection returns to the cell.
                if (workbook.State.Editing is null) workbook.FocusCell();
                if (OwnsGesture(e.Pointer) && click is { } completed &&
                    (completed.DoubleClick || completed.Bounds.Contains(e.GetPosition(canvas))) &&
                    workbook.State.Editing is null && workbook.Session is { } session && workbook.Snapshot is { } selected &&
                    completed.Owner == new CellClickOwner(session.Identity, selected.Worksheets[selected.View.Sheet].SheetId,
                        selected.View.Row, selected.View.Column))
                {
                    completedClick = completed.Owner;
                    doubleClick = completed.DoubleClick;
                }
            }
        }
        Cancel();
        // Normal release preserves the completed click for the next double-click press.
        if (owner == CurrentOwner) _completedCellClick = completedClick;
        else doubleClick = false;
        canvas.SyncWheelContext(false);
        e.Handled = true;
        return doubleClick;
    }

    internal void CaptureLost(IPointer pointer)
    {
        if (ReferenceEquals(_capturedPointer, pointer)) Cancel();
        canvas.SyncWheelContext(false);
    }
}
