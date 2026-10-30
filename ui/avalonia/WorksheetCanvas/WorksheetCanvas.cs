using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CyanCalc.Controls;

// Avalonia host for shared viewport geometry/cache, drawing and pointer dispatch.
public sealed class WorksheetCanvas : Control
{
    private readonly WorksheetRenderResources _renderResources = new();
    private SheetPalette _palette = SheetPalette.ForLight();
    internal IBrush AccentBrush => _palette.Accent;
    internal event EventHandler? PaletteChanged;
    internal WorkbookView? Workbook { get; set; }
    public SheetLayout Layout { get; private set; } = new();
    private Dictionary<(int, int), CellSnapshot> _cells = new();
    private readonly Dictionary<(int, int), MergedCell> _mergeMap = new();
    private List<MergedCellRegion> _mergedRegions = new();
    private readonly List<(CellSnapshot Cell, AxisCell Column)> _overflowCells = new();
    private (Guid Session, long Revision, int Sheet, int Row, int Column, Size Size)? _cacheKey;
    public IReadOnlyDictionary<(int, int), CellSnapshot> VisibleCells => _cells;
    private readonly WorksheetPointerController _pointer;
    private (Guid Session, int Sheet)? _wheelContext;
    private Vector _wheelPixels;
    private Vector? _lastModelScroll;
    private bool _applyingWheel;
    private readonly WorksheetSelectionExtent _selectionExtent = new();
    private Size _scrollSpacer = new(100_000, 500_000);
    internal Vector ScrollOffset => _wheelPixels;
    internal Size ScrollExtent { get; private set; } = new(100_000, 500_000);
    internal event EventHandler? ScrollPositionChanged;

    public WorksheetCanvas()
    {
        _pointer = new(this);
        ClipToBounds = true;
        DetachedFromVisualTree += (_, _) => { _pointer.Cancel(); ResetWheelScroll(); ResetRenderResources(); };
        ActualThemeVariantChanged += (_, _) => RefreshPalette();
    }

    private void RefreshPalette()
    {
        var next = SheetPalette.For(ActualThemeVariant);
        if (next.IsDark == _palette.IsDark) return;
        _palette = next;
        ResetRenderResources();
        InvalidateVisual();
        PaletteChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Workbook?.ViewportChanged(e.NewSize);
    }

    internal void CancelPointerGesture() => _pointer.Cancel();

    internal void ResetRenderResources() => _renderResources.Clear();

