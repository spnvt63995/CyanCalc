using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CyanCalc.Controls;

// A real TextBox remains the input target even before editing, so IME gets a native client.
public sealed class CellEditor : TextBox
{
    private TextPresenter? _presenter;
    private IPointer? _selectAllDragPointer;
    private PointerEventArgs? _selectionMove;
    private readonly DispatcherTimer _selectionScrollTimer;
    internal Func<PointerEventArgs, bool>? ScrollSelection { get; set; }
    private FormulaColorAdorner? _colors;
    private FormulaReference[] _references = [];
    public FormulaReference[] References
    {
        get => _references;
        set { _references = value; if (_colors is not null) { _colors.References = value; _colors.InvalidateVisual(); } }
    }
    protected override Type StyleKeyOverride => typeof(TextBox);
    public bool IsComposing => !string.IsNullOrEmpty(_presenter?.PreeditText);
    public event EventHandler? CompositionStarted;

    public CellEditor()
    {
        _selectionScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _selectionScrollTimer.Tick += (_, _) =>
        {
            if (_selectionMove is not { } move || !CanScrollSelection(move))
            { StopSelectionScroll(); return; }
            if (ScrollSelection?.Invoke(move) != true) return;
            // The stored event retains the pointer's window position. Re-evaluate it
            // against the moved presenter, preserving TextBox's character/word anchor.
            UpdateLayout();
            base.OnPointerMoved(move);
        };
        LostFocus += (_, _) => StopSelectionScroll();
        DetachedFromVisualTree += (_, _) => StopSelectionScroll();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        // TextBox clears PreeditText during base.OnLostFocus. Capture and finish it
        // first, while this editor still owns the native input client and cursor.
        FinishComposition(true);
        base.OnLostFocus(e);
    }

    internal void FinishComposition(bool commit)
    {
        if (_presenter?.PreeditText is not { Length: > 0 } preedit || !MacTextComposition.Unmark(this)) return;
        if (commit)
            RaiseEvent(new TextInputEventArgs { RoutedEvent = TextInputEvent, Text = preedit });
    }

    internal void StopSelectionScroll()
    {
        _selectionScrollTimer.Stop();
        _selectionMove = null;
    }

    internal void ResetUndoHistory()
    {
        if (!IsUndoEnabled) return;
        // Avalonia records programmatic Text assignments too. A reused editor's
        // display/previous draft must not become part of the next edit's history.
        SetCurrentValue(IsUndoEnabledProperty, false);
        SetCurrentValue(IsUndoEnabledProperty, true);
    }

