using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CyanCalc.Controls;

internal sealed class PanelColorButton : Button
{
    protected override global::Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => MacOSButtonAutomationPeer.Create(this);
    private readonly UserModel? _model;
    private readonly Border _swatch = new() { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(31, 0, 0, 0)) };
    private readonly bool _textColor;
    private JsonNode? _value;
    internal event Action? Changed;
    internal JsonNode? Value
    {
        get => _value?.DeepClone();
        set
        {
            _value = value?.DeepClone();
            RefreshSwatch();
            Changed?.Invoke();
        }
    }
    private void RefreshSwatch()
    {
        var hex = _model?.ResolveColor(JsonSerializer.SerializeToElement(_value)) ?? (_value is JsonValue ? _value.ToString() : "");
        _swatch.Background = string.IsNullOrEmpty(hex) ? (_textColor ? SheetPalette.For(ActualThemeVariant).DefaultText : Brushes.Transparent) : ConditionalRenderer.Brush(hex);
    }
    internal PanelColorButton(WorkbookView? owner, UserModel? model, LocalizedText label, JsonNode? value, string name, bool defaultToCurrent = false)
    {
        _model = model; _textColor = name is "StyleFontColor" or "StyleBorderColor";
        Name = name; Width = 32; Height = 32; Padding = new Thickness(2); Content = _swatch;
        HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch;
        Classes.Add("panel-swatch"); Classes.Add("workbook-flat");
        BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(6);
        label.Describe(this); Value = value;
        // Attaching/retheming changes presentation, not the user's draft color.
        ActualThemeVariantChanged += (_, _) => RefreshSwatch();
        Click += (_, _) => owner?.ShowPanelColor(this, _model, Value, chosen => Value = chosen, defaultToCurrent);
    }
    internal void UseIcon(string icon)
    {
        Content = null;
        Classes.Remove("panel-swatch"); Width = Height = 28; Padding = new Thickness(0); BorderThickness = new Thickness(0);
        _swatch.Width = 14; _swatch.Height = 3; _swatch.CornerRadius = new CornerRadius(1); _swatch.BorderThickness = new Thickness(0);
        _swatch.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom; _swatch.Margin = new Thickness(0, 0, 0, 2);
        Content = new Grid { Children = { new WorkbookIcon(icon) { HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center }, _swatch } };
    }
}

public sealed partial class WorkbookView
{
    internal void ShowPanelColor(Control anchor, UserModel? model, JsonNode? value, Action<JsonNode?> choose, bool defaultToCurrent)
    {
        if (model is null || !ReferenceEquals(model, Session)) return;
        var selected = ReadPickerColor(JsonSerializer.SerializeToElement(value));
        ShowToolbarFlyout(anchor, BuildColorPicker(model, "panel." + anchor.Name, selected, color =>
        {
            if (ReferenceEquals(model, Session)) choose(JsonSerializer.SerializeToNode(ColorValue(color)));
        }, () => { CloseToolbarPopups(); anchor.Focus(); }, defaultValue: defaultToCurrent ? model.ResolveColor(JsonSerializer.SerializeToElement(value)) : ""));
    }
}
