using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace CyanCalc.Controls;

// A stationary canvas with the pixel spacer/offset contract of Worksheet.tsx.
internal sealed class WorksheetScrollHost : Grid
{
    private readonly WorksheetCanvas _worksheet;
    private readonly Control _content;
    private readonly ScrollBar _vertical = new() { Name = "WorksheetVerticalScrollBar", Orientation = Orientation.Vertical, Width = 15, SmallChange = 40, Classes = { "worksheet-scroll" } };
    private readonly ScrollBar _horizontal = new() { Name = "WorksheetHorizontalScrollBar", Orientation = Orientation.Horizontal, Height = 15, SmallChange = 40, Classes = { "worksheet-scroll" } };
    private readonly OverlayScrollBarVisibility _verticalVisibility, _horizontalVisibility;
    private bool _syncing, _attached;
    private Vector _lastOffset;

    internal WorksheetScrollHost(WorksheetCanvas worksheet, Control content)
    {
        _worksheet = worksheet; _content = content;
        RowDefinitions = new("*,15"); ColumnDefinitions = new("*,15");
        Children.Add(content);
        SetColumn(_vertical, 1); Children.Add(_vertical);
        SetRow(_horizontal, 1); Children.Add(_horizontal);
        _verticalVisibility = new(_vertical); _horizontalVisibility = new(_horizontal);
        _worksheet.ScrollPositionChanged += (_, _) => Sync();
        foreach (var bar in new[] { _vertical, _horizontal })
        {
            bar.PropertyChanged += (_, e) =>
            {
                if (!_syncing && e.Property == ScrollBar.ValueProperty) _worksheet.SetScrollPosition(new(_horizontal.Value, _vertical.Value));
            };
            bar.AddHandler(PointerWheelChangedEvent, (_, e) =>
            {
                e.Handled = true; _worksheet.ScrollWheel(e.Delta, e.KeyModifiers);
            }, RoutingStrategies.Tunnel);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); _attached = true;
        MacScrollerPreferences.Changed += PreferenceChanged; PreferenceChanged(); Sync();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false; MacScrollerPreferences.Changed -= PreferenceChanged;
        _verticalVisibility.Reset(false, MacScrollerPreferences.UsesOverlay);
        _horizontalVisibility.Reset(false, MacScrollerPreferences.UsesOverlay);
        base.OnDetachedFromVisualTree(e);
    }
    private void PreferenceChanged()
    {
        var overlay = MacScrollerPreferences.UsesOverlay;
        SetRowSpan(_content, overlay ? 2 : 1); SetColumnSpan(_content, overlay ? 2 : 1);
        _verticalVisibility.Reset(_attached, overlay); _horizontalVisibility.Reset(_attached, overlay);
    }
    private void Sync()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            var offset = _worksheet.ScrollOffset; var extent = _worksheet.ScrollExtent; var viewport = _worksheet.Bounds.Size;
            var previous = _lastOffset; _lastOffset = offset;
            Update(_horizontal, extent.Width, viewport.Width, offset.X);
            Update(_vertical, extent.Height, viewport.Height, offset.Y);
            if (previous.X != offset.X) _horizontalVisibility.Show();
            if (previous.Y != offset.Y) _verticalVisibility.Show();
        }
        finally { _syncing = false; }
    }
    private static void Update(ScrollBar bar, double extent, double viewport, double offset)
    {
        bar.Maximum = Math.Max(0, extent - viewport); bar.ViewportSize = viewport;
        // WebKit's page step leaves up to 40px of overlap (at least 80% of a page).
        bar.LargeChange = Math.Max(1, Math.Max(Math.Round(viewport * .8, MidpointRounding.AwayFromZero), Math.Round(viewport - 40, MidpointRounding.AwayFromZero)));
        bar.Value = offset;
    }
}
