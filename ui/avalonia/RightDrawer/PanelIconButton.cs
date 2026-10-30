using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace CyanCalc.Controls;

// The names/order (including Triangle) are the pinned IconPicker CYCLE_ICONS.
internal sealed class PanelIconButton : Button
{
    protected override global::Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => MacOSButtonAutomationPeer.Create(this);
    internal static readonly string[] Icons = ["ArrowUp", "ArrowAngleUp", "ArrowRight", "ArrowAngleDown", "ArrowDown", "TriangleUp", "FlatRectangle", "TriangleDown", "Check", "Exclamation", "Cross", "ThumbsUp", "ThumbsDown", "Circle", "Rhombus", "Triangle", "Flag", "Star", "Heart"];
    protected override Type StyleKeyOverride => typeof(Button);
    internal string Value { get; private set; }
    private string _color;
    internal string Color { get => _color; set { _color = value; Refresh(); } }
    internal event Action? Changed;
    internal PanelIconButton(WorkbookView? owner, string value, string color)
    {
        Value = value; _color = color; Width = Height = 32; Padding = new Thickness(6); CornerRadius = new CornerRadius(6); BorderThickness = new Thickness(1); Classes.Add("workbook-flat"); Classes.Add("panel-swatch"); AutomationProperties.SetName(this, "Pick icon"); Refresh();
        Click += (_, _) => owner?.ShowPanelIcons(this, Value, Color, icon => { Value = icon; Refresh(); Changed?.Invoke(); });
    }
    private void Refresh() => Content = new IconSample(Value, Color);
    internal void SetValue(string value) { Value = value; Refresh(); }
    internal sealed class IconSample(string name, string color) : Control
    {
        public IconSample() : this("Cross", "#000000") { }
        protected override Size MeasureOverride(Size availableSize) => new(18, 18);
        public override void Render(DrawingContext context) => ConditionalRenderer.Icon(context, name == "Triangle" ? "TriangleUpFilled" : Icons.Contains(name) ? name : "Cross", color, new Point(1, 1), 16);
    }
}

public sealed partial class WorkbookView
{
    internal void ShowPanelIcons(Control anchor, string selected, string color, Action<string> choose)
    {
        var panel = new Grid { Classes = { "icon-picker" }, ColumnDefinitions = new("28,28,28,28,28,28"), RowDefinitions = new("28,28,28,28"), ColumnSpacing = 2, RowSpacing = 2, Margin = new Thickness(6) };
        foreach (var name in PanelIconButton.Icons)
        {
            var button = new WorkbookButton { Content = new PanelIconButton.IconSample(name, color), Width = 28, Height = 28, Classes = { "picker-chip", "icon-picker-item" } };
            if (name == selected) button.Classes.Add("selected");
            Grid.SetColumn(button, panel.Children.Count % 6); Grid.SetRow(button, panel.Children.Count / 6);
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) => { choose(name); CloseToolbarPopups(); anchor.Focus(); };
            panel.Children.Add(button);
        }
        ShowToolbarFlyout(anchor, panel);
    }
}
