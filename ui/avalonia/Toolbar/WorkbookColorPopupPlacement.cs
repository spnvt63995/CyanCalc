using Avalonia;
using Avalonia.Controls;

namespace CyanCalc.Controls;

internal static class WorkbookColorPopupPlacement
{
    // ColorPicker/useAnchorPosition.ts and AdvancedColorPicker.tsx at the pinned upstream.
    // The advanced picker intentionally uses a different horizontal/vertical fallback.
    internal static Point Position(Rect anchor, Size panel, Size viewport, PlacementMode placement, bool advanced)
    {
        const double offset = 4, margin = 8;
        double left, top;
        if (advanced)
        {
            left = anchor.Left - offset;
            top = anchor.Bottom + offset;
            if (top + panel.Height > viewport.Height - margin) top = anchor.Top - panel.Height - offset;
        }
        else if (placement == PlacementMode.RightEdgeAlignedTop)
        {
            left = anchor.Right;
            top = anchor.Top;
        }
        else
        {
            var below = viewport.Height - anchor.Bottom - margin;
            var above = anchor.Top - margin;
            var openBelow = placement == PlacementMode.TopEdgeAlignedLeft
                ? above < panel.Height + offset && below > above
                : below >= panel.Height + offset || below >= above;
            left = anchor.Left + panel.Width > viewport.Width - margin ? anchor.Right - panel.Width : anchor.Left;
            top = openBelow ? anchor.Bottom + offset : anchor.Top - panel.Height - offset;
        }
        if (left + panel.Width > viewport.Width - margin) left = viewport.Width - panel.Width - margin;
        if (left < margin) left = margin;
        if (!advanced && top + panel.Height > viewport.Height - margin) top = viewport.Height - panel.Height - margin;
        if (top < margin) top = margin;
        return new Point(left, top);
    }
}
