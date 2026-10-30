using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace CyanCalc.Controls;

// IronCalc ColorScaleRule/IconSetsRule preset lists, in UI order.
// The engine consumes thresholds LOW -> HIGH; the original icon swatches are HIGH -> LOW.
internal sealed record ConditionalPreset(string Id, string Type, string[] Colors, string[] Icons)
{
    internal string DisplayName => Id switch
    {
        "r-y-g" => "Red, yellow, green", "g-y-r" => "Green, yellow, red",
        "r-w-g" => "Red, white, green", "g-w-r" => "Green, white, red",
        "r-w-b" => "Red, white, blue", "b-w-r" => "Blue, white, red",
        "g-w-p" => "Green, white, purple", "p-w-g" => "Purple, white, green",
        "g-lg" => "Green to light green", "lg-g" => "Light green to green",
        "r-lr" => "Red to light red", "lr-r" => "Light red to red",
        "r-y-b" => "Red, yellow, blue", "b-y-r" => "Blue, yellow, red",
        "dp-m-r" => "Purple, magenta, peach", "r-m-dp" => "Peach, magenta, purple",
        "dir-3-arrows-color" => "3 colored arrows", "dir-3-chevrons-color" => "3 colored direction marks",
        "dir-4-arrows-color" => "4 colored arrows", "dir-5-arrows-color" => "5 colored arrows",
        "shapes-3-circles-color" => "3 colored circles", "shapes-4-circles-color" => "4 colored circles",
        "shapes-3-multiple" => "Diamond, triangle, circle", "shapes-4-circles" => "4 circle indicators",
        "ind-2-arrows" => "2 arrow indicators", "ind-2-thumbs" => "Thumbs up and down",
        "ind-3-checkx" => "Check and cross", "ind-2-triangles" => "2 triangle indicators",
        "ind-3-check-exclaim-x" => "Check, exclamation, cross", "ind-4-flags" => "3 colored flags",
        _ => $"{Icons.Length} {(Icons[0] == "Star" ? "stars" : "hearts")}"
    };
    internal static readonly ConditionalPreset[] All = Load();
    private static ConditionalPreset[] Load()
    {
        var assembly = typeof(ConditionalPreset).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(x => x.EndsWith("conditional-presets.json")))!;
        return JsonSerializer.Deserialize<ConditionalPreset[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    internal JsonObject Apply(JsonObject current)
    {
        var result = (JsonObject)current.DeepClone();
        result["type"] = Type;
        var thresholds = new JsonArray();
        if (Type == "ColorScale")
        {
            var previous = current["thresholds"]!.AsArray();
            thresholds.Add(new JsonObject { ["cfvo"] = previous[0]!["cfvo"]!.DeepClone(), ["color"] = Colors[0].ToLowerInvariant() });
            // Original five-color swatches still save three stops: first, middle, last.
            if (Colors.Length > 2) thresholds.Add(new JsonObject
            {
                ["cfvo"] = previous.Count == 3 ? previous[1]!["cfvo"]!.DeepClone() : new JsonObject { ["Percentile"] = 50 },
                ["color"] = Colors[Colors.Length / 2].ToLowerInvariant()
            });
            thresholds.Add(new JsonObject { ["cfvo"] = previous[^1]!["cfvo"]!.DeepClone(), ["color"] = Colors[^1].ToLowerInvariant() });
        }
        else
        {
            var count = Icons.Length;
            for (var i = 0; i < count; i++)
            {
                JsonNode bound = new JsonObject { ["Percent"] = (int)Math.Floor(i * 100.0 / count + 0.5) };
                thresholds.Add(Type == "IconRating"
                    ? new JsonArray(bound, JsonValue.Create(true))
                    : new JsonObject { ["cfvo"] = i == 0 ? JsonValue.Create("Min") : bound,
                        ["icon"] = Icons[count - 1 - i], ["color"] = Colors[count - 1 - i], ["is_strict"] = true });
            }
            if (Type == "IconRating") { result["icon"] = Icons[0]; result["color"] = Colors[0]; }
        }
        result["thresholds"] = thresholds;
        return result;
    }
}

internal sealed class ConditionalPresetTile : Button
{
    protected override global::Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => MacOSButtonAutomationPeer.Create(this);
    protected override Type StyleKeyOverride => typeof(Button);
    internal ConditionalPreset Preset { get; }
    internal ConditionalPresetTile(ConditionalPreset preset, WorkbookText? text = null)
    {
        Preset = preset;
        Name = "Preset_" + preset.Id.Replace('-', '_');
        text ??= new WorkbookText();
        text.Compose(t => WorkbookOptions.RuleType(t, preset.Type).Value + " · " + (preset.Type == "ColorScale"
            ? string.Join(", ", preset.Colors) : string.Join(", ", preset.Icons.Select(icon => t["native.icons." + icon])))).Describe(this);
        Padding = new Thickness(4); Margin = new Thickness(0, 0, 4, 4);
        if (preset.Type is "IconSet" or "IconRating")
        {
            Classes.Add("workbook-flat"); Classes.Add("icon-preset");
            Margin = default; Padding = new Thickness(6, 4); MinWidth = 0; Height = 32;
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
        }
        if (preset.Type == "ColorScale")
        {
            Classes.Add("color-scale-preset"); Margin = default; Padding = default; MinWidth = 0; Height = 32;
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch;
            Content = new ColorScaleSwatch(preset.Colors) { IsHitTestVisible = false };
        }
        else Content = new Swatch(preset);
    }
    private sealed class Swatch(ConditionalPreset preset) : Control
    {
        protected override Size MeasureOverride(Size availableSize) => new(0, 24);
        public override void Render(DrawingContext context)
        {
            var size = Math.Max(0, Math.Min(preset.Icons.Length >= 5 ? 12 : 14, (Bounds.Width - (preset.Icons.Length - 1) * 3) / preset.Icons.Length));
            var left = (Bounds.Width - preset.Icons.Length * size - (preset.Icons.Length - 1) * 3) / 2.0;
            for (var i = 0; i < preset.Icons.Length; i++)
                ConditionalRenderer.Icon(context, preset.Icons[i], preset.Colors[i], new Point(left + i * (size + 3), (Bounds.Height - size) / 2.0), size);
        }
    }
}