    internal void Refresh()
    {
        if (Workbook?.Session is not { } session || Workbook.Snapshot is not { } snapshot || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var key = (session.Identity, session.Revision, snapshot.View.Sheet, snapshot.View.TopRow, snapshot.View.LeftColumn, Bounds.Size);
        if (_cacheKey == key) { InvalidateVisual(); return; }
        Layout = SheetLayout.Create(session, snapshot, Bounds.Size);
        _cells = session.Cells(snapshot.View.Sheet, Layout.Rows.Select(x => x.Index).ToArray(), Layout.Columns.Select(x => x.Index).ToArray())
            .ToDictionary(x => (x.Row, x.Column));
        _mergeMap.Clear();
        foreach (var merge in snapshot.MergedCells)
        foreach (var row in Layout.Rows.Where(x => x.Index >= merge.Row && x.Index <= merge.LastRow))
        foreach (var column in Layout.Columns.Where(x => x.Index >= merge.Column && x.Index <= merge.LastColumn))
            _mergeMap[(row.Index, column.Index)] = merge;
        _mergedRegions = MergedCellLayout.Create(session, snapshot, Layout, Bounds.Size);
        LoadOverflowCells(session, snapshot);
        _cacheKey = key;
        InvalidateVisual();
    }

    private void LoadOverflowCells(UserModel session, WorkbookSnapshot snapshot)
    {
        _overflowCells.Clear();
        var scrolling = Layout.Columns.Where(x => !x.Frozen).ToArray();
        if (scrolling.Length == 0) return;
        var first = scrolling[0]; var last = scrolling[^1];
        var widths = Layout.Columns.ToDictionary(x => x.Index, x => x.Size);
        foreach (var row in Layout.Rows)
        {
            if (session.GetLastNonEmptyInRowBeforeColumn(snapshot.View.Sheet, row.Index, first.Index) is { } before && before > snapshot.FrozenColumns)
                Add(row.Index, before, first.Start - Span(before, first.Index - 1));
            if (session.GetFirstNonEmptyInRowAfterColumn(snapshot.View.Sheet, row.Index, last.Index) is { } after)
                Add(row.Index, after, last.End + Span(last.Index + 1, after - 1));
        }
        void Add(int row, int column, double start)
        {
            if (snapshot.MergedCells.Any(x => x.Contains(row, column))) return;
            var width = Span(column, column);
            if (width > 0) _overflowCells.Add((session.Cell(snapshot.View.Sheet, row, column), new(column, start, width, false)));
        }
        double Span(int from, int to)
        {
            if (to < from) return 0;
            foreach (var missing in Enumerable.Range(from, to - from + 1).Where(x => !widths.ContainsKey(x)).Chunk(256))
            {
                var values = session.Dimensions(snapshot.View.Sheet, [], missing).Columns;
                for (int i = 0; i < missing.Length; i++) widths[missing[i]] = values[i];
            }
            return Enumerable.Range(from, to - from + 1).Sum(x => widths[x]);
        }
    }

    /// <summary>Visible editing bounds, including a merged anchor that is outside the viewport.</summary>
    public Rect? EditorRect(int row, int column)
    {
        var region = _mergedRegions.FirstOrDefault(x => x.Merge.Contains(row, column));
        return region is null ? Layout.CellRect(row, column) : region.Bounds.Intersect(region.Clip);
    }

    public override void Render(DrawingContext context)
    {
        var workbook = Workbook;
        var state = new WorksheetRenderState(Bounds.Size, _palette, Layout, workbook?.Snapshot,
            _cells, _mergeMap, _mergedRegions, _overflowCells,
            workbook?.State.Editing is not null, _pointer.IsSelectingRange,
            workbook?.State.CutRange, workbook?.State.FillPreview, workbook?.State.References ?? [],
            FillHandle(), _pointer.ResizeGuide);
        _renderResources.SetContext(workbook?.Session?.Identity, workbook?.Snapshot?.View.Sheet ?? -1,
            _palette, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        new WorksheetRenderer(state, _renderResources).Draw(context);
    }

    internal Rect? FillHandle()
    {
        if (Workbook?.Snapshot is not { } snapshot || !Workbook.CanEdit || Workbook.State.Editing is not null || _pointer.IsSelectingRange) return null;
        var range = CellArea.FromView(snapshot.View);
        return Layout.CellRect(range.Row + range.Height - 1, range.Column + range.Width - 1) is { } rect
            ? new Rect(rect.Right - 3, rect.Bottom - 3, 6, 6) : null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pointer.Pressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointer.Moved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var doubleClick = _pointer.Released(e);
        base.OnPointerReleased(e);
        // Browser dblclick follows pointerup, after capture/release and range selection.
        if (doubleClick) Workbook?.BeginDoubleClickEdit();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _pointer.CaptureLost(e.Pointer);
        base.OnPointerCaptureLost(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
        ScrollWheel(e.Delta, e.KeyModifiers);
    }

    internal void ScrollWheel(Vector delta, KeyModifiers modifiers)
    {
        if (delta == default) return;
        SyncWheelContext(false);
        // Avalonia's physical ScrollContentPresenter uses 50 DIP per normalized unit;
        // on macOS this also reverses the platform's precise-delta / 50 conversion.
        if (modifiers.HasFlag(KeyModifiers.Shift) && delta.X == 0) delta = new(delta.Y, 0);
        double Offset(double current, double input) => double.IsFinite(input) ? current - input * 50 : current;
        SetScrollPosition(new(Offset(_wheelPixels.X, delta.X), Offset(_wheelPixels.Y, delta.Y)));
    }

    internal void SetScrollPosition(Vector offset, Func<bool>? ownsInput = null)
    {
        if (Workbook?.Session is not { } model || Workbook.Snapshot is not { } snapshot) return;
        var sheetId = snapshot.Worksheets[snapshot.View.Sheet].SheetId;
        SyncWheelContext(false);
        if (!OwnsViewport(model, sheetId) || ownsInput?.Invoke() == false) return;
        ApplyScrollPosition(offset);
        ScrollPositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool OwnsViewport(UserModel model, int sheetId) => ReferenceEquals(Workbook?.Session, model) &&
        Workbook.Snapshot is { } current && model.GetSelectedSheet() == current.View.Sheet &&
        current.Worksheets[current.View.Sheet].SheetId == sheetId;

    private void ApplyScrollPosition(Vector offset)
    {
        if (Workbook?.Session is not { } model || Workbook.Snapshot is not { } snapshot) return;
        var sheetId = snapshot.Worksheets[snapshot.View.Sheet].SheetId;
        // A command callback can change sheets before WorkbookView refreshes its
        // snapshot. Wait for that refresh instead of synchronizing a stale sheet.
        if (!OwnsViewport(model, sheetId)) return;
        _applyingWheel = true;
        try
        {
            while (true)
            {
                var next = new Vector(Math.Clamp(offset.X, 0, Math.Max(0, ScrollExtent.Width - Bounds.Width)),
                    Math.Clamp(offset.Y, 0, Math.Max(0, ScrollExtent.Height - Bounds.Height)));
                if (next == _wheelPixels) break;
                _wheelPixels = next;
                var row = SheetCoordinates.Bounded(model, snapshot.View.Sheet, true, snapshot.FrozenRows, next.Y);
                var column = SheetCoordinates.Bounded(model, snapshot.View.Sheet, false, snapshot.FrozenColumns, next.X);
                if (row != snapshot.View.TopRow || column != snapshot.View.LeftColumn) model.SetTopLeftVisibleCell(row, column);
                // The host's Changed callback may switch sheets or replace and
                // dispose this model. Do not read it or reuse its offset afterwards.
                if (!OwnsViewport(model, sheetId)) return;
                snapshot = Workbook.Snapshot!;
                _lastModelScroll = new(model.GetScrollX(), model.GetScrollY());
                UpdateScrollExtent();
                // Rendering the selected range can shrink DOM overflow. The
                // browser clamps the offset and dispatches another scroll event.
                offset = _wheelPixels;
            }
        }
        finally
        {
            _applyingWheel = false;
            if (!OwnsViewport(model, sheetId)) SyncWheelContext();
        }
    }

    private void UpdateScrollExtent()
    {
        var overflow = Workbook?.Session is { } model && Workbook.Snapshot is { } snapshot
            ? _selectionExtent.Measure(model, snapshot, _pointer.IsSelectingRange, Workbook.CanEdit) : default;
        ScrollExtent = new(Math.Max(_scrollSpacer.Width, overflow.Width), Math.Max(_scrollSpacer.Height, overflow.Height));
    }

    internal void ResetWheelScroll()
    {
        _wheelContext = null;
        _lastModelScroll = null;
        _wheelPixels = default;
        ScrollExtent = _scrollSpacer = new(100_000, 500_000);
    }

    internal void SyncWheelContext(bool refreshExtent = true)
    {
        _pointer.SyncContext();
        if (_applyingWheel) return;
        var context = Workbook?.Session is { } session && Workbook.Snapshot is { } snapshot
            ? ((Guid Session, int Sheet)?)(session.Identity, snapshot.View.Sheet) : null;
        if (_wheelContext != context) { ResetWheelScroll(); _wheelContext = context; }
        if (Workbook?.Session is not { } model) { ScrollPositionChanged?.Invoke(this, EventArgs.Empty); return; }
        var scroll = new Vector(model.GetScrollX(), model.GetScrollY());
        // The DOM spacer grows on a render/refresh, not on each native scroll event.
        // Keeping it stable during a drag also keeps the thumb under the pointer.
        if (refreshExtent || _lastModelScroll != scroll) _scrollSpacer = new(scroll.X + 100_000, scroll.Y + 500_000);
        // Keep the fractional pixel offset until navigation changes the model scroll position.
        if (_lastModelScroll != scroll) { _wheelPixels = scroll; _lastModelScroll = scroll; }
        UpdateScrollExtent();
        ApplyScrollPosition(_wheelPixels);
        ScrollPositionChanged?.Invoke(this, EventArgs.Empty);
    }
}
