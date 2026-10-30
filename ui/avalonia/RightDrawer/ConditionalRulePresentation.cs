using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyanCalc.Controls;

internal static class ConditionalRulePresentation
{
    internal static bool ValidRanges(string ranges) => Regex.IsMatch(ranges, @"^[A-Z]+[1-9][0-9]*(?::[A-Z]+[1-9][0-9]*)?(?: [A-Z]+[1-9][0-9]*(?::[A-Z]+[1-9][0-9]*)?)*$");
    internal readonly record struct Range(int Left, int Top, int Right, int Bottom);
    internal static Range? ParseRange(string range)
    {
        var cells = range.Split(':');
        if (!CellAddress.TryParse(cells[0].Trim(), out var r1, out var c1) || !CellAddress.TryParse(cells.Length > 1 ? cells[1].Trim() : cells[0].Trim(), out var r2, out var c2)) return null;
        return new Range(Math.Min(c1, c2), Math.Min(r1, r2), Math.Max(c1, c2), Math.Max(r1, r2));
    }
    // Equivalent to isRangeInRanges, without visiting each cell of a full-column selection.
    internal static bool Covers(string selection, string ranges)
    {
        if (ParseRange(selection) is not { } selected) return false;
        var remaining = new List<Range> { selected };
        foreach (var text in ranges.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (ParseRange(text) is not { } cover) return false;
            var next = new List<Range>();
            foreach (var r in remaining)
            {
                var l = Math.Max(r.Left, cover.Left); var t = Math.Max(r.Top, cover.Top); var rr = Math.Min(r.Right, cover.Right); var b = Math.Min(r.Bottom, cover.Bottom);
                if (l > rr || t > b) { next.Add(r); continue; }
                if (r.Top < t) next.Add(new(r.Left, r.Top, r.Right, t - 1));
                if (b < r.Bottom) next.Add(new(r.Left, b + 1, r.Right, r.Bottom));
                if (r.Left < l) next.Add(new(r.Left, t, l - 1, b));
                if (rr < r.Right) next.Add(new(rr + 1, t, r.Right, b));
            }
            remaining = next;
        }
        return remaining.Count == 0;
    }
    internal static string Description(JsonObject rule, WorkbookText text, Func<string, string>? resolve = null)
    {
        string Get(string key) => rule[key]?.ToString() ?? "";
        string T(string key, params (string Name, object? Value)[] values) => text.Key("conditional_formatting." + key, values).Value;
        var type = Get("type"); var op = type == "TimePeriod" ? Get("time_period") : Get("operator");
        var key = op switch
        {
            "NotBetween" => "not_between", "LessThan" => "less_than", "LessThanOrEqual" => "less_than_or_equal", "GreaterThan" => "greater_than", "GreaterThanOrEqual" => "greater_than_or_equal", "Equal" or "Equals" => "equals", "NotEqual" => "does_not_equal",
            "DoesNotContain" => "does_not_contain", "BeginsWith" => "begins_with", "EndsWith" => "ends_with", "Last7Days" => "in_last_7_days", "Next7Days" => "in_next_7_days", "LastWeek" => "last_week", "ThisWeek" => "this_week", "NextWeek" => "next_week", "LastMonth" => "last_month", "ThisMonth" => "this_month", "NextMonth" => "next_month", "LastYear" => "last_year", "ThisYear" => "this_year", "NextYear" => "next_year", _ => op.ToLowerInvariant()
        };
        var v = Get(type == "Text" ? "value" : type == "TimePeriod" ? "date1" : "formula"); var v2 = Get(type == "TimePeriod" ? "date2" : "formula2");
        v = resolve?.Invoke(v) ?? v; v2 = resolve?.Invoke(v2) ?? v2;
        var between = op is "Between" or "NotBetween";
        if (type == "CellIs")
        {
            if (between) return v.Length > 0 && v2.Length > 0 ? T("desc_cell_value_" + key, ("value", v), ("value2", v2)) : T("desc_cell_value", ("op", T("desc_op_" + key)));
            var symbol = op switch { "LessThan" => "<", "LessThanOrEqual" => "≤", "GreaterThan" => ">", "GreaterThanOrEqual" => "≥", "Equal" => "=", "NotEqual" => "≠", _ => op };
            return T("desc_cell_value", ("op", symbol + (v.Length == 0 ? "" : " " + v)));
        }
        if (type == "Text") return v.Length > 0 ? T("desc_text_value", ("op", T("desc_op_" + key)), ("value", v)) : T("desc_text", ("op", T("desc_op_" + key)));
        if (type == "TimePeriod") return between && v.Length > 0 && v2.Length > 0 ? T("desc_date_" + key, ("value", v), ("value2", v2)) : T("desc_date", ("op", T("desc_op_" + key)));
        if (type == "Formula") return v.Length == 0 ? T("rule_type_formula") : T("desc_formula", ("value", v));
        if (type == "ColorScale") return T("desc_color_scale");
        if (type == "DataBar") return T("desc_data_bars");
        if (type is "IconSet" or "IconRating") return T("icon_sets_mode_preset");
        return WorkbookOptions.RuleType(text, type).Value;
    }
}

