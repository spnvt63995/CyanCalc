using Avalonia;
using Avalonia.Media;
using CyanGraph.Data;
using IRelationship = Neo4j.Driver.IRelationship;

namespace CyanGraph;

sealed class GraphRoute
{
    public IRelationship Relationship { get; }
    public StreamGeometry Line { get; } = new();
    public StreamGeometry Arrow { get; } = new();
    public Point[] Samples { get; } = new Point[33];
    public Point Label => Samples[Samples.Length / 2];
    public Rect Bounds { get; private set; }

    public GraphRoute(IRelationship relationship, Point start, Point firstControl, Point secondControl, Point end)
    {
        Relationship = relationship;
        using (var path = Line.Open())
        {
            path.BeginFigure(start, false);
            path.CubicBezierTo(firstControl, secondControl, end);
            path.EndFigure(false);
        }
        Vector tangent = end - secondControl;
        var direction = tangent / tangent.Length;
        var back = end - direction * 14;
        var side = new Vector(-direction.Y, direction.X) * 6;
        using (var arrow = Arrow.Open())
        {
            arrow.BeginFigure(end, true);
            arrow.LineTo(back + side); arrow.LineTo(back - side);
            arrow.EndFigure(true);
        }
        for (var i = 0; i < Samples.Length; i++)
        {
            var t = i / (double)(Samples.Length - 1);
            // De Casteljau interpolation yields points on the rendered cubic for labels and hit testing.
            var a = Between(start, firstControl, t);
            var b = Between(firstControl, secondControl, t);
            var c = Between(secondControl, end, t);
            Samples[i] = Between(Between(a, b, t), Between(b, c, t), t);
        }
        Bounds = Line.Bounds.Inflate(8);
    }

    public void Translate(Vector offset)
    {
        var move = new TranslateTransform(offset.X, offset.Y);
        Line.Transform = move; Arrow.Transform = move;
        Bounds = Bounds.Translate(offset);
        for (int i = 0; i < Samples.Length; i++) Samples[i] += offset;
    }

    static Point Between(Point a, Point b, double t) => a + (b - a) * t;

    public double Distance(Point p)
    {
        double closest = double.PositiveInfinity;
        for (var i = 1; i < Samples.Length; i++)
        {
            var a = Samples[i - 1];
            Vector segment = Samples[i] - a;
            var lengthSquared = Vector.Dot(segment, segment);
            var t = lengthSquared == 0 ? 0 : Math.Clamp(Vector.Dot(p - a, segment) / lengthSquared, 0, 1);
            Vector difference = p - (a + segment * t);
            closest = Math.Min(closest, difference.Length);
        }
        return closest;
    }
}

sealed class GraphLayout
{
    public const double Radius = 36;
    public Dictionary<string, Point> Centers { get; } = new(StringComparer.Ordinal);
    public List<GraphRoute> Routes { get; } = [];
    public Rect Extent { get; }

