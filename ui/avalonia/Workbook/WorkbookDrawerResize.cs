using Avalonia;
using Avalonia.Controls;
using CyanCalc.Controls.RightDrawer;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    private void InstallDrawerResize(Grid root)
    {
        // CSS drawer widths describe the content box; Avalonia Width includes its border.
        _drawer.Width = DrawerResizeHandle.DefaultWidth + _drawer.BorderThickness.Left;
        _drawer.ZIndex = 1;
        var handle = new DrawerResizeHandle(_drawer, root, () => Worksheet.AccentBrush, Text)
        {
            IsVisible = _drawer.IsVisible,
            Margin = new Thickness(_drawer.BorderThickness.Left, 0, 0, 0),
            ZIndex = 2
        };
        Grid.SetRow(handle, 1); Grid.SetColumn(handle, 1);
        root.Children.Add(handle);
        var compact = false;
        var wideWidth = _drawer.Width;
        TopLevel? viewport = null;
        void UpdateDrawerLayout()
        {
            // The original media query uses the viewport, even when a host sidebar
            // narrows the workbook. Fall back to local bounds only while unhosted.
            var viewportWidth = viewport?.ClientSize.Width ?? Bounds.Width;
            var nextCompact = viewportWidth is > 0 and <= 600;
            if (compact != nextCompact)
            {
                if (nextCompact) wideWidth = _drawer.Width;
                _drawer.Width = nextCompact ? double.NaN : wideWidth;
                Grid.SetColumn(_drawer, nextCompact ? 0 : 1);
                Grid.SetColumnSpan(_drawer, nextCompact ? 2 : 1);
                Grid.SetRow(_drawer, nextCompact ? 0 : 1);
                Grid.SetRowSpan(_drawer, nextCompact ? 2 : 1);
                _drawer.BorderThickness = nextCompact ? new Thickness(0) : new Thickness(1, 0, 0, 0);
                compact = nextCompact;
            }
            handle.IsVisible = _drawer.IsVisible && !compact;
        }
        void ViewportChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == TopLevel.ClientSizeProperty) UpdateDrawerLayout();
        }
        AttachedToVisualTree += (_, _) =>
        {
            viewport = TopLevel.GetTopLevel(this);
            if (viewport is not null) viewport.PropertyChanged += ViewportChanged;
            UpdateDrawerLayout();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (viewport is not null) viewport.PropertyChanged -= ViewportChanged;
            viewport = null;
        };
        SizeChanged += (_, _) => UpdateDrawerLayout();
        _drawer.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty) UpdateDrawerLayout();
        };
        Worksheet.PaletteChanged += (_, _) => handle.InvalidateVisual();
        UpdateDrawerLayout();
    }
}
