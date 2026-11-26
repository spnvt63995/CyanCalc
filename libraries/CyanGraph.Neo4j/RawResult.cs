using System.Collections;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Neo4j.Driver;

namespace CyanGraph.Data;

/// <summary>A JSON view of the records already fetched. The separate columns array preserves
/// the schema for empty results and gives every row the same positional interpretation.</summary>
public static class RawResult
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = 1024
    };

    public static string Format(GraphResult result, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var rows = result.Rows.Select(record => Enumerable.Range(0, result.Columns.Count)
            .Select(column => Value(record[column], token)));
        return JsonSerializer.Serialize(new { columns = result.Columns, rows, hasMore = result.HasMore }, Options);
    }

    internal static object? Value(object? value, CancellationToken token, bool browserRecord = false)
    {
        token.ThrowIfCancellationRequested();
        object? Nested(object? item) => Value(item, token, browserRecord);
        return value switch
        {
            null or string or bool or long or int or short or sbyte or byte => value,
            double d when double.IsFinite(d) => d,
            float f when float.IsFinite(f) => f,
            INode n => new { type = "node", elementId = n.ElementId, labels = n.Labels, properties = Nested(n.Properties) },
            IRelationship r => Relationship(r, Nested(r.Properties), browserRecord),
            IPath p => new { type = "path", nodes = p.Nodes.Select(n => Nested(n)),
                relationships = p.Relationships.Select(r => Nested(r)) },
            byte[] bytes => new { type = "bytes", base64 = Convert.ToBase64String(bytes) },
            Point p => new { type = "point", srid = p.SrId, coordinates = (double.IsNaN(p.Z)
                ? new[] { p.X, p.Y } : new[] { p.X, p.Y, p.Z }).Select(x => Nested(x)) },
            Duration d => new { type = "duration", months = d.Months, days = d.Days, seconds = d.Seconds, nanos = d.Nanos },
            IVector v => new { type = "vector", elementType = v.ElementType.ToString(), values = v.UntypedValues.Cast<object?>().Select(Nested) },
            UnsupportedType u => new { type = "unsupported", name = u.Name, minimumProtocolVersion = u.MinimumProtocolVersion.ToString(), message = u.Message },
            IReadOnlyDictionary<string, object> map => map.ToDictionary(p => p.Key, p => Nested(p.Value), StringComparer.Ordinal),
            IEnumerable list => list.Cast<object?>().Select(Nested),
            _ => new { type = value.GetType().Name, value = Convert.ToString(value, CultureInfo.InvariantCulture) }
        };
    }

    static Dictionary<string, object?> Relationship(IRelationship edge, object? properties, bool browserRecord)
    {
        var value = new Dictionary<string, object?>
        {
            ["type"] = browserRecord ? edge.Type : "relationship", ["elementId"] = edge.ElementId
        };
        if (!browserRecord) value["relationshipType"] = edge.Type;
        value["startNodeElementId"] = edge.StartNodeElementId;
        value["endNodeElementId"] = edge.EndNodeElementId;
        value["properties"] = properties;
        return value;
    }
}
