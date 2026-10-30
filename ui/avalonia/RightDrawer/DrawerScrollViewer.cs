using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace CyanCalc.Controls;

internal sealed class DrawerScrollViewer : ScrollViewer
{
    private OverlayScrollBarVisibility? _visibility;
    private bool _attached;
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    internal DrawerScrollViewer()
    {
        Classes.Add("workbook-drawer-scroll");
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        AllowAutoHide = false;
        ScrollChanged += (_, e) => { if (ReferenceEquals(e.Source, this) && e.OffsetDelta.Y != 0) _visibility?.Show(); };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        MacScrollerPreferences.Changed += PreferenceChanged;
        PreferenceChanged();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        MacScrollerPreferences.Changed -= PreferenceChanged;
        ResetBar();
        base.OnDetachedFromVisualTree(e);
    }

    private void PreferenceChanged() { AllowAutoHide = MacScrollerPreferences.UsesOverlay; ResetBar(); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _visibility?.Dispose();
        base.OnApplyTemplate(e);
        var bar = e.NameScope.Find<ScrollBar>("PART_VerticalScrollBar");
        _visibility = bar is null ? null : new OverlayScrollBarVisibility(bar);
        ResetBar();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AllowAutoHideProperty) ResetBar();
    }

    private void ResetBar() => _visibility?.Reset(_attached, AllowAutoHide);
}
