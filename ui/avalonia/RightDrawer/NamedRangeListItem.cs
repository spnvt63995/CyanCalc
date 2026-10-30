using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;

namespace CyanCalc.Controls;

// NamedRanges.tsx: three text lines, with a growing row for wrapped names/formulas.
internal sealed class NamedRangeListItem : Border
{
    protected override Type StyleKeyOverride => typeof(Border);
    private readonly Grid _columns = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _selection = new() { Name = "NamedRangeSelection", Width = 3, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false, Opacity = 0 };
    private Control? _pressed;
    private bool _selected;

    internal NamedRangeListItem(string name, string scope, string formula, Action select)
    {
        Name = "NamedRangeRow"; Classes.Add("named-range-row");
        MinHeight = 76; BorderThickness = new Thickness(0, 0, 0, 1); Focusable = true; Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(this, name);
        this.Bind(BorderBrushProperty, this.GetResourceObservable("Dialog-grey-200"));
        _selection.Bind(BackgroundProperty, _selection.GetResourceObservable("Dialog-primary-main"));
        Child = new Grid { Children = { _selection, _columns } };
        var text = new StackPanel { Spacing = 2, MinWidth = 0 };
        TextBlock Line(string id, string value, string color)
        {
            TextBlock line = id == "NamedRangeScope" ? new TextBlock() : new BreakAllTextBlock();
            line.Name = id; line.Text = value; line.FontSize = 12; line.LineHeight = 18; line.TextWrapping = TextWrapping.Wrap; line.ClipToBounds = true;
            line.Bind(TextBlock.ForegroundProperty, line.GetResourceObservable(color)); text.Children.Add(line); return line;
        }
        var title = Line("NamedRangeTitle", name, "Dialog-common-black"); title.FontWeight = FontWeight.SemiBold; title.MaxHeight = 36;
        var scopeText = Line("NamedRangeScope", scope, "Dialog-common-black"); scopeText.TextWrapping = TextWrapping.NoWrap; scopeText.TextTrimming = TextTrimming.CharacterEllipsis;
        Line("NamedRangeReference", formula, "Dialog-grey-600");
        _columns.Children.Add(text); Grid.SetColumn(_actions, 1); _columns.Children.Add(_actions);
        SetSelected(false);
        var easing = new SplineEasing { X1 = .42, Y1 = 0, X2 = .58, Y2 = 1 };
        _columns.Transitions = new Transitions { new ThicknessTransition { Property = MarginProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = easing } };
        _selection.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = easing } };
        Transitions = new Transitions { new BrushTransition { Property = BackgroundProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = easing } };
        PropertyChanged += (_, e) => { if (e.Property == IsPointerOverProperty) UpdateInset(); };
        // The web list prevents mousedown's focus change, including on edit/delete.
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
            else select();
        };
        PointerCaptureLost += (_, _) => _pressed = null;
        KeyDown += (_, e) =>
        {
            if (ReferenceEquals(e.Source, this) && e.Key is Key.Enter or Key.Space) { select(); e.Handled = true; }
        };
    }

    internal void SetSelected(bool selected) { _selected = selected; _selection.Opacity = selected ? 1 : 0; UpdateInset(); }
    private void UpdateInset() => _columns.Margin = new Thickness(_selected || IsPointerOver ? 23 : 15, 8, 12, 8);

    internal void AddAction(LocalizedText label, string icon, Action action, bool enabled)
    {
        var button = label.Describe(new WorkbookButton { Content = new WorkbookIcon(icon, 16), Width = 28, Height = 28,
            MinWidth = 0, MinHeight = 0, Padding = default, IsEnabled = enabled, Classes = { "workbook-flat" } });
        button.Click += (_, e) => { action(); e.Handled = true; }; _actions.Children.Add(button);
    }
    private sealed class BreakAllTextBlock : TextBlock
    {
        protected override Type StyleKeyOverride => typeof(TextBlock);
        protected override TextLayout CreateTextLayout(string? text)
        {
            // CSS word-break: break-all. Keep the original Text for accessibility;
            // add break opportunities only to layout, without splitting graphemes.
            var characters = StringInfo.GetTextElementEnumerator(text ?? "");
            var elements = new List<string>();
            while (characters.MoveNext()) elements.Add(characters.GetTextElement());
            return base.CreateTextLayout(string.Join("\u200b", elements));
        }
    }

}
