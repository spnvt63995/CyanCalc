using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CyanGraph.Data;

namespace CyanGraph;

static class GraphAppearance
{
    public static string Label(ResultNode node) => node.Entity?.Labels.FirstOrDefault() ?? (node.Entity == null ? "Endpoint" : "Node");
    public static string Caption(ResultNode node)
    {
        if (node.Entity != null)
        {
            var props = node.Entity.Properties;
            foreach (var key in new[] { "name", "title", "label", "word" })
                if (props.TryGetValue(key, out var value) && value is string s && !string.IsNullOrWhiteSpace(s)) return s;
        }
        return node.ElementId;
    }

    public static string Short(string text, int limit)
    {
        var span = text.AsSpan();
        int length = 0;
        for (int i = 0; i < limit && length < span.Length; i++)
            length += StringInfo.GetNextTextElementLength(span[length..]);
        return (length == span.Length ? text : text[..length] + "…").ReplaceLineEndings(" ");
    }

    public static string CanvasCaption(ResultNode node)
    {
        string value = Caption(node);
        if (value == node.ElementId && value.Length > 23) return "…" + value[^20..];
        return Short(value, 23);
    }

    public sealed record ColorPair(IBrush Fill, Pen Outline);
    static ColorPair Pair(string fill, string outline) => new(Brush.Parse(fill), new Pen(Brush.Parse(outline), 2));
    static readonly ColorPair[] Palette = [Pair("#C3EADD","#2B7963"), Pair("#D2E5FE","#456F9C"),
        Pair("#F5DFB6","#946B2C"), Pair("#E0D7F4","#78619A"), Pair("#F4D6DD","#A3596F"), Pair("#CCEBEE","#397C85")];
    static readonly ColorPair Endpoint = Pair("#E3E8EA", "#75848C");
    public static ColorPair Color(ResultNode node)
    {
        if (node.Entity == null) return Endpoint;
        uint hash = 2166136261;
        foreach (var c in Label(node)) hash = unchecked((hash ^ c) * 16777619);
        return Palette[hash % Palette.Length];
    }
}

sealed class GraphCanvas : Control
{
    static readonly IBrush Ground = Brush.Parse("#F7FAFA"), Dots = Brush.Parse("#D7E4E3"),
        EdgeColor = Brush.Parse("#60777C"), Selected = Brush.Parse("#006C66"), LabelGround = Brush.Parse("#F7FAFA");
    static readonly Pen EdgePen = new(EdgeColor, 2.2), SelectedPen = new(Selected, 3.6), Ring = new(Selected, 3);
    static readonly Cursor Hand = new(StandardCursorType.Hand), Pan = new(StandardCursorType.SizeAll);
    readonly Dictionary<(string Text, double Size, double Width), FormattedText> captions = [];
    ResultGraph? graph;
    GraphLayout? layout;
    public GraphElementKey? Selection { get; private set; }
    public event Action<GraphElementKey?>? SelectionChanged;
    public event Action<GraphElementKey?>? MenuRequested;
    public event Action? ViewChanged;
    public double Zoom { get; private set; } = 1;
    Vector origin;
    Point pressed;
    Vector startOrigin;
    IPointer? gesture;
    bool moved, autoFit = true;

    public GraphCanvas()
    {
        Focusable = true; ClipToBounds = true;
        AutomationProperties.SetName(this, "Query graph");
        ContextRequested += (_, e) =>
        {
            Focus();
            var target = e.TryGetPosition(this, out var position) ? Hit(position) : Selection;
            Select(target);
            MenuRequested?.Invoke(target);
            e.Handled = true;
        };
        SizeChanged += (_, _) => { if (autoFit) Fit(); };
    }

    public void SetGraph(ResultGraph? value)
    {
        gesture?.Capture(null); gesture = null;
        graph = value;
        layout = value is { Nodes.Count: > 0 } ? new GraphLayout(value) : null;
        captions.Clear(); Selection = null; Fit();
    }

    public void Fit()
    {
        autoFit = true;
        if (layout != null && Bounds.Width > 0 && Bounds.Height > 0)
        {
            var extent = layout.Extent;
            Zoom = Math.Clamp(Math.Min(Math.Max(1, Bounds.Width - 36) / extent.Width,
                Math.Max(1, Bounds.Height - 32) / extent.Height), 0.001, 1.35);
            origin = new Vector(Bounds.Width / 2 - extent.Center.X * Zoom, Bounds.Height / 2 - extent.Center.Y * Zoom);
        }
        else { Zoom = 1; origin = default; }
        ViewChanged?.Invoke(); InvalidateVisual();
    }

    public void ZoomBy(double factor, Point? anchor = null)
    {
        if (layout == null) return;
        var at = anchor ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
        var world = ToWorld(at);
        double min = Math.Min(0.12, Math.Min(Bounds.Width / layout.Extent.Width, Bounds.Height / layout.Extent.Height) * .8);
        Zoom = Math.Clamp(Zoom * factor, Math.Max(.0005, min), 5);
        origin = new Vector(at.X - world.X * Zoom, at.Y - world.Y * Zoom);
        autoFit = false; ViewChanged?.Invoke(); InvalidateVisual();
    }

    Point ToWorld(Point p) => new((p.X - origin.X) / Zoom, (p.Y - origin.Y) / Zoom);
    FormattedText Text(string value, double size, double width)
    {
        var key = (value, size, width);
        if (captions.TryGetValue(key, out var text)) return text;
        var face = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        return captions[key] = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, CyanGraphView.Ink)
            { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
    }

