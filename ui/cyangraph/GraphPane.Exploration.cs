using Avalonia.Controls;
using Avalonia.Layout;
using CyanGraph.Data;

namespace CyanGraph;

sealed partial class GraphPane
{
    readonly TextBlock visibleCount = CyanGraphView.Caption("");
    readonly Button undoHide;
    readonly ContextMenu actions = new();
    GraphScene? scene;
    public event Action? VisibilityChanged;

    public bool IsOnCanvas(GraphElementKey key) => graph?.Contains(key) == true;

    public void ShowScene(GraphScene? value)
    {
        actions.Close();
        if (scene != null) scene.Changed -= SceneChanged;
        scene = value;
        if (scene != null) scene.Changed += SceneChanged;
        canvas.ShowSelection(null);
        SceneChanged();
    }

    static string Counts(int nodes, int relationships) =>
        $"{nodes} {(nodes == 1 ? "node" : "nodes")} · {relationships} {(relationships == 1 ? "relationship" : "relationships")}";

    void SceneChanged()
    {
        var selected = canvas.Selection;
        var next = scene?.Visible;
        bool changed = !ReferenceEquals(next, graph);
        if (changed)
        {
            graph = next;
            canvas.SetGraph(graph);
            if (selected is { } key && IsOnCanvas(key)) canvas.ShowSelection(key);
        }
        visibleCount.Text = graph == null ? "" : "On canvas · " + Counts(graph.Nodes.Count, graph.Relationships.Count);
        var hidden = scene?.LastHidden();
        undoHide.IsVisible = hidden != null;
        if (hidden != null) ToolTip.SetTip(undoHide, RestoreText(hidden));
        Inspect(canvas.Selection);
        if (selected != canvas.Selection) ElementPicked?.Invoke(null);
        if (changed) VisibilityChanged?.Invoke();
    }

    static string RestoreText(HiddenElement hidden) =>
        "Undo hide · " + Counts(hidden.NodesToRestore, hidden.RelationshipsToRestore);

    void OpenMenu(GraphElementKey? target)
    {
        if (scene is not { } current) return;
        var items = new List<MenuItem>();
        void Item(string label, Action run, bool enabled = true)
        {
            var item = new MenuItem { Header = label, IsEnabled = enabled };
            item.Click += (_, _) => run();
            items.Add(item);
        }
        if (target is { } key)
        {
            if (key.Kind == GraphElementKind.Node)
            {
                var (label, enabled) = ExpandAction(current, key.Id);
                Item(label, () => _ = current.ExpandAsync(key.Id), enabled);
            }
            Item(key.Kind == GraphElementKind.Node ? "Hide node" : "Hide relationship", () => current.Hide(key));
        }
        if (current.LastHidden() is { } hidden) Item(RestoreText(hidden), current.RestoreLast);
        if (items.Count == 0) return;
        actions.ItemsSource = items;
        actions.Open(canvas);
    }

    static (string Label, bool Enabled) ExpandAction(GraphScene current, string id)
    {
        if (current.LoadingNode == id) return ("Loading neighbors…", false);
        var page = current.ProgressFor(id);
        if (page is { HasMore: false }) return ("All neighbors loaded", false);
        return (page == null ? "Expand neighbors" : $"Load next {GraphConnection.NeighborPageSize}", current.LoadingNode == null);
    }

    void AddSource(GraphElementKey key) =>
        inspector.Children.Add(CyanGraphView.Caption(scene!.Original.Contains(key) ? "From query result" : "From neighbor expansion"));

    void AddNeighborControls(string nodeId)
    {
        var current = scene!;
        var (label, enabled) = ExpandAction(current, nodeId);
        var page = current.ProgressFor(nodeId);
        if (page != null)
        {
            string noun = page.Loaded == 1 ? "relationship" : "relationships";
            var range = $"{page.Loaded} {noun} loaded" + (page.HasMore ? " · more available" : " · complete");
            inspector.Children.Add(Text(range, 13));
        }
        if (page == null || page.HasMore)
        {
            var button = Tool(label, label, () => _ = current.ExpandAsync(nodeId));
            button.IsEnabled = enabled;
            button.HorizontalAlignment = HorizontalAlignment.Left;
            inspector.Children.Add(button);
        }
        if (current.Failure is { } error && error.NodeId == nodeId)
            inspector.Children.Add(Text("Could not load neighbors. " + error.Message, 13));
    }
}