    private bool CanScrollSelection(PointerEventArgs e) =>
        ScrollSelection is not null && IsFocused && IsVisible && IsEffectivelyEnabled &&
        IsHitTestVisible && !IsReadOnly && !IsComposing && _presenter is not null &&
        e.Pointer.Type == PointerType.Mouse && e.Pointer != _selectAllDragPointer &&
        e.Pointer.Captured == _presenter && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        StopSelectionScroll();
        _selectAllDragPointer = null;
        base.OnPointerPressed(e);
        if (e.ClickCount == 3 && e.Pointer.Type == PointerType.Mouse &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            _presenter is not null && e.Pointer.Captured == _presenter &&
            SelectionStart == 0 && SelectionEnd == Text?.Length)
            _selectAllDragPointer = e.Pointer;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        // TextBox 11.3.20 selects all on the third press, then shrinks that selection
        // character by character on move. Preserve the third press's whole-text selection
        // until this held press ends; ordinary character and word drags stay in TextBox.
        if (e.Pointer == _selectAllDragPointer && e.Pointer.Captured == _presenter &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        base.OnPointerMoved(e);
        if (CanScrollSelection(e))
        {
            _selectionMove = e;
            if (!_selectionScrollTimer.IsEnabled) _selectionScrollTimer.Start();
        }
        else StopSelectionScroll();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        StopSelectionScroll();
        _selectAllDragPointer = null;
        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        StopSelectionScroll();
        _selectAllDragPointer = null;
        base.OnPointerCaptureLost(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        StopSelectionScroll();
        _selectAllDragPointer = null;
        if (!e.Handled && !IsComposing && AcceptsReturn && e.Key == Key.Enter &&
            e.KeyModifiers == KeyModifiers.Alt && MacTextComposition.HandleOptionReturn(this))
        { e.Handled = true; return; }
        var input = e;
        if (!e.Handled && !IsComposing && OperatingSystem.IsMacOS() && e.Key is Key.Up or Key.Down &&
            e.KeyModifiers is KeyModifiers.Meta or (KeyModifiers.Meta | KeyModifiers.Shift) &&
            Application.Current?.PlatformSettings?.HotkeyConfiguration is { } shortcuts)
        {
            // Avalonia's macOS map has Cmd+Home/End but omits Cmd+Up/Down.
            // Resolve document navigation together, retaining TextBox's caret,
            // selection and scrolling mechanics and the application's key map.
            var gestures = (e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift)) switch
            {
                (Key.Up, false) => shortcuts.MoveCursorToTheStartOfDocument,
                (Key.Up, true) => shortcuts.MoveCursorToTheStartOfDocumentWithSelection,
                (Key.Down, false) => shortcuts.MoveCursorToTheEndOfDocument,
                _ => shortcuts.MoveCursorToTheEndOfDocumentWithSelection
            };
            if (gestures.FirstOrDefault() is { } gesture)
                input = new KeyEventArgs
                {
                    RoutedEvent = e.RoutedEvent, Source = e.Source,
                    Key = gesture.Key, KeyModifiers = gesture.KeyModifiers
                };
        }
        if (!e.Handled && !IsComposing && SelectionStart != SelectionEnd && _presenter is { } presenter &&
            Application.Current?.PlatformSettings?.HotkeyConfiguration is { } keys &&
            (keys.MoveCursorToTheStartOfLine.Any(gesture => gesture.Matches(input)) ||
             keys.MoveCursorToTheEndOfLine.Any(gesture => gesture.Matches(input)) ||
             keys.MoveCursorToTheStartOfDocument.Any(gesture => gesture.Matches(input)) ||
             keys.MoveCursorToTheEndOfDocument.Any(gesture => gesture.Matches(input))))
        {
            // Avalonia 11.3.20 clears selection at its old anchor when a boundary move
            // leaves the caret unchanged. Collapse at the visible caret first; TextBox
            // still resolves the platform gesture and the actual line/document boundary.
            var caret = presenter.CaretIndex;
            SetCurrentValue(CaretIndexProperty, caret);
            SetCurrentValue(SelectionStartProperty, caret);
            SetCurrentValue(SelectionEndProperty, caret);
        }
        base.OnKeyDown(input);
        e.Handled = input.Handled;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == IsVisibleProperty)
        {
            _selectAllDragPointer = null;
            StopSelectionScroll();
        }
        // The adorner lives outside the editor's visual subtree and does not inherit its visibility.
        if (change.Property == IsVisibleProperty && _colors is not null)
            _colors.IsVisible = IsVisible;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        StopSelectionScroll();
        _selectAllDragPointer = null;
        if (_presenter is not null) { _presenter.PropertyChanged -= PresenterChanged; AdornerLayer.SetAdorner(_presenter, null); }
        base.OnApplyTemplate(e);
        _presenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
        if (_presenter is not null)
        {
            _presenter.PropertyChanged += PresenterChanged;
            _colors = new FormulaColorAdorner(_presenter, this) { References = _references, IsHitTestVisible = false, IsVisible = IsVisible };
            AdornerLayer.SetAdorner(_presenter, _colors);
        }
    }

    private void PresenterChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextPresenter.PreeditTextProperty && IsComposing)
            CompositionStarted?.Invoke(this, EventArgs.Empty);
        _colors?.InvalidateVisual();
    }
}
