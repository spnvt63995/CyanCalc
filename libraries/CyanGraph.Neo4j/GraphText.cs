using System.Collections;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Neo4j.Driver;

namespace CyanGraph.Data;

public static class GraphText
{
    static readonly JsonSerializerOptions TextJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static string Label(INode node) => "(" + string.Join("", node.Labels.Select(l => ":" + l)) + Props(node) + ")";
    static string Edge(IRelationship rel) => "[:" + rel.Type + Props(rel) + "]";
    static string Props(IEntity e) => e.Properties.Count == 0 ? "" : " " + Map(e.Properties);
    static string Map(IReadOnlyDictionary<string, object> map) =>
        "{" + string.Join(", ", map.Select(kv => kv.Key + ": " + Format(kv.Value))) + "}";

    public static string Format(object? value) => value switch
    {
        null => "null",
        string s => JsonSerializer.Serialize(s, TextJson),
        bool b => b ? "true" : "false",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        INode node => Label(node),
        IRelationship rel => Edge(rel),
        IPath path => PathText(path),
        IReadOnlyDictionary<string, object> map => Map(map),
        IEnumerable list => "[" + string.Join(", ", list.Cast<object?>().Select(Format)) + "]",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    static string PathText(IPath path)
    {
        var text = new System.Text.StringBuilder(Label(path.Start));
        for (var i = 0; i < path.Relationships.Count; i++)
        {
            var relationship = path.Relationships[i];
            bool forward = relationship.StartNodeElementId == path.Nodes[i].ElementId;
            text.Append(forward ? "-" : "<-").Append(Edge(relationship)).Append(forward ? "->" : "-")
                .Append(Label(path.Nodes[i + 1]));
        }
        return text.ToString();
    }
}
