using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;

namespace CyanCalc.Controls;

/// <summary>Reusable native workbook UI. The host owns Session and file/document lifetime.</summary>
public sealed partial class WorkbookView : UserControl
{
    public WorkbookText Text { get; } = new();
    public string UiLanguage { get => Text.Language; set => Text.Language = value; }
    private UserModel? _session;
    public UserModel? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value)) return;
            Worksheet.CancelPointerGesture();
            // The original document switch saves the pre-animation order. Discard
            // its pending presentation before the host can dispose the old model.
            _conditionalReorder.Cancel();
            var drawerState = value is not null && _drawer.IsVisible ? _captureDrawerState?.Invoke() : null;
            _captureDrawerState = null;
            CloseToolbarPopups(); _toolbarDialog?.Close(); _alertDialog?.Close();
            Worksheet.ResetWheelScroll();
            Worksheet.ResetRenderResources();
            if (_session is not null) _session.Changed -= ModelChanged;
            EndEditSession(); State.CutRange = null; State.IsFormatPainter = false; _paintStyles = null;
            // Detaching can resolve inherited theme colors through the previous model.
            // The host may dispose that model as soon as this setter returns.
            _drawer.IsVisible = false; _drawer.Child = null;
            _drawerFeature = null; _previewDrawerPreservesEdit = false;
            _lastDocumentRevision = -1; _session = value;
            if (_session is not null && _attached) _session.Changed += ModelChanged;
            // A new model needs the arranged viewport even when the canvas size is unchanged.
            UpdateViewport(Worksheet.Bounds.Size);
            Refresh();
            if (drawerState is not null) RestoreDrawerState(drawerState);
            RefreshDrawerButtons();
        }
    }
    public WorkbookSnapshot? Snapshot { get; private set; }
    public WorkbookState State { get; } = new();
    public WorksheetCanvas Worksheet { get; }
    public CellEditor CellInput { get; }
    public CellEditor FormulaInput { get; }
    public event EventHandler<string>? Error;
    public event EventHandler<LocalizedText>? LocalizedError;
    public event EventHandler? DocumentChanged;
    private readonly TextBlock _address = new() { Name = "CellAddress", VerticalAlignment = VerticalAlignment.Center };
    private readonly SheetTabBar _sheetTabs;
    private readonly Border _drawer = new() { Classes = { "workbook-drawer" }, IsVisible = false, Background = Brushes.White, BorderBrush = ConditionalRenderer.Brush("#DCE2EB"), BorderThickness = new Thickness(1, 0, 0, 0) };
    private readonly Canvas _editorLayer = new() { ClipToBounds = true };
    private readonly Border _cellEditorFrame = new() { IsVisible = false, IsHitTestVisible = false, BorderThickness = new Thickness(2) };
    private bool _syncing;
    private bool _refreshing;
    private bool _attached;
    private int _attachmentVersion;
    private long _lastDocumentRevision = -1;
    private Button? _undoButton;
    private Button? _redoButton;

    public WorkbookView()
    {
        WorkbookChrome.Attach(this);
        FontFamily = WorkbookDialog.Font; FontSize = 12;
        _formulaHelper = new FormulaHelper(Text) { OpenLink = uri => OpenExternalLink?.Invoke(uri) };
        Background = Brushes.White;
        Worksheet = new WorksheetCanvas { Workbook = this, Name = "WorksheetCanvas" };
        CellInput = new CellEditor
        {
            Name = "CellEditor", MinHeight = 0, MinWidth = 0, Padding = new Thickness(3, 0), BorderThickness = new Thickness(1),
            BorderBrush = Worksheet.AccentBrush, Background = Brushes.Transparent, Foreground = Brushes.Transparent,
            IsHitTestVisible = false, Text = "", AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Top, FontFamily = WorkbookDialog.Font
        };
        CellInput.Classes.Add("workbook-editor");
        ScrollViewer.SetHorizontalScrollBarVisibility(CellInput, ScrollBarVisibility.Hidden);
        ScrollViewer.SetVerticalScrollBarVisibility(CellInput, ScrollBarVisibility.Hidden);
        Worksheet.PaletteChanged += (_, _) => CellInput.BorderBrush = Worksheet.AccentBrush;
        FormulaInput = new CellEditor { Name = "FormulaEditor", MinHeight = 0, Height = 22, Padding = new Thickness(0), BorderThickness = new Thickness(0), AcceptsReturn = true, FontSize = 12, FontFamily = WorkbookDialog.Font };
        FormulaInput.Classes.Add("workbook-editor");
        ScrollViewer.SetHorizontalScrollBarVisibility(FormulaInput, ScrollBarVisibility.Hidden);
        ScrollViewer.SetVerticalScrollBarVisibility(FormulaInput, ScrollBarVisibility.Hidden);
        _sheetTabs = new SheetTabBar(SelectSheet, TrySheetCommand, ShowSettings, Report,
            () => (Session?.Identity ?? Guid.Empty, Session?.Revision ?? -1), () => Session?.Snapshot(), Text);
        _sheetTabs.ColorPickerRequested = ShowSheetColorPicker;
        _sheetTabs.PreserveEditorFocus = IsSheetReferenceMode;
        LinkCanEdit = () => CanEdit;
        InternalLinkNavigator = TryFollowInternalLink;
        InstallLinkUi();
        _editorLayer.Children.Add(_cellEditorFrame); _editorLayer.Children.Add(CellInput);
        var worksheetLayer = new Grid { Children = { Worksheet, _editorLayer } };
        var worksheetGrid = new WorksheetScrollHost(Worksheet, worksheetLayer);
        var formula = new Grid { ColumnDefinitions = new("Auto,25,23,*"), Height = 40, Margin = new Thickness(8, 0, 0, 0) };
        var nameMenu = CreateNameMenuButton(() => CanEdit);
        nameMenu.Height = 24; nameMenu.Padding = new Thickness(8, 0);
        nameMenu.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _address, new WorkbookIcon("ChevronDown", 12) } };
        formula.Children.Add(nameMenu);
        var divider = new Border { Width = 1, Height = 16, Background = ConditionalRenderer.Brush("#DFE5EE"), Margin = new Thickness(8, 0, 16, 0) };
        Grid.SetColumn(divider, 1); formula.Children.Add(divider);
        var fx = new WorkbookIcon("Fx", 15) { Margin = new Thickness(0, 0, 8, 0), Foreground = ConditionalRenderer.Brush("#828282") };
        Grid.SetColumn(fx, 2); formula.Children.Add(fx); Grid.SetColumn(FormulaInput, 3); formula.Children.Add(FormulaInput);
        Text.Key("native.formula_bar").Describe(FormulaInput);
        var bottom = new DockPanel { MinHeight = 40, Background = ConditionalRenderer.Brush("#F4F6F9"), LastChildFill = true };
        bottom.Children.Add(_sheetTabs);
        var sheetBody = new Grid { RowDefinitions = new("40,*,Auto") };
        sheetBody.Children.Add(formula); Grid.SetRow(worksheetGrid, 1); sheetBody.Children.Add(worksheetGrid);
        Grid.SetRow(bottom, 2); sheetBody.Children.Add(bottom);
        var root = new Grid { RowDefinitions = new("Auto,*"), ColumnDefinitions = new("*,Auto") };
        var toolbarControl = BuildToolbar(); Grid.SetColumnSpan(toolbarControl, 2); root.Children.Add(toolbarControl);
        Grid.SetRow(sheetBody, 1); root.Children.Add(sheetBody);
        Grid.SetRow(_drawer, 1); Grid.SetColumn(_drawer, 1); root.Children.Add(_drawer);
        InstallDrawerResize(root);
        Content = root;
        void UpdateChromePalette()
        {
            var palette = SheetPalette.For(ActualThemeVariant);
            Background = _drawer.Background = bottom.Background = palette.Chrome;
            Foreground = palette.Token("common-black");
            _drawer.BorderBrush = divider.Background = palette.Separator;
            FormulaInput.Background = palette.Input; FormulaInput.Foreground = palette.DefaultText;
            fx.Foreground = palette.Token("grey-600");
            if (_toolbarRoot is Border toolbar) { toolbar.Background = palette.Chrome; toolbar.BorderBrush = palette.Separator; }
            if (_toolbarBack is not null) _toolbarBack.Background = palette.Chrome;
            if (_toolbarForward is not null) _toolbarForward.Background = palette.Chrome;
            if (_fontSizeInput is not null) { _fontSizeInput.Background = palette.Input; _fontSizeInput.Foreground = palette.DefaultText; }
            PositionEditor();
        }
        ActualThemeVariantChanged += (_, _) => UpdateChromePalette();
        UpdateChromePalette();
        InstallEditorInput();
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            if (_session is not null) { _session.Changed -= ModelChanged; _session.Changed += ModelChanged; }
            Refresh();
        };
        DetachedFromVisualTree += (_, _) => { _attached = false; _attachmentVersion++; if (_session is not null) _session.Changed -= ModelChanged; };
    }

    private void ModelChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Refresh); return; }
        Refresh();
    }

    public void Refresh()
    {
        if (_refreshing) return;
        _refreshing = _syncing = true;
        try
        {
            Snapshot = Session?.Snapshot();
            Worksheet.SyncWheelContext();
            IsEnabled = Snapshot is not null;
            if (Snapshot is not { } snapshot)
            {
                RenderIdleText(""); _address.Text = ""; CellInput.IsVisible = _cellEditorFrame.IsVisible = false;
                RefreshFormulaTextColors();
                HideLinkTooltip(); _sheetTabs.Clear(); Worksheet.InvalidateVisual(); return;
            }
            _address.Text = CellArea.FromView(snapshot.View) is { Width: 1, Height: 1 } ? CellAddress.Format(snapshot.View.Row, snapshot.View.Column) : CellArea.FromView(snapshot.View).Address;
            if (State.Editing is null) RenderIdleText(snapshot.Cell.Content);
            else RenderEditText();
            RefreshFormulaTextColors();
            if (_undoButton is not null) _undoButton.IsEnabled = CanEdit && snapshot.CanUndo;
            if (_redoButton is not null) _redoButton.IsEnabled = CanEdit && snapshot.CanRedo;
            RefreshToolbar(snapshot.Cell.ExtendedStyle.Style);
            Worksheet.Refresh(); RefreshLinks(); PositionEditor(); _sheetTabs.Refresh(snapshot, CanEdit, Session!.ResolveColor);
            if (_lastDocumentRevision != Session!.Revision)
            {
                _lastDocumentRevision = Session.Revision;
                DocumentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception exception) when (exception is IronCalcException or InvalidOperationException) { Report(exception.Message); }
        finally { _syncing = _refreshing = false; }
    }

    internal void ViewportChanged(Size size)
    {
        UpdateViewport(size);
        Refresh();
    }

    private void UpdateViewport(Size size)
    {
        if (Session is null || size.Width < 1 || size.Height < 1) return;
        Session.Query("set_viewport", new { width = Math.Max(1, size.Width - SheetLayout.RowHeader), height = Math.Max(1, size.Height - SheetLayout.ColumnHeader) });
    }

    private void PositionEditor()
    {
        if (Snapshot is not { } snapshot) return;
        var editing = State.Editing;
        var rect = Worksheet.EditorRect(editing?.Row ?? snapshot.View.Row, editing?.Column ?? snapshot.View.Column);
        // A text selection can scroll its owning cell out of the visible row/column
        // cache. Keep the draft's full bounds, as the original worksheet editor does.
        if (rect is null && editing is not null && Session is { } session)
            rect = new Rect(SheetCoordinates.Position(session, snapshot, editing.Row, editing.Column),
                new Size(editing.EditorWidth, editing.EditorHeight));
        // The selection anchor can leave the viewport during a range drag. Keep
        // the transparent idle TextBox focusable so release restores keyboard/IME
        // input, including Undo after a fill, without moving the viewport back.
        if (rect is null && editing is null) rect = new Rect(0, 0, 1, 1);
        CellInput.IsVisible = rect is not null && (State.EditSession?.IsVisibleOnSheet(snapshot.View.Sheet) ?? true);
        _cellEditorFrame.IsVisible = CellInput.IsVisible && editing is not null;
        if (rect is not { } bounds) return;
        Canvas.SetLeft(CellInput, bounds.X); Canvas.SetTop(CellInput, bounds.Y);
        if (editing is not null)
        {
            using var text = new TextLayout(State.EditSession!.Text, new Typeface(CellInput.FontFamily), CellInput.FontSize,
                Brushes.Black, textWrapping: TextWrapping.NoWrap, lineHeight: 22);
            editing.EditorWidth = Math.Max(editing.EditorWidth, bounds.Width);
            editing.EditorHeight = Math.Max(editing.EditorHeight, bounds.Height);
            var width = Math.Ceiling(text.WidthIncludingTrailingWhitespace);
            if (width > editing.EditorWidth - 5) editing.EditorWidth = width + 10;
            editing.EditorHeight = Math.Max(editing.EditorHeight, Math.Ceiling(text.Height));
            // worksheetCanvas draws the measured editor inside the cell outline (3px inset).
            CellInput.Width = Math.Max(1, editing.EditorWidth - 3);
            CellInput.Height = Math.Max(1, editing.EditorHeight - 3);
        }
        else { CellInput.Width = bounds.Width; CellInput.Height = bounds.Height; }
        var palette = SheetPalette.For(ActualThemeVariant);
        Canvas.SetLeft(_cellEditorFrame, bounds.X - 2); Canvas.SetTop(_cellEditorFrame, bounds.Y - 2);
        _cellEditorFrame.Width = CellInput.Width + 4; _cellEditorFrame.Height = CellInput.Height + 4;
        _cellEditorFrame.Background = palette.Background; _cellEditorFrame.BorderBrush = palette.Token("sheet-outline-color");
        _cellEditorFrame.BoxShadow = BoxShadows.Parse("0 0 0 3 " + palette.Token("sheet-outline-editing-color"));
        CellInput.Background = editing is null ? Brushes.Transparent : palette.Background;
        // Fluent's focused TextBox template can paint its own background. Keep the idle input
        // target focused for text/IME without covering the canvas cell underneath it.
        CellInput.Opacity = editing is null ? 0 : 1;
        CellInput.Foreground = editing is null ? Brushes.Transparent : palette.DefaultText;
        CellInput.CaretBrush = editing is null ? Brushes.Transparent : palette.DefaultText;
        CellInput.BorderThickness = editing is null ? new Thickness(0) : new Thickness(1.5);
        CellInput.IsHitTestVisible = editing is not null;
    }

    public void FocusCell() { if (CellInput.IsVisible) CellInput.Focus(); }
    internal void Report(string message) => Error?.Invoke(this, message);
    internal void Report(LocalizedText message)
    {
        Error?.Invoke(this, message.Value);
        LocalizedError?.Invoke(this, message);
    }
    internal void Run(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is IronCalcException or InvalidOperationException or FormatException or ArgumentException) { Report(exception.Message); }
    }
    internal void Command(string operation, object? args = null) => Run(() =>
    {
        if (!CanEdit && !AllowsWorkbookCommand(operation)) return;
        if (!CommitEdit()) return;
        try { Session?.Execute(operation, args); }
        catch (IronCalcException error)
        {
            var title = operation switch
            {
                "insert_columns" => "error_inserting_columns", "insert_rows" => "error_inserting_rows",
                "delete_columns" => "error_deleting_columns", "delete_rows" => "error_deleting_rows",
                "move_columns" => "error_moving_columns", "move_rows" => "error_moving_rows", _ => null
            };
            if (title is not null) ShowWorkbookAlert(Text.Key("error_dialog." + title), Text.Key("error_dialog." + title));
            else if (operation is "autofill_rows" or "autofill_columns") ShowWorkbookAlert(Text.Key("error_dialog.error_autofill"), Text.Key(error.Message.Contains("merged cell", StringComparison.Ordinal) ? "error_dialog.error_autofill_merged" : "error_dialog.error_autofill_array"));
            else if (operation == "clear") ShowWorkbookAlert(Text.Key("error_dialog.error_deleting_cells"), Text.Compose(_ => error.Message));
            else throw;
            return;
        }
        FocusCell();
    });
    public void Undo() => Command("undo");
    public void Redo() => Command("redo");

    public void GoTo(string address)
    {
        Run(() =>
        {
            if (!CommitEdit()) return;
            TryNavigateAddressOrName(address);
        });
    }

    internal void ScrollBy(int rows, int columns)
    {
        if (Snapshot is not { } snapshot) return;
        ScrollTo(snapshot.View.TopRow + rows, snapshot.View.LeftColumn + columns);
    }
    private void ScrollTo(int row, int column)
    {
        if (Snapshot is not { } snapshot) return;
        Run(() => Session?.Execute("scroll", new { row = Math.Clamp(row, Math.Min(snapshot.FrozenRows + 1, CellAddress.LastRow), CellAddress.LastRow), column = Math.Clamp(column, Math.Min(snapshot.FrozenColumns + 1, CellAddress.LastColumn), CellAddress.LastColumn) }));
    }

    internal void SelectAt(int row, int column, bool extend, bool preserveRange)
    {
        if (Session is null || Snapshot is null) return;
        if (State.Editing is not null && TryStartReference(row, column)) return;
        if (!CommitEdit()) return;
        var area = CellArea.FromView(Snapshot.View);
        if (preserveRange && row >= area.Row && row < area.Row + area.Height && column >= area.Column && column < area.Column + area.Width) return;
        Run(() =>
        {
            if (row == 0 && column == 0) { Session.SetSelectedCell(1, 1); Session.SetSelectedRange(1, 1, CellAddress.LastRow, CellAddress.LastColumn); }
            else if (row == 0)
            {
                var first = extend ? Math.Min(area.Column, column) : column;
                var last = extend ? Math.Max(area.Column + area.Width - 1, column) : column;
                Session.SetSelectedCell(1, first); Session.SetSelectedRange(1, first, CellAddress.LastRow, last);
            }
            else if (column == 0)
            {
                var first = extend ? Math.Min(area.Row, row) : row;
                var last = extend ? Math.Max(area.Row + area.Height - 1, row) : row;
                Session.SetSelectedCell(first, 1); Session.SetSelectedRange(first, 1, last, CellAddress.LastColumn);
            }
            else if (extend) Session.Execute("select_area", new { row, column });
            else Session.SetSelectedCell(row, column);
            ApplyFormatPainter();
        });
    }

    internal void ExtendSelection(int row, int column)
    {
        if (State.EditSession?.IsSelectingReference == true) { UpdateReference(row, column); return; }
        Run(() => Session?.Execute("select_area", new { row, column }));
    }
    internal void ExtendHeaderSelection(int anchor, int target, bool rows)
    {
        if (Snapshot is null || Session is null) return;
        // The press anchor is independent of the active cell, which follows the
        // leading edge of the range even when dragging back across the anchor.
        var first = Math.Min(anchor, target);
        var last = Math.Max(anchor, target);
        Run(() =>
        {
            if (rows) { Session.SetSelectedCell(first, 1); Session.SetSelectedRange(first, 1, last, CellAddress.LastColumn); }
            else { Session.SetSelectedCell(1, first); Session.SetSelectedRange(1, first, CellAddress.LastRow, last); }
        });
    }
    internal void ResizeAxis(bool row, int index, double size) => Command(row ? "row_height" : "column_width",
        row ? new { row = index, end = index, height = size } : (object)new { column = index, end = index, width = size });

    internal void CommitFill(Func<bool> ownsGesture)
    {
        var plan = State.FillPlan;
        State.FillPlan = null;
        if (plan is null || !CanEdit || Session is not { } session || Snapshot is null ||
            CellArea.FromView(Snapshot.View) != plan.Source) return;
        var sheetId = Snapshot.Worksheets[plan.Source.Sheet].SheetId;
        bool CanComplete()
        {
            if (!CanEdit || !ReferenceEquals(Session, session) || !ownsGesture()) return false;
            // A nested Changed callback can switch sheets while Refresh is still busy.
            var current = session.Snapshot();
            return current.View.Sheet == plan.Source.Sheet && current.Worksheets[current.View.Sheet].SheetId == sheetId;
        }
        Run(() =>
        {
            if (!CommitEdit() || !CanComplete()) return;
            try { session.Execute(plan.ByRows ? "autofill_rows" : "autofill_columns", new { area = plan.Source, to = plan.To }); }
            catch (IronCalcException error)
            {
                ShowWorkbookAlert(Text.Key("error_dialog.error_autofill"), Text.Key(error.Message.Contains("merged cell", StringComparison.Ordinal)
                    ? "error_dialog.error_autofill_merged" : "error_dialog.error_autofill_array"));
                return;
            }
            // Execute raises Changed synchronously. The host may replace/dispose the
            // session, change sheets, detach the view or cancel capture in that callback.
            if (!CanComplete()) return;
            var selection = plan.Selection;
            session.SetSelectedCell(selection.Row, selection.Column);
            if (!CanComplete()) return;
            session.SetSelectedRange(selection.Row, selection.Column,
                selection.Row + selection.Height - 1, selection.Column + selection.Width - 1);
        });
    }

    private bool TrySheetCommand(string operation, object? args)
    {
        if (!CanEdit || Session is null || !CommitEdit()) return false;
        try
        {
            Session.Execute(operation, args);
            FocusCell();
            return true;
        }
        catch (Exception exception) when (exception is IronCalcException or InvalidOperationException or FormatException or ArgumentException)
        {
            // Workbook.tsx reports rename failures with alert(`${e}`), not a host banner.
            if (operation == "rename_sheet") ShowWorkbookAlert(Text.Key("sheet_tab.rename"), Text.Compose(_ => $"Error: {exception.Message}"));
            else Report(exception.Message);
            return false;
        }
    }

    internal static Button Button(string label, Action action, string? tooltip = null)
    {
        var button = new WorkbookButton { Content = label, Classes = { "workbook-flat" } };
        button.Click += (_, _) => action();
        if (tooltip is not null) ToolTip.SetTip(button, tooltip);
        global::Avalonia.Automation.AutomationProperties.SetName(button, tooltip ?? label);
        return button;
    }
    internal static MenuItem MenuItem(string label, Action action)
    {
        var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); return item;
    }
    internal static MenuItem Submenu(string label, params Control[] items) => new() { Header = label, ItemsSource = items };
    internal static TextBlock Label(string text) => new() { Text = text, FontSize = 12, Margin = new Thickness(0), TextWrapping = TextWrapping.Wrap };
}
