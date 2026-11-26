using System.Collections;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CyanGraph.Data;

namespace CyanGraph;

sealed partial class GraphPane : Grid
{
    readonly GraphCanvas canvas = new();
    readonly TextBlock scale = CyanGraphView.Caption("100%");
    readonly StackPanel inspector = new() { Spacing = 10, Margin = new Thickness(16) };
    readonly ScrollViewer scroll = new();
    ResultGraph? graph;
    public event Action<GraphElementKey?>? ElementPicked;

    readonly GraphDialogs dialogs;

    public GraphPane(GraphDialogs dialogs)
    {
        this.dialogs = dialogs;
        ColumnDefinitions = new("*,272"); RowDefinitions = new("Auto,*");
        var toolbar = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(12, 5) };
        visibleCount.TextTrimming = TextTrimming.CharacterEllipsis;
        CyanGraphView.Put(toolbar, visibleCount, 0, 0);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        undoHide = Tool("Undo hide", "Undo hide", () => scene?.RestoreLast());
        undoHide.IsVisible = false;
        tools.Children.Add(undoHide);
        scale.MinWidth = 48; scale.TextAlignment = TextAlignment.Center;
        tools.Children.Add(Tool("−", "Zoom out", () => canvas.ZoomBy(1 / 1.25)));
        tools.Children.Add(scale);
        tools.Children.Add(Tool("+", "Zoom in", () => canvas.ZoomBy(1.25)));
        tools.Children.Add(Tool("Fit", "Fit graph", canvas.Fit));
        CyanGraphView.Put(toolbar, tools, 0, 1);
        CyanGraphView.Put(this, toolbar, 0, 0);
        CyanGraphView.Put(this, canvas, 1, 0);
        scroll.Content = inspector;
        AutomationProperties.SetName(scroll, "Graph inspector");
        var panel = new Border { Background = CyanGraphView.Paper, BorderBrush = CyanGraphView.Line,
            BorderThickness = new Thickness(1, 0, 0, 0), Child = scroll };
        SetRowSpan(panel, 2); CyanGraphView.Put(this, panel, 0, 1);
        canvas.ViewChanged += () => scale.Text = $"{canvas.Zoom:P0}";
        canvas.SelectionChanged += item => { Inspect(item); ElementPicked?.Invoke(item); };
        canvas.MenuRequested += OpenMenu;
    }

    static Button Tool(string text, string name, Action action)
    {
        var b = new Button { Content = text, MinWidth = 32, Padding = new Thickness(8, 3),
            HorizontalContentAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetName(b, name); ToolTip.SetTip(b, name);
        b.Click += (_, _) => action();
        return b;
    }

    public void SelectFromTable(GraphElementKey? item)
    {
        if (item is { } key && !IsOnCanvas(key)) item = null;
        if (canvas.Selection != item) { canvas.ShowSelection(item); Inspect(item); }
        canvas.RevealSelection();
    }

    public void RevealSelection() => canvas.RevealSelection();

    static TextBlock Text(string text, double size, bool bold = false) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = CyanGraphView.Ink, TextWrapping = TextWrapping.Wrap
    };

    void Inspect(GraphElementKey? selected)
    {
        inspector.Children.Clear(); scroll.Offset = default;
        if (graph == null) return;
        if (selected is not { } key)
        {
            inspector.Children.Add(Text("Graph overview", 17, true));
            inspector.Children.Add(Text(Counts(graph.Nodes.Count, graph.Relationships.Count), 14));
            var original = scene!.Original;
            if (!ReferenceEquals(original, graph))
                inspector.Children.Add(Text("Original query · " + Counts(original.Nodes.Count, original.Relationships.Count), 13));
            foreach (var group in graph.Nodes.Values.GroupBy(GraphAppearance.Label))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var color = GraphAppearance.Color(group.First());
                row.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6),
                    Background = color.Fill, BorderBrush = color.Outline.Brush, BorderThickness = new Thickness(1) });
                row.Children.Add(Text($"{group.Key} ({group.Count()})", 13)); inspector.Children.Add(row);
            }
            foreach (var group in graph.Relationships.Values.GroupBy(edge => edge.Type).OrderBy(group => group.Key, StringComparer.Ordinal))
                inspector.Children.Add(Text($"{group.Key} ({group.Count()})", 13));
            var hint = CyanGraphView.Caption("Right-click for graph actions. Drag to pan or scroll to zoom."); hint.TextWrapping = TextWrapping.Wrap;
            inspector.Children.Add(hint); return;
        }
        string id = key.Id;
        if (key.Kind == GraphElementKind.Node)
        {
            var node = graph.Nodes[id];
            inspector.Children.Add(Text("Node details", 17, true));
            inspector.Children.Add(Text(GraphAppearance.Short(GraphAppearance.Caption(node), 90), 16, true));
            AddSource(key);
            AddNeighborControls(id);
            if (node.Entity != null)
            {
                Field("Labels", node.Entity.Labels.Count == 0 ? "None" : string.Join(" · ", node.Entity.Labels));
                Properties(node.Entity.Properties);
            }
            else inspector.Children.Add(Text("Only the endpoint ID was returned by this query.", 13));
        }
        else
        {
            var edge = graph.Relationships[id];
            inspector.Children.Add(Text("Relationship details", 17, true));
            AddSource(key);
            Field("Type", edge.Type);
            Endpoint("From", graph.Nodes[edge.StartNodeElementId]);
            Endpoint("To", graph.Nodes[edge.EndNodeElementId]);
            Properties(edge.Properties);
        }
        Field("Element ID", id);
    }

    void Endpoint(string label, ResultNode node)
    {
        string caption = GraphAppearance.Caption(node);
        var value = Field(label, GraphAppearance.Short(caption, 64));
        if (caption != node.ElementId) value.Children.Add(Text(node.ElementId, 11));
    }

    StackPanel Field(string key, string value)
    {
        var field = new StackPanel { Spacing = 3 };
        field.Children.Add(new TextBlock { Text = key, FontSize = 12, Foreground = CyanGraphView.Muted, TextWrapping = TextWrapping.Wrap });
        field.Children.Add(Text(value, 14)); inspector.Children.Add(field);
        return field;
    }

    void Properties(IReadOnlyDictionary<string, object> properties)
    {
        inspector.Children.Add(new Border { Height = 1, Background = CyanGraphView.Line, Margin = new Thickness(0, 2) });
        inspector.Children.Add(Text("Properties", 14, true));
        if (properties.Count == 0) inspector.Children.Add(Text("No properties", 13));
        foreach (var (name, raw) in properties.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var full = raw is string s ? s : GraphText.Format(raw);
            var preview = GraphAppearance.Short(full, 75);
            var field = Field(name, preview);
            if (preview == full && raw is not (IEnumerable and not string)) continue;
            var open = new Button { Content = "Full value…", Padding = new Thickness(8, 3) };
            AutomationProperties.SetName(open, "Open full value for " + name);
            open.Click += async (_, _) =>
            {
                await ShowFullValue(name, full);
            };
            field.Children.Add(open);
        }
    }

    async Task ShowFullValue(string name, string value)
    {
        var dialog = new Window { Title = "Full property value", Width = 640, Height = 360, MinWidth = 320,
            MinHeight = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = CyanGraphView.Paper };
        var body = new Grid { Margin = new Thickness(20), RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12 };
        var full = new TextBox { Text = value, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(12) };
        AutomationProperties.SetName(full, "Full value of " + name);
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close(); };
        CyanGraphView.Put(body, Text(name, 18, true), 0, 0); CyanGraphView.Put(body, full, 1, 0); CyanGraphView.Put(body, close, 2, 0);
        dialog.Content = body; await dialogs.ShowAsync(dialog, this);
    }
}
