using System.Text.Json;
using System.Text.Json.Nodes;
using CyanCalc;

namespace CyanCalc.Controls.Preview;

// Work only on a display copy. In particular, never replace stored slot/tint colors.
internal static class StylePreviewResolver
{
    internal static CellStyle Resolve(UserModel model, JsonElement style)
    {
        var copy = JsonNode.Parse(style.GetRawText())!.AsObject();
        foreach (var group in new[] { "font", "fill" })
            if (copy[group] is JsonObject value) ResolveColor(value);
        if (copy["border"] is JsonObject borders)
            foreach (var edge in new[] { "left", "right", "top", "bottom", "diagonal" })
                if (borders[edge] is JsonObject value) ResolveColor(value);
        // The engine omits default vertical alignment. getTileStyle leaves that
        // unset (CSS center); the cell-rendering DTO would otherwise supply bottom.
        if (copy["alignment"] is JsonObject alignment && !alignment.ContainsKey("vertical"))
            alignment["vertical"] = "";
        return copy.Deserialize<CellStyle>(UserModel.JsonOptions)!;

        void ResolveColor(JsonObject value)
        {
            if (value["color"] is { } color)
                value["color"] = model.ResolveColor(JsonSerializer.SerializeToElement(color));
        }
    }

    internal static bool Matches(JsonElement active, JsonElement style, JsonElement includes)
    {
        // The engine does not retain a named-style identity. Compare included raw
        // groups on the active cell, rather than claiming the entire range matches.
        var compared = false;
        // Pinned Style carries no protection payload; includes.protection alone
        // cannot establish a visual match. quote_prefix belongs to cell content.
        foreach (var group in new[] { "num_fmt", "font", "fill", "border", "alignment" })
        {
            var key = group == "num_fmt" ? "number_format" : group;
            if (includes.ValueKind == JsonValueKind.Object && includes.TryGetProperty(key, out var enabled) && !enabled.GetBoolean()) continue;
            compared = true;
            active.TryGetProperty(group, out var a); style.TryGetProperty(group, out var b);
            if (!JsonNode.DeepEquals(Canonical(a), Canonical(b))) return false;
        }
        return compared;
    }

    internal static bool SameTheme(JsonElement a, JsonElement b) =>
        JsonNode.DeepEquals(JsonNode.Parse(a.GetRawText()), JsonNode.Parse(b.GetRawText()));

    private static JsonNode? Canonical(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.False) return null;
        if (value.ValueKind == JsonValueKind.Object)
        {
            var result = new JsonObject();
            foreach (var field in value.EnumerateObject())
                if (Canonical(field.Value) is { } child) result[field.Name] = child;
            return result.Count == 0 ? null : result;
        }
        return JsonNode.Parse(value.GetRawText());
    }
}
