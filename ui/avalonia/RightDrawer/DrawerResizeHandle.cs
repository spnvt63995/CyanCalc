using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyanCalc.Controls.RightDrawer;

/// <summary>The desktop adapter for the pinned IronCalc RightDrawer resize contract.</summary>
internal sealed class DrawerResizeHandle : Control
{
    // ironcalc/IronCalc v0.9.0,
    // webapp/IronCalc/src/components/RightDrawer/RightDrawer.tsx.
    // Upstream values are content widths, excluding the drawer's left border.
    internal const double DefaultWidth = 360;
    internal const double MinimumWidth = 300;
    internal const double MaximumWidth = 500;
    private const double KeyboardStep = 16;
    private readonly Border _drawer;
    private readonly Control _viewport;
    private readonly Func<IBrush> _accentBrush;
    private IPointer? _pointer;
    private WindowBase? _window;

    internal DrawerResizeHandle(Border drawer, Control viewport, Func<IBrush> accentBrush, WorkbookText? text = null)
    {
        _drawer = drawer;
        _viewport = viewport;
        _accentBrush = accentBrush;
        Name = "DrawerResizeHandle";
        Width = 3;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Stretch;
        Focusable = IsTabStop = true;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        text ??= new WorkbookText();
        text.Key("right_drawer.resize_drawer").Describe(this);
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Separator);
        // Avalonia 11's cross-platform peer has no orientation property.
        text.Key("native.resize_help").Bind(this, AutomationProperties.HelpTextProperty);
        // The ancestor's tunnel runs before Avalonia's target-level pointer-focus
        // handler. This is the native equivalent of the web mousedown preventDefault.
        viewport.AddHandler(PointerPressedEvent, StartResize, RoutingStrategies.Tunnel);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var active = _pointer is not null;
        using (context.PushOpacity(active ? 1 : 0.5))
            context.FillRectangle(active || IsPointerOver || IsFocused ? _accentBrush() : Brushes.Transparent,
                new Rect(Bounds.Size));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var contentWidth = _drawer.Width - _drawer.BorderThickness.Left;
        var width = e.Key switch
        {
            Key.Left => contentWidth + KeyboardStep,
            Key.Right => contentWidth - KeyboardStep,
            Key.Home => MinimumWidth,
            Key.End => MaximumWidth,
            _ => double.NaN
        };
        if (!double.IsNaN(width))
        {
            SetDrawerWidth(width);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void StartResize(object? sender, PointerPressedEventArgs e)
    {
        // The original onMouseDown handler does not filter the mouse button.
        if (ReferenceEquals(e.Source, this) && _pointer is null && e.Pointer.Type == PointerType.Mouse)
        {
            _pointer = e.Pointer;
            e.Pointer.Capture(this);
            InvalidateVisual();
            // Like preventDefault in the web drawer, a resize press must not select text
            // or move focus away from the editor/drawer control that currently owns it.
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (ReferenceEquals(_pointer, e.Pointer))
        {
            // RightDrawer.tsx uses window.innerWidth - clientX, including when
            // the workbook is offset or wider than the viewport.
            var viewport = TopLevel.GetTopLevel(this);
            SetDrawerWidth(viewport is null ? _viewport.Bounds.Width - e.GetPosition(_viewport).X
                : viewport.ClientSize.Width - e.GetPosition(viewport).X);
            e.Handled = true;
        }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (ReferenceEquals(_pointer, e.Pointer))
        {
            StopResize();
            e.Handled = true;
        }
        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _pointer = null;
        InvalidateVisual();
        base.OnPointerCaptureLost(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = e.Root as WindowBase;
        if (_window is not null) _window.Deactivated += WindowDeactivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null) _window.Deactivated -= WindowDeactivated;
        _window = null;
        StopResize();
        base.OnDetachedFromVisualTree(e);
    }

    private void WindowDeactivated(object? sender, EventArgs e) => StopResize();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == IsVisibleProperty && !IsVisible) ||
            (change.Property == IsEnabledProperty && !IsEnabled)) StopResize();
        if (change.Property == IsPointerOverProperty || change.Property == IsFocusedProperty) InvalidateVisual();
    }

    private void SetDrawerWidth(double width)
    {
        if (double.IsFinite(width)) _drawer.Width = Math.Clamp(width, MinimumWidth, MaximumWidth) + _drawer.BorderThickness.Left;
    }

    private void StopResize()
    {
        var pointer = _pointer;
        _pointer = null;
        if (ReferenceEquals(pointer?.Captured, this)) pointer.Capture(null);
        InvalidateVisual();
    }
}
