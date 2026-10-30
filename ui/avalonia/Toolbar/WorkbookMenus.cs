using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;

namespace CyanCalc.Controls;

// Menu/menu.css and the picker styles at the pinned upstream. Keep Avalonia's
// MenuItem/Popup input, ownership and accessibility; replace only their presentation.
internal static class WorkbookMenus
{
    internal static ContextMenu Create(IEnumerable<Control>? items = null, bool anchored = false)
    {
        var menu = new ContextMenu { ItemsSource = items };
        Attach(menu);
        if (anchored)
        {
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
            menu.VerticalOffset = 4;
        }
        return menu;
    }

    internal static void Attach(StyledElement root)
    {
        root.Resources["MenuCheckTemplate"] = new FuncControlTemplate<ContentControl>((_, _) => new WorkbookIcon("Check"));
        root.Resources["MenuArrowTemplate"] = new FuncControlTemplate<ContentControl>((_, _) => new WorkbookIcon("ChevronRight"));
        root.Styles.Add(new StyleInclude(new Uri("avares://CyanCalc.Avalonia/"))
            { Source = new Uri("avares://CyanCalc.Avalonia/Toolbar/WorkbookMenuStyles.axaml") });
        void Palette()
        {
            var palette = SheetPalette.For(root.ActualThemeVariant);
            foreach (var token in new[] { "common-black", "grey-100", "grey-200", "grey-300", "grey-400", "grey-600", "grey-900", "primary-main",
                "primary-dark", "primary-contrast-text", "error-main", "surface-elevated", "surface-elevated-border", "input-background", "input-border", "sheet-default-text-color" })
                root.Resources["Menu-" + token] = palette.Token(token);
            var error = ((ISolidColorBrush)palette.Token("error-main")).Color;
            root.Resources["Menu-error-hover"] = new SolidColorBrush(Color.FromArgb(20, error.R, error.G, error.B));
        }
        Palette();
        root.ActualThemeVariantChanged += (_, _) => Palette();
    }

    internal static Separator Divider() => new() { Classes = { "picker-divider" } };

    internal static Button RowButton(Control content)
    {
        var button = new WorkbookButton { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        button.Classes.Add("picker-row"); return button;
    }

    internal static void Secondary(TextBlock text) =>
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Menu-grey-400"));
}

internal sealed class WorkbookPickerFlyout : Flyout
{
    internal void ConstrainColorPicker(TopLevel? viewport)
    {
        if (viewport is null || Content is not Control content || !content.Classes.Contains("color-picker")) return;
        var preferredPlacement = Placement;
        var panelSize = default(Size);
        Placement = PlacementMode.Custom;
        HorizontalOffset = VerticalOffset = 0;
        PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.None;

        Point Offset(Control anchor)
        {
            // A nested border-color anchor belongs to a separate PopupRoot. Convert
            // through screen coordinates only to reach the owning app's client area.
            var origin = anchor.TranslatePoint(default, viewport) ?? viewport.PointToClient(anchor.PointToScreen(default));
            var bounds = new Rect(origin, anchor.Bounds.Size);
            var position = WorkbookColorPopupPlacement.Position(bounds, panelSize, viewport.ClientSize,
                preferredPlacement, content.Width == 240);
            return new Point(position.X - origin.X, position.Y - origin.Y);
        }
        CustomPopupPlacementCallback = placement =>
        {
            panelSize = placement.PopupSize;
            placement.Anchor = PopupAnchor.TopLeft;
            placement.Gravity = PopupGravity.BottomRight;
            placement.ConstraintAdjustment = PopupPositionerConstraintAdjustment.None;
            placement.Offset = Offset((Control)placement.Target);
        };
        void Reposition(object? sender, EventArgs e)
        {
            if (!IsOpen || Target is not { } anchor || panelSize == default) return;
            var offset = Offset(anchor);
            // Flyout properties are copied only when opened. Update the live Popup
            // so scrolling/resizing also repositions an already open color picker.
            Popup.HorizontalOffset = offset.X;
            Popup.VerticalOffset = offset.Y;
        }
        Opened += (_, _) =>
        {
            viewport.LayoutUpdated += Reposition;
            content.LayoutUpdated += Reposition;
        };
        Closed += (_, _) =>
        {
            viewport.LayoutUpdated -= Reposition;
            content.LayoutUpdated -= Reposition;
        };
    }

    protected override Control CreatePresenter()
    {
        var presenter = (FlyoutPresenter)base.CreatePresenter();
        WorkbookMenus.Attach(presenter);
        if (Content is Control icons && icons.Classes.Contains("icon-picker")) presenter.CornerRadius = new CornerRadius(6);
        if (Content is Control content && content.Classes.Contains("color-picker"))
        {
            void Resize()
            {
                presenter.CornerRadius = new CornerRadius(content.Width == 240 ? 12 : 8);
            }
            content.PropertyChanged += (_, e) => { if (e.Property == Control.WidthProperty) Resize(); };
            Resize();
        }
        return presenter;
    }
}

internal sealed class WorkbookMenuFlyout : MenuFlyout
{
    protected override Control CreatePresenter()
    {
        var presenter = base.CreatePresenter();
        WorkbookMenus.Attach(presenter);
        return presenter;
    }
}