internal sealed class ConditionalRulePreview : Control
{
    private readonly JsonObject _rule;
    private readonly UserModel? _model;
    internal ConditionalRulePreview(JsonObject rule, UserModel? model) { _rule = (JsonObject)rule.DeepClone(); _model = model; Height = 38; MinWidth = 65; IsHitTestVisible = false; }
    private string Color(JsonNode? value) => _model?.ResolveColor(JsonSerializer.SerializeToElement(value)) ?? (value is JsonValue ? value.ToString() : "");
    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size); var type = _rule["type"]!.GetValue<string>();
        if (type == "ColorScale")
        {
            var stops = _rule["thresholds"]!.AsArray();
            for (var i = 0; i < 5; i++)
            {
                var p = i / 4.0 * (stops.Count - 1); var low = (int)p; var high = Math.Min(low + 1, stops.Count - 1); var t = p - low;
                var a = Avalonia.Media.Color.Parse(Color(stops[low]!["color"])); var b = Avalonia.Media.Color.Parse(Color(stops[high]!["color"]));
                byte Mix(byte x, byte y) => (byte)Math.Floor(x + (y - x) * t + .5);
                context.FillRectangle(new SolidColorBrush(Avalonia.Media.Color.FromRgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B))), new Rect(i * r.Width / 5, 2, r.Width / 5, r.Height - 4));
            }
        }
        else if (type == "DataBar")
        {
            var widths = new[] { .6, .85, .35, .55 };
            for (var i = 0; i < 4; i++) ConditionalRenderer.DataBar(context, new Rect(r.X, r.Y + i * r.Height / 4, r.Width, r.Height / 4), new(Color(_rule["positive_color"]), Color(_rule["negative_color"]), _rule["is_gradient"]!.GetValue<bool>(), widths[i], 0, true));
        }
        else if (type is "IconSet" or "IconRating")
        {
            var icons = _rule["thresholds"]!.AsArray(); var size = Math.Min(24, r.Width / icons.Count);
            for (var i = 0; i < icons.Count; i++)
            {
                var item = icons[icons.Count - 1 - i]!;
                ConditionalRenderer.Icon(context, type == "IconRating" ? _rule["icon"]!.GetValue<string>() : item["icon"]!.GetValue<string>(), Color(type == "IconRating" ? _rule["color"] : item["color"]), new Point(i * size, (r.Height - size) / 2), size);
            }
        }
        else
        {
            var format = _rule["format"];
            context.FillRectangle(ConditionalRenderer.Brush(Color(format?["fill"]?["color"]), "#FFFFFF"), r);
            var font = format?["font"];
            var typeface = new Typeface(FontFamily.Default, font?["i"]?.GetValue<bool>() == true ? FontStyle.Italic : FontStyle.Normal, font?["b"]?.GetValue<bool>() == true ? FontWeight.Bold : FontWeight.Normal);
            var text = new FormattedText("Aa", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12, ConditionalRenderer.Brush(Color(font?["color"]), "#000000"));
            var decorations = new TextDecorationCollection(); if (font?["u"]?.GetValue<bool>() == true) decorations.Add(TextDecorations.Underline[0]); if (font?["strike"]?.GetValue<bool>() == true) decorations.Add(TextDecorations.Strikethrough[0]); text.SetTextDecorations(decorations);
            context.DrawText(text, new Point((r.Width - text.Width) / 2, (r.Height - text.Height) / 2));
        }
    }
}

// EditRule's icon header uses an 18px glyph; list samples use 16px.
internal sealed class ConditionalIconHeaderPreview : Grid
{
    internal ConditionalIconHeaderPreview(string icon, string color)
    {
        Width = 40; Height = 32; IsHitTestVisible = false;
        var background = new Border { CornerRadius = new CornerRadius(4) };
        background.Bind(Border.BackgroundProperty, background.GetResourceObservable("Dialog-common-white"));
        var frame = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Opacity = .12 };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Dialog-common-black"));
        Children.Add(background); Children.Add(frame);
        Children.Add(new HeaderIcon(icon, color) { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
    }
    private sealed class HeaderIcon(string icon, string color) : Control
    {
        public override void Render(DrawingContext context) => ConditionalRenderer.Icon(context, icon, color, default, 18, uiPreview: true);
    }
}