    Rect EdgeLabel(GraphRoute route)
    {
        var text = Text(GraphAppearance.Short(route.Relationship.Type, 20), 12, 132);
        return new Rect(route.Label.X - text.Width / 2 - 6, route.Label.Y - text.Height / 2 - 3, text.Width + 12, text.Height + 6);
    }
    static void CenterText(DrawingContext dc, FormattedText text, Point center) =>
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        dc.DrawRectangle(Ground, null, new Rect(Bounds.Size));
        if (graph == null || layout == null) return;
        for (double y = (origin.Y % 28 + 28) % 28; y < Bounds.Height; y += 28)
            for (double x = (origin.X % 28 + 28) % 28; x < Bounds.Width; x += 28)
                dc.DrawEllipse(Dots, null, new Point(x, y), .9, .9);

        using var transform = dc.PushTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(origin));
        foreach (var route in layout.Routes)
        {
            bool active = Selection == new GraphElementKey(GraphElementKind.Relationship, route.Relationship.ElementId);
            dc.DrawGeometry(null, active ? SelectedPen : EdgePen, route.Line);
            dc.DrawGeometry(active ? Selected : EdgeColor, null, route.Arrow);
        }
        foreach (var route in layout.Routes)
        {
            var box = EdgeLabel(route);
            bool active = Selection == new GraphElementKey(GraphElementKind.Relationship, route.Relationship.ElementId);
            dc.DrawRectangle(active ? GraphAppearance.Color(graph.Nodes[route.Relationship.StartNodeElementId]).Fill : LabelGround,
                active ? new Pen(Selected, 1) : null, box, 5, 5);
            CenterText(dc, Text(GraphAppearance.Short(route.Relationship.Type, 20), 12, 132), route.Label);
        }
        foreach (var (id, p) in layout.Centers)
        {
            var node = graph.Nodes[id];
            var color = GraphAppearance.Color(node);
            bool active = Selection == new GraphElementKey(GraphElementKind.Node, id);
            if (active) dc.DrawEllipse(null, Ring, p, GraphLayout.Radius + 5, GraphLayout.Radius + 5);
            dc.DrawEllipse(color.Fill, color.Outline, p, GraphLayout.Radius, GraphLayout.Radius);
            CenterText(dc, Text(GraphAppearance.Short(GraphAppearance.Label(node), 8), 12, 62), p);
            CenterText(dc, Text(GraphAppearance.CanvasCaption(node), 14, 168), p + new Vector(0, 55));
        }
    }

    GraphElementKey? Hit(Point screen)
    {
        if (layout == null || graph == null) return null;
        var p = ToWorld(screen);
        foreach (var (id, point) in layout.Centers)
        {
            var caption = Text(GraphAppearance.CanvasCaption(graph.Nodes[id]), 14, 168);
            Vector delta = p - point;
            if (delta.Length <= GraphLayout.Radius + 3 ||
                new Rect(point.X - caption.Width / 2, point.Y + 44, caption.Width, 22).Contains(p))
                return new GraphElementKey(GraphElementKind.Node, id);
        }
        GraphRoute? closest = null;
        double distance = Math.Max(4, 7 / Zoom);
        foreach (var route in layout.Routes)
        {
            if (EdgeLabel(route).Contains(p)) return new GraphElementKey(GraphElementKind.Relationship, route.Relationship.ElementId);
            if (!route.Bounds.Inflate(distance).Contains(p)) continue;
            double d = route.Distance(p);
            if (d < distance) { distance = d; closest = route; }
        }
        return closest == null ? null : new GraphElementKey(GraphElementKind.Relationship, closest.Relationship.ElementId);
    }

    void Select(GraphElementKey? item)
    {
        ShowSelection(item); SelectionChanged?.Invoke(item);
    }

    public void ShowSelection(GraphElementKey? item)
    {
        Selection = item;
        InvalidateVisual();
    }

    public void RevealSelection()
    {
        if (Selection is not { } key || layout == null || !IsEffectivelyVisible || Bounds.Width < 1 || Bounds.Height < 1) return;
        var world = key.Kind == GraphElementKind.Node ? layout.Centers[key.Id]
            : layout.Routes.First(edge => edge.Relationship.ElementId == key.Id).Label;
        var screen = new Point(world.X * Zoom + origin.X, world.Y * Zoom + origin.Y);
        var viewport = new Rect(Bounds.Size);
        var margin = Math.Min(48, Math.Min(viewport.Width, viewport.Height) / 4);
        if (viewport.Deflate(margin).Contains(screen)) return;
        origin += (Vector)(viewport.Center - screen); autoFit = false; InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || layout == null) return;
        Focus(); pressed = e.GetPosition(this); startOrigin = origin; moved = false;
        gesture = e.Pointer; gesture.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (gesture != e.Pointer) { Cursor = Hit(p).HasValue ? Hand : null; return; }
        Vector delta = p - pressed;
        if (!moved && delta.Length < 4) return;
        moved = true; autoFit = false; origin = startOrigin + delta; Cursor = Pan;
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (gesture != e.Pointer) return;
        if (!moved) Select(Hit(e.GetPosition(this)));
        gesture = null; e.Pointer.Capture(null); Cursor = null; e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e); gesture = null; Cursor = null;
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (layout == null || e.Delta.Y == 0) return;
        ZoomBy(Math.Pow(1.18, Math.Clamp(e.Delta.Y, -4, 4)), e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Select(null); e.Handled = true; }
    }
}