    public GraphLayout(ResultGraph graph)
    {
        if (graph.Nodes.Count == 0) return;
        var neighbors = graph.Nodes.Keys.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal));
        foreach (var edge in graph.Relationships.Values)
        {
            if (edge.StartNodeElementId == edge.EndNodeElementId) continue;
            neighbors[edge.StartNodeElementId].Add(edge.EndNodeElementId);
            neighbors[edge.EndNodeElementId].Add(edge.StartNodeElementId);
        }

        var groups = graph.Relationships.Values.GroupBy(r =>
            string.CompareOrdinal(r.StartNodeElementId, r.EndNodeElementId) <= 0
                ? (r.StartNodeElementId, r.EndNodeElementId) : (r.EndNodeElementId, r.StartNodeElementId))
            .ToLookup(g => g.Key.Item1);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var components = new List<(Dictionary<string, Point> Positions, List<GraphRoute> Routes, Rect Bounds)>();
        foreach (var id in graph.Nodes.Keys)
        {
            if (seen.Contains(id)) continue;
            var members = Layers(id, neighbors, seen).SelectMany(layer => layer).ToList();
            var positions = Place(members, neighbors);
            var routes = members.SelectMany(node => groups[node]).SelectMany(group => RouteGroup(group.ToArray(), positions)).ToList();
            components.Add((positions, routes, BoundsFor(positions, routes)));
        }

        // Tile disconnected components using their full bounds, including captions and loop arcs.
        var shelfWidth = Math.Max(components.Max(c => c.Bounds.Width),
            Math.Sqrt(components.Sum(c => c.Bounds.Width * c.Bounds.Height)) * 1.4);
        double x = 0, y = 0, shelfHeight = 0, right = 0;
        foreach (var (positions, routes, bounds) in components)
        {
            if (x > 0 && x + bounds.Width > shelfWidth) { x = 0; y += shelfHeight + 28; shelfHeight = 0; }
            var shift = new Vector(x - bounds.X, y - bounds.Y);
            foreach (var (id, point) in positions) Centers.Add(id, point + shift);
            foreach (var route in routes) route.Translate(shift);
            Routes.AddRange(routes);
            right = Math.Max(right, x + bounds.Width);
            x += bounds.Width + 28;
            shelfHeight = Math.Max(shelfHeight, bounds.Height);
        }
        Extent = new Rect(0, 0, right, y + shelfHeight);
    }

    static List<List<string>> Layers(string root, Dictionary<string, HashSet<string>> neighbors, HashSet<string> seen)
    {
        seen.Add(root);
        var layers = new List<List<string>> { new() { root } };
        for (int level = 0; level < layers.Count; level++)
        {
            var next = new List<string>();
            foreach (var id in layers[level])
                foreach (var adjacent in neighbors[id])
                    if (seen.Add(adjacent)) next.Add(adjacent);
            if (next.Count > 0) layers.Add(next);
        }
        return layers;
    }

    static Dictionary<string, Point> Place(List<string> nodes, Dictionary<string, HashSet<string>> neighbors)
    {
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        if (nodes.Count <= 2)
        {
            for (int i = 0; i < nodes.Count; i++) positions[nodes[i]] = new Point(i * 286, 0);
            return positions;
        }
        var center = nodes.MaxBy(id => neighbors[id].Count)!;
        positions[center] = new Point(0, 0);
        var layers = Layers(center, neighbors, new HashSet<string>(StringComparer.Ordinal));
        double radius = 0;
        foreach (var layer in layers.Skip(1))
        {
            radius = Math.Max(radius + 166, layer.Count * 165 / Math.Tau);
            for (int i = 0; i < layer.Count; i++)
            {
                double angle = Math.Tau * i / layer.Count - Math.PI / 2;
                positions[layer[i]] = new Point(radius * Math.Cos(angle), radius * Math.Sin(angle));
            }
        }
        return positions;
    }

    static IEnumerable<GraphRoute> RouteGroup(IRelationship[] edges, Dictionary<string, Point> points)
    {
        for (int i = 0; i < edges.Length; i++)
        {
            var edge = edges[i];
            var from = points[edge.StartNodeElementId];
            var to = points[edge.EndNodeElementId];
            if (edge.StartNodeElementId == edge.EndNodeElementId)
            {
                double width = 62 + i * 36, height = 100 + i * 57;
                yield return new GraphRoute(edge, from + new Vector(-24, -27), from + new Vector(-width, -height),
                    to + new Vector(width, -height), to + new Vector(24, -27));
                continue;
            }
            Vector delta = to - from;
            var normal = new Vector(-delta.Y, delta.X) / delta.Length;
            if (string.CompareOrdinal(edge.StartNodeElementId, edge.EndNodeElementId) > 0) normal = -normal;
            var control = from + delta * 0.5 + normal * ((i - (edges.Length - 1) / 2.0) * 100);
            Vector a = control - from, b = control - to;
            var start = from + a * (Radius / a.Length);
            var end = to + b * (Radius / b.Length);
            // The two cubic controls describe the same quadratic, with tangents clipped to each circle.
            yield return new GraphRoute(edge, start, start + (control - start) * (2.0 / 3),
                end + (control - end) * (2.0 / 3), end);
        }
    }

    static Rect BoundsFor(Dictionary<string, Point> positions, IEnumerable<GraphRoute> routes)
    {
        var bounds = positions.Values.Select(p => new Rect(p.X - 90, p.Y - Radius - 10, 180, Radius * 2 + 48))
            .Aggregate((a, b) => a.Union(b));
        foreach (var route in routes)
        {
            bounds = bounds.Union(route.Bounds)
                .Union(new Rect(route.Label.X - 72, route.Label.Y - 13, 144, 26));
        }
        return bounds;
    }
}
