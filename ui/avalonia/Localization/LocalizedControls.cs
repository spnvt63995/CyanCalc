using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    internal static Button Button(LocalizedText label, Action action, LocalizedText? tooltip = null)
    {
        var button = Button("", action);
        label.Bind(button, ContentControl.ContentProperty); (tooltip ?? label).Describe(button); return button;
    }
    internal static Button Button(string literal, Action action, LocalizedText tooltip) => tooltip.Describe(Button(literal, action));
    internal static TextBlock Label(LocalizedText label) => label.Bind(Label(""), TextBlock.TextProperty);
    internal static MenuItem MenuItem(LocalizedText label, Action action)
    {
        var item = MenuItem("", action); label.Bind(item, HeaderedSelectingItemsControl.HeaderProperty);
        label.Bind(item, global::Avalonia.Automation.AutomationProperties.NameProperty); return item;
    }
    internal static TextBox Field(StackPanel panel, LocalizedText label, string initial, string? name = null)
    {
        var field = new StackPanel { Spacing = 4 }; panel.Children.Add(field); field.Children.Add(Label(label));
        var input = label.Describe(new PanelTextBox { Name = name, Text = initial, Classes = { "dialog-input" } });
        field.Children.Add(input); return input;
    }
    internal static ComboBox Choice(StackPanel panel, LocalizedText label, string[] choices, string? selected = null,
        Func<string, LocalizedText>? display = null, string? name = null, bool showLabel = true)
    {
        var field = new StackPanel { Spacing = 4 }; panel.Children.Add(field);
        if (showLabel) field.Children.Add(Label(label));
        var input = label.Describe(new ComboBox { Name = name, ItemsSource = choices, SelectedItem = selected ?? choices.FirstOrDefault(),
            HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "dialog-select" } });
        input.DropDownOpened += (_, _) => input.MaxDropDownHeight = Math.Max(32, (TopLevel.GetTopLevel(input)?.ClientSize.Height ?? 800) - 16);
        if (display is not null) input.ItemTemplate = LocalizedText.Options(display);
        field.Children.Add(input); return input;
    }
    private static Button IconButton(string icon, Action action, LocalizedText name) => name.Describe(IconButton(icon, action, ""));
    private static ToggleButton ToolbarToggle(string icon, LocalizedText name, Action action) => name.Describe(ToolbarToggle(icon, "", action));
    private static MenuItem IconMenuItem(LocalizedText label, string icon, Action action, bool enabled = true)
    {
        var item = MenuItem(label, action); item.Icon = new WorkbookIcon(icon); item.IsEnabled = enabled; return item;
    }
    private static Button MenuButton(string literal, Control[] items, LocalizedText name, string? icon = null, Action? beforeOpen = null) =>
        name.Describe(MenuButton(literal, items, "", icon, beforeOpen));
}
