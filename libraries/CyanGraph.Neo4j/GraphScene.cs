using Neo4j.Driver;

namespace CyanGraph.Data;

public sealed record NeighborProgress(int Loaded, bool HasMore, string After);
public sealed record HiddenElement(GraphElementKey Key, int NodesToRestore, int RelationshipsToRestore);

/// <summary>One query's display graph. Its table, raw records and cell index always refer to Original.</summary>
public sealed class GraphScene
{
    readonly GraphConnection connection;
    readonly Dictionary<string, ResultNode> nodes;
    readonly Dictionary<string, IRelationship> edges;
    readonly Dictionary<string, NeighborProgress> progress = new(StringComparer.Ordinal);
    readonly Stack<GraphElementKey> undo = [];
    readonly HashSet<GraphElementKey> hidden = [];
    Task loading = Task.CompletedTask;
    CancellationTokenSource? request;
    bool retired;
    public ResultGraph Original { get; }
    public ResultGraph Visible { get; private set; }
    public string? LoadingNode { get; private set; }
    public (string NodeId, string Message)? Failure { get; private set; }
    public event Action? Changed;

    internal GraphScene(GraphConnection connection, ResultGraph original)
    {
        this.connection = connection;
        Original = Visible = original;
        nodes = new(original.Nodes, StringComparer.Ordinal);
        edges = new(original.Relationships, StringComparer.Ordinal);
    }

    public NeighborProgress? ProgressFor(string nodeId) => progress.GetValueOrDefault(nodeId);

    public Task ExpandAsync(string nodeId)
    {
        if (request != null) return loading;
        if (retired || !Visible.Nodes.ContainsKey(nodeId) || ProgressFor(nodeId) is { HasMore: false })
            return Task.CompletedTask;
        var source = request = new CancellationTokenSource();
        LoadingNode = nodeId;
        Failure = null;
        loading = LoadAsync(nodeId, source);
        Changed?.Invoke();
        return loading;
    }

    async Task LoadAsync(string nodeId, CancellationTokenSource source)
    {
        try
        {
            await Task.Yield();
            var previous = ProgressFor(nodeId);
            var page = await connection.ReadNeighborsAsync(nodeId, previous?.After ?? "", source.Token);
            if (source.IsCancellationRequested) return;
            foreach (var link in page.Links)
            {
                Remember(link.Source);
                Remember(link.Other);
                edges.TryAdd(link.Relationship.ElementId, link.Relationship);
            }
            var cursor = page.Links.LastOrDefault()?.Relationship.ElementId ?? previous?.After ?? "";
            progress[nodeId] = new((previous?.Loaded ?? 0) + page.Links.Count, page.HasMore, cursor);
            if (page.Links.Count > 0) Rebuild();
        }
        catch (Exception error) when (!source.IsCancellationRequested) { Failure = (nodeId, error.Message); }
        catch (Exception) when (source.IsCancellationRequested) { }
        finally
        {
            request = null;
            LoadingNode = null;
            source.Dispose();
            if (!retired) Changed?.Invoke();
        }
    }

    void Remember(INode node)
    {
        if (!nodes.TryGetValue(node.ElementId, out var prior) || prior.Entity == null)
            nodes[node.ElementId] = new(node.ElementId, node);
    }

    public void Hide(GraphElementKey key)
    {
        if (retired || !Visible.Contains(key)) return;
        if (key.Kind == GraphElementKind.Node && key.Id == LoadingNode) request?.Cancel();
        hidden.Add(key);
        undo.Push(key);
        Rebuild();
        Changed?.Invoke();
    }

    public HiddenElement? LastHidden()
    {
        if (!undo.TryPeek(out var key)) return null;
        int restoredEdges = key.Kind == GraphElementKind.Relationship ? 1 :
            edges.Values.Count(edge => (edge.StartNodeElementId == key.Id || edge.EndNodeElementId == key.Id)
                && ShowEdge(edge, restoring: key));
        return new(key, key.Kind == GraphElementKind.Node ? 1 : 0, restoredEdges);
    }

    public void RestoreLast()
    {
        if (retired || !undo.TryPop(out var key)) return;
        hidden.Remove(key);
        Rebuild();
        Changed?.Invoke();
    }

    bool ShowEdge(IRelationship edge, GraphElementKey? restoring = null)
    {
        bool Show(GraphElementKind kind, string id)
        {
            var key = new GraphElementKey(kind, id);
            return key == restoring || !hidden.Contains(key);
        }
        return Show(GraphElementKind.Relationship, edge.ElementId) && Show(GraphElementKind.Node, edge.StartNodeElementId)
            && Show(GraphElementKind.Node, edge.EndNodeElementId);
    }

    void Rebuild() => Visible = Original.WithVisibleElements(
        nodes.Values.Where(node => !hidden.Contains(new(GraphElementKind.Node, node.ElementId))),
        edges.Values.Where(edge => ShowEdge(edge)));

    internal Task RetireAsync()
    {
        retired = true;
        request?.Cancel();
        return loading;
    }
}
