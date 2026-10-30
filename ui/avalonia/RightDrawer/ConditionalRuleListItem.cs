using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CyanCalc.Controls;

// ConditionalFormatting.tsx list rows have a compact sample, unlike EditRule's preview.
internal sealed class ConditionalRuleListItem : Border
{
    protected override Type StyleKeyOverride => typeof(Border);
    private readonly Grid _columns = new() { ColumnDefinitions = new("16,40,*,88"), ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center, Height = 52 };
    private readonly Grid _order = new() { RowDefinitions = new("16,16,16"), RowSpacing = 2 };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _selection = new() { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    private readonly Action _select;
    private readonly List<Button> _orderButtons = new();
    private Control? _pressed;
    private bool _selected;
    internal ConditionalRuleListItem(int priority, LocalizedText title, string range, Control preview, Action select)
    {
        _select = select; Name = "ConditionalRuleRow"; Classes.Add("conditional-rule-row");
        Height = 68; BorderThickness = new Thickness(0, 0, 0, 1); Focusable = true; Cursor = new Cursor(StandardCursorType.Hand);
        this.Bind(BorderBrushProperty, this.GetResourceObservable("Dialog-grey-200"));
        _selection.Bind(BackgroundProperty, _selection.GetResourceObservable("Dialog-primary-main"));
        Child = new Grid { Children = { _selection, _columns } };
        var number = new TextBlock { Name = "ConditionalPriority", Text = priority.ToString(CultureInfo.InvariantCulture), FontSize = 12, LineHeight = 18,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        number.Bind(TextBlock.ForegroundProperty, number.GetResourceObservable("Dialog-grey-600"));
        Grid.SetRow(number, 1); _order.Children.Add(number); _columns.Children.Add(_order);
        preview.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(preview, 1); _columns.Children.Add(preview);
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        var description = title.Block(); description.Name = "ConditionalRuleTitle"; description.FontWeight = FontWeight.SemiBold;
        description.Bind(TextBlock.ForegroundProperty, description.GetResourceObservable("Dialog-common-black"));
        var address = new TextBlock { Name = "ConditionalRuleRange", Text = string.IsNullOrEmpty(range) ? "—" : range };
        address.Bind(TextBlock.ForegroundProperty, address.GetResourceObservable("Dialog-grey-600"));
        foreach (var label in new[] { description, address })
        {
            label.FontSize = 12; label.LineHeight = 18; label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(label);
        }
        Grid.SetColumn(text, 2); _columns.Children.Add(text);
        Grid.SetColumn(_actions, 3); _columns.Children.Add(_actions);
        title.Bind(this, global::Avalonia.Automation.AutomationProperties.NameProperty);
        PropertyChanged += (_, e) => { if (e.Property == IsPointerOverProperty || e.Property == IsKeyboardFocusWithinProperty) UpdateActions(); };
        // Like the web's mousedown preventDefault, defer focus changes to the action.
        // Keep keyboard navigation, and activate only the control released inside.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _pressed = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault() ?? (Control)this;
            e.Pointer.Capture(this); e.Handled = true;
        }, RoutingStrategies.Tunnel);
        PointerReleased += (_, e) =>
        {
            var pressed = _pressed; _pressed = null; e.Pointer.Capture(null); e.Handled = true;
            if (e.InitialPressMouseButton != MouseButton.Left || pressed is null || !pressed.IsEffectivelyEnabled
                || !new Rect(pressed.Bounds.Size).Contains(e.GetPosition(pressed))) return;
            if (pressed is Button button) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else _select();
        };
        PointerCaptureLost += (_, _) => _pressed = null;
        KeyDown += (_, e) =>
        {
            if (ReferenceEquals(e.Source, this) && e.Key is Key.Enter or Key.Space) { _select(); e.Handled = true; }
        };
        SetSelected(false);
    }
    internal void SetSelected(bool selected)
    {
        _selected = selected; _selection.IsVisible = selected;
        _columns.Margin = new Thickness(selected ? 19 : 11, 0, 12, 0); UpdateActions();
    }
    internal void AddAction(LocalizedText label, string icon, Action action, bool enabled, bool order)
    {
        var button = label.Describe(new WorkbookButton { Content = new WorkbookIcon(icon, 16), Width = order ? 16 : 28, Height = order ? 16 : 28,
            MinWidth = 0, MinHeight = 0, Padding = default, IsEnabled = enabled, Classes = { "workbook-flat", "conditional-rule-action" } });
        button.Click += (_, e) => { action(); e.Handled = true; };
        if (order)
        {
            button.Classes.Add("conditional-order"); Grid.SetRow(button, _orderButtons.Count == 0 ? 0 : 2);
            _orderButtons.Add(button); _order.Children.Add(button);
        }
        else _actions.Children.Add(button);
        UpdateActions();
    }
    private void UpdateActions()
    {
        var visible = _selected || IsPointerOver || IsKeyboardFocusWithin;
        foreach (var control in _orderButtons.Cast<Control>().Append(_actions))
        { control.Opacity = visible ? 1 : 0; control.IsHitTestVisible = visible; }
    }
}

