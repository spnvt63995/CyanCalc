using System.Collections;
using Neo4j.Driver;

namespace CyanGraph.Data;

public sealed record ResultNode(string ElementId, INode? Entity);
public enum GraphElementKind { Node, Relationship }
public readonly record struct GraphElementKey(GraphElementKind Kind, string Id)
{
    public static GraphElementKey? FromValue(object? value) => value switch
    {
        INode n => new(GraphElementKind.Node, n.ElementId),
        IRelationship r => new(GraphElementKind.Relationship, r.ElementId),
        _ => null
    };
}
public readonly record struct ResultCell(int Row, int Column);

/// <summary>Element IDs are scoped to this query result. A relationship can return endpoint IDs
/// without either node's labels or properties.</summary>
public sealed class ResultGraph
{
    readonly Dictionary<string, ResultNode> nodes = new(StringComparer.Ordinal);
    readonly Dictionary<string, IRelationship> relationships = new(StringComparer.Ordinal);
    readonly Dictionary<GraphElementKey, HashSet<ResultCell>> cells = [];
    public IReadOnlyDictionary<string, ResultNode> Nodes => nodes;
    public IReadOnlyDictionary<string, IRelationship> Relationships => relationships;
    public bool Contains(GraphElementKey key) => key.Kind == GraphElementKind.Node
        ? nodes.ContainsKey(key.Id) : relationships.ContainsKey(key.Id);
    public IReadOnlyCollection<ResultCell> CellsFor(GraphElementKey key) =>
        cells.TryGetValue(key, out var found) ? found : Array.Empty<ResultCell>();

    public ResultGraph() { }
    ResultGraph(ResultGraph original) { cells = original.cells; }

    internal ResultGraph WithVisibleElements(IEnumerable<ResultNode> visibleNodes, IEnumerable<IRelationship> visibleEdges)
    {
        var view = new ResultGraph(this);
        foreach (var node in visibleNodes) view.nodes.Add(node.ElementId, node);
        foreach (var edge in visibleEdges) view.relationships.Add(edge.ElementId, edge);
        return view;
    }

    public static ResultGraph From(GraphResult result)
    {
        var graph = new ResultGraph();
        for (int row = 0; row < result.Rows.Count; row++)
            for (int column = 0; column < result.Columns.Count; column++)
                graph.Visit(result.Rows[row][column], new ResultCell(row, column));
        return graph;
    }

    void Link(GraphElementKey key, ResultCell cell)
    {
        if (!cells.TryGetValue(key, out var positions)) cells.Add(key, positions = []);
        positions.Add(cell);
    }

    void AddNode(INode node, ResultCell cell)
    {
        Link(new(GraphElementKind.Node, node.ElementId), cell);
        if (!nodes.TryGetValue(node.ElementId, out var previous) || previous.Entity == null)
            nodes[node.ElementId] = new ResultNode(node.ElementId, node);
    }

    void AddRelationship(IRelationship relationship, ResultCell cell)
    {
        Link(new(GraphElementKind.Relationship, relationship.ElementId), cell);
        if (!relationships.TryAdd(relationship.ElementId, relationship)) return;
        nodes.TryAdd(relationship.StartNodeElementId, new ResultNode(relationship.StartNodeElementId, null));
        nodes.TryAdd(relationship.EndNodeElementId, new ResultNode(relationship.EndNodeElementId, null));
    }

    void Visit(object? value, ResultCell cell)
    {
        switch (value)
        {
            case INode node:
                AddNode(node, cell);
                break;
            case IRelationship relationship:
                AddRelationship(relationship, cell);
                break;
            case IPath path:
                foreach (var node in path.Nodes) AddNode(node, cell);
                foreach (var edge in path.Relationships) AddRelationship(edge, cell);
                break;
            case IReadOnlyDictionary<string, object> map:
                foreach (var item in map.Values) Visit(item, cell);
                break;
            case IEnumerable items when value is not string and not byte[]:
                foreach (var item in items) Visit(item, cell);
                break;
        }
    }
}
