using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    private void ShowBorderPicker(Control anchor)
    {
        if (!CanEdit || Session is not { } model || !CommitEdit()) return;
        string? selected = null;
        var color = PickerColor.Rgb("");
        var style = "thin";
        var panel = new StackPanel { Width = 156 };
        var types = new StackPanel { Spacing = 4, Margin = new Thickness(4) };
        panel.Children.Add(types);
        var buttons = new Dictionary<string, ToggleButton>();
        void Apply()
        {
            if (selected is null || !CanEdit || !ReferenceEquals(Session, model) || Snapshot is not { } snapshot) return;
            Command("border", new { area = CellArea.FromView(snapshot.View),
                border = new { type = selected, item = new { style, color = ColorValue(color) } } });
        }
        var choices = new (string Type, string Icon, LocalizedText Label, string? Off)[]
        {
            ("All", "Grid2X2", Text.Key("toolbar.borders.all"), null), ("Inner", "BorderInner", Text.Key("toolbar.borders.inner"), null),
            ("CenterH", "BorderCenterH", Text.Key("toolbar.borders.horizontal"), null), ("CenterV", "BorderCenterV", Text.Key("toolbar.borders.vertical"), null),
            ("Outer", "BorderOuter", Text.Key("toolbar.borders.outer"), "None"), ("None", "BorderNone", Text.Key("toolbar.borders.clear"), "None"),
            ("Top", "BorderTop", Text.Key("toolbar.borders.top"), "None"), ("Right", "BorderRight", Text.Key("toolbar.borders.right"), "None"),
            ("Bottom", "BorderBottom", Text.Key("toolbar.borders.bottom"), "None"), ("Left", "BorderLeft", Text.Key("toolbar.borders.left"), "None")
        };
        for (var row = 0; row < 2; row++)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            foreach (var choice in choices.Skip(row * 5).Take(5))
            {
                ToggleButton? button = null;
                button = ToolbarToggle(choice.Icon, choice.Label, () =>
                {
                    selected = selected == choice.Type ? choice.Off : choice.Type;
                    foreach (var item in buttons) item.Value.IsChecked = item.Key == selected;
                    Apply();
                    // Border commands keep this menu open. Continue navigation
                    // here only if the command left the same menu and document active.
                    if (ReferenceEquals(Session, model) && _attached &&
                        _toolbarFlyout is { IsOpen: true } flyout && ReferenceEquals(flyout.Content, panel) &&
                        button is { IsEffectivelyVisible: true })
                        button.Focus();
                });
                button.Classes.Add("border-choice");
                buttons.Add(choice.Type, button); group.Children.Add(button);
            }
            types.Children.Add(group);
        }
        panel.Children.Add(WorkbookMenus.Divider());
        var options = new StackPanel { Margin = new Thickness(4) }; panel.Children.Add(options);
        var colorButton = PickerMenuButton("PencilLine", Text.Key("toolbar.borders.color"));
        colorButton.Click += (_, _) => ShowNestedFlyout(colorButton, BuildColorPicker(model, "border", color,
            value => { color = value; Apply(); }, () => _nestedToolbarFlyout?.Hide()));
        options.Children.Add(colorButton);
        var styleButton = PickerMenuButton("LineStyle", Text.Key("toolbar.borders.style"));
        styleButton.Click += (_, _) =>
        {
            var styles = new StackPanel { Width = 92, Margin = new Thickness(4) };
            foreach (var value in new[] { "thin", "medium", "thick", "double", "dotted", "mediumdashed", "slantdashdot", "mediumdashdot", "mediumdashdotdot" })
            {
                var button = WorkbookMenus.RowButton(new BorderLinePreview(value) { Height = 16 });
                if (value == style) button.Classes.Add("selected");
                Text.Key("native.border_styles." + value).Describe(button);
                button.Click += (_, _) => { style = value; Apply(); _nestedToolbarFlyout?.Hide(); };
                styles.Children.Add(button);
            }
            styles.KeyDown += (_, e) => { if (e.Key == Key.Escape) { _nestedToolbarFlyout?.Hide(); e.Handled = true; } };
            ShowNestedFlyout(styleButton, styles);
        };
        options.Children.Add(styleButton);
        panel.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_nestedToolbarFlyout?.IsOpen == true) _nestedToolbarFlyout.Hide();
            else { CloseToolbarPopups(); anchor.Focus(); }
            e.Handled = true;
        };
        ShowToolbarFlyout(anchor, panel);
    }

    private static Button PickerMenuButton(string icon, LocalizedText label)
    {
        var content = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 8 };
        var glyph = new WorkbookIcon(icon); glyph.Bind(WorkbookIcon.ForegroundProperty, glyph.GetResourceObservable("Menu-grey-600")); content.Children.Add(glyph);
        var text = label.Block(); Grid.SetColumn(text, 1); content.Children.Add(text);
        var arrow = new WorkbookIcon("ChevronRight"); arrow.Bind(WorkbookIcon.ForegroundProperty, arrow.GetResourceObservable("Menu-grey-600")); Grid.SetColumn(arrow, 2); content.Children.Add(arrow);
        var button = WorkbookMenus.RowButton(content); label.Describe(button); return button;
    }

    internal void ShowPanelLineStyle(Control anchor, string selected, Action<string> choose)
    {
        var list = new StackPanel { Width = 112, Margin = new Thickness(4) };
        foreach (var style in new[] { "thin", "medium", "thick", "double", "dotted", "mediumdashed", "slantdashdot", "mediumdashdot", "mediumdashdotdot" })
        {
            var row = new Grid { ColumnDefinitions = new("16,8,*") };
            row.Children.Add(new WorkbookIcon("Check") { IsVisible = selected == style });
            var preview = new BorderLinePreview(style) { Height = 16 }; Grid.SetColumn(preview, 2); row.Children.Add(preview);
            var button = WorkbookMenus.RowButton(row); Text.Key("native.border_styles." + style).Describe(button);
            button.Click += (_, _) => { choose(style); CloseToolbarPopups(); anchor.Focus(); }; list.Children.Add(button);
        }
        ShowToolbarFlyout(anchor, list);
    }

    internal sealed class BorderLinePreview : Control
    {
        internal IBrush? Stroke { get; init; }
        private readonly string _style;
        internal BorderLinePreview(string style)
        {
            _style = style;
            ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        }
        public override void Render(DrawingContext context)
        {
            // CSS preview lengths are pixels, unlike the engine's pen-width-scaled dashes.
            var brush = Stroke ?? SheetPalette.For(ActualThemeVariant).Token("grey-900");
            var height = _style.StartsWith("medium", StringComparison.Ordinal) ? 2 : _style is "thick" or "double" ? 3 : 1;
            var top = (Bounds.Height - height) / 2;
            if (_style == "double")
            {
                context.FillRectangle(brush, new Rect(0, top, Bounds.Width, 1));
                context.FillRectangle(brush, new Rect(0, top + 2, Bounds.Width, 1)); return;
            }
            double[]? pattern = _style switch
            {
                "dotted" => [1, 1], "mediumdashed" => [6, 4],
                "slantdashdot" or "mediumdashdot" => [4, 2, 1, 2],
                "mediumdashdotdot" => [4, 2, 1, 2, 1, 2], _ => null
            };
            if (pattern is null) { context.FillRectangle(brush, new Rect(0, top, Bounds.Width, height)); return; }
            for (double x = 0; x < Bounds.Width;)
                for (var i = 0; i < pattern.Length && x < Bounds.Width; i++)
                {
                    if (i % 2 == 0) context.FillRectangle(brush, new Rect(x, top, Math.Min(pattern[i], Bounds.Width - x), height));
                    x += pattern[i];
                }
        }
    }
}
