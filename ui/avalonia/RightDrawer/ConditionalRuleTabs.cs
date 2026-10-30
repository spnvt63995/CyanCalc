using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CyanCalc.Controls;

// edit-rule.css keeps the tabs in the scroll flow, then pins them at top: 0.
// A render translation preserves that flow and the same tab/input instances.
internal sealed class ConditionalRuleTabs : TabControl
{
    private readonly TranslateTransform _translation = new();
    private ScrollViewer? _scroll;
    private ScrollContentPresenter? _viewport;
    protected override Type StyleKeyOverride => typeof(TabControl);

    internal ConditionalRuleTabs()
    {
        RenderTransform = _translation;
        ZIndex = 1;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroll = this.FindAncestorOfType<ScrollViewer>();
        _viewport = this.FindAncestorOfType<ScrollContentPresenter>();
        if (_scroll is not null) _scroll.ScrollChanged += Scrolled;
        LayoutUpdated += UpdatePosition;
        UpdatePosition(this, EventArgs.Empty);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LayoutUpdated -= UpdatePosition;
        if (_scroll is not null) _scroll.ScrollChanged -= Scrolled;
        _scroll = null;
        _viewport = null;
        _translation.Y = 0;
        base.OnDetachedFromVisualTree(e);
    }

    private void Scrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, _scroll)) UpdatePosition(sender, e);
    }

    private void UpdatePosition(object? sender, EventArgs e)
    {
        if (_viewport is null || this.GetVisualParent() is not { } parent ||
            parent.TranslatePoint(Bounds.TopLeft, _viewport) is not { } origin ||
            parent.TranslatePoint(new Point(0, parent.Bounds.Height), _viewport) is not { } end) return;

        // Use the parent's coordinates so the existing translation cannot feed
        // back into the calculation; stay within the editor's containing block.
        _translation.Y = Math.Clamp(-origin.Y, 0, Math.Max(0, end.Y - origin.Y - Bounds.Height));
    }
}