internal sealed class ConditionalRuleListPreview : Grid
{
    internal ConditionalRuleListPreview(JsonObject rule, UserModel model)
    {
        Name = "ConditionalListPreview"; Width = 40; Height = 36; IsHitTestVisible = false;
        string Resolve(JsonNode? value) => model.ResolveColor(JsonSerializer.SerializeToElement(value));
        var type = rule["type"]!.GetValue<string>();
        var background = new Border { CornerRadius = new CornerRadius(4) };
        if (type is "IconSet" or "IconRating") background.Bind(Border.BackgroundProperty, background.GetResourceObservable("Dialog-common-white"));
        else if (rule["format"]?["fill"]?["color"] is { } fill) background.Background = ConditionalRenderer.Brush(Resolve(fill));
        Children.Add(background);
        if (type is "IconSet" or "IconRating")
        {
            string icon, color;
            if (type == "IconSet")
            {
                var thresholds = rule["thresholds"]!.AsArray();
                var preset = ConditionalPreset.All.FirstOrDefault(p => p.Type == "IconSet" && p.Icons.Reverse().SequenceEqual(thresholds.Select(t => t!["icon"]!.GetValue<string>())));
                var fallback = thresholds.Count switch { 2 => "ind-3-checkx", 4 => "dir-4-arrows-color", 5 => "dir-5-arrows-color", _ => "dir-3-arrows-color" };
                preset ??= ConditionalPreset.All.Single(p => p.Id == fallback);
                icon = preset.Icons[0]; color = preset.Colors[0];
            }
            else
            {
                icon = rule["icon"]!.GetValue<string>();
                icon = icon == "Triangle" ? "TriangleUpFilled" : PanelIconButton.Icons.Contains(icon) ? icon : "Cross";
                color = Resolve(rule["color"]);
            }
            Children.Add(new SampleIcon(icon, color) { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }
        else if (type is "ColorScale" or "DataBar") Children.Add(new SampleChart((JsonObject)rule.DeepClone(), Resolve));
        else
        {
            var font = rule["format"]?["font"];
            var sample = new TextBlock { Text = "Aa", FontSize = 12, FontWeight = font?["b"]?.GetValue<bool>() == true ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = font?["i"]?.GetValue<bool>() == true ? FontStyle.Italic : FontStyle.Normal,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var color = Resolve(font?["color"]);
            if (string.IsNullOrEmpty(color)) sample.Bind(TextBlock.ForegroundProperty, sample.GetResourceObservable("Dialog-sheet-default-text-color"));
            else sample.Foreground = ConditionalRenderer.Brush(color);
            sample.TextDecorations = new TextDecorationCollection();
            if (font?["u"]?.GetValue<bool>() == true) sample.TextDecorations.Add(TextDecorations.Underline[0]);
            if (font?["strike"]?.GetValue<bool>() == true) sample.TextDecorations.Add(TextDecorations.Strikethrough[0]);
            Children.Add(sample);
        }
        var frame = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Opacity = .12 };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Dialog-common-black")); Children.Add(frame);
    }
    private sealed class SampleIcon(string icon, string color) : Control
    {
        public override void Render(DrawingContext context) => ConditionalRenderer.Icon(context, icon, color, default, 16, uiPreview: true);
    }
    private sealed class SampleChart(JsonObject rule, Func<JsonNode?, string> resolve) : Control
    {
        public override void Render(DrawingContext context)
        {
            using var clip = context.PushClip(new RoundedRect(new Rect(Bounds.Size), 4));
            if (rule["type"]!.GetValue<string>() == "ColorScale")
            {
                var stops = rule["thresholds"]!.AsArray();
                for (var i = 0; i < 5; i++)
                {
                    var p = i / 4.0 * (stops.Count - 1); var low = (int)p; var high = Math.Min(low + 1, stops.Count - 1); var t = p - low;
                    var a = Color.Parse(resolve(stops[low]!["color"])); var b = Color.Parse(resolve(stops[high]!["color"]));
                    byte Mix(byte x, byte y) => (byte)Math.Floor(x + (y - x) * t + .5);
                    context.FillRectangle(new SolidColorBrush(Color.FromRgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B))), new Rect(0, i * Bounds.Height / 5, Bounds.Width, Bounds.Height / 5));
                }
            }
            else
            {
                var color = Color.Parse(resolve(rule["positive_color"]));
                IBrush brush = new SolidColorBrush(color);
                if (rule["is_gradient"]!.GetValue<bool>()) brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = new GradientStops { new(color, 0), new(Color.FromArgb(26, color.R, color.G, color.B), 1) } };
                var heights = (Bounds.Height - 5) / 4; var widths = new[] { .6, .85, .35, .55 };
                using var innerClip = context.PushClip(new RoundedRect(new Rect(1, 1, Bounds.Width - 2, Bounds.Height - 2), 3));
                for (var i = 0; i < 4; i++)
                {
                    var y = 1 + i * (heights + 1);
                    if (i > 0 && this.TryFindResource("Dialog-grey-200", out var separator) && separator is IBrush separatorBrush)
                        context.FillRectangle(separatorBrush, new Rect(1, y - 1, Bounds.Width - 2, 1));
                    context.FillRectangle(brush, new Rect(1, y, (Bounds.Width - 2) * widths[i], heights));
                }
            }
        }
    }
}
