using System.Text.Json;

namespace CyanGraph.Data;

public static class QueryParameters
{
    public static Dictionary<string, object> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new FormatException("Parameters must be a JSON object.");
        return Map(document.RootElement);
    }

    static Dictionary<string, object> Map(JsonElement element)
    {
        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!map.TryAdd(property.Name, Value(property.Value)!))
                throw new FormatException($"Duplicate parameter key: {property.Name}");
        return map;
    }

    static object? Value(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.GetRawText().IndexOfAny(['.', 'e', 'E']) >= 0 &&
            element.TryGetDouble(out var number) && double.IsFinite(number) => number,
        JsonValueKind.Number => throw new FormatException("A number is outside the Neo4j integer or float range."),
        JsonValueKind.Array => element.EnumerateArray().Select(Value).ToList(),
        JsonValueKind.Object => Map(element),
        _ => throw new FormatException("Unsupported parameter value.")
    };
}
