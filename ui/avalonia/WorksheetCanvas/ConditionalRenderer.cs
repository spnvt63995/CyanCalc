using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;

namespace CyanCalc.Controls;

// Native drawing counterpart of cfRenderer.ts and its Lucide path specifications.
internal static class ConditionalRenderer
{
    private sealed record IconSpec(string[] Paths, bool Stroke = false, bool? Fill = null, double StrokeWidth = 1.5, bool FlipY = false);
    private static readonly Dictionary<string, IconSpec> Icons = LoadIcons();
    private static readonly Dictionary<string, Geometry> GeometryCache = new();
    private static Dictionary<string, IconSpec> LoadIcons()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(x => x.EndsWith("icons.json")))!;
        return JsonSerializer.Deserialize<Dictionary<string, IconSpec>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    internal static IBrush Brush(string? color, string fallback = "#202838") =>
        new SolidColorBrush(Color.TryParse(color, out var parsed) ? parsed : Color.Parse(fallback));

    public static void DataBar(DrawingContext context, Rect rect, ConditionalDataBar bar)
    {
        var left = rect.X + Math.Min(bar.AxisPosition, bar.Value) * rect.Width;
        var width = Math.Abs(bar.AxisPosition - bar.Value) * rect.Width;
        var positive = bar.Value >= bar.AxisPosition;
        var color = ((SolidColorBrush)Brush(positive ? bar.PositiveColor : bar.NegativeColor, "#63B5E4")).Color;
        IBrush brush = new SolidColorBrush(color);
        if (bar.IsGradient)
        {
            var light = Color.FromRgb((byte)(color.R + (255 - color.R) * .6), (byte)(color.G + (255 - color.G) * .6), (byte)(color.B + (255 - color.B) * .6));
            brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = [new GradientStop(positive ? light : color, 0), new GradientStop(positive ? color : light, 1)]
            };
        }
        var padding = Math.Max(2, rect.Height * .15);
        if (width > 0) context.FillRectangle(brush, new Rect(left, rect.Y + padding, width, Math.Max(0, rect.Height - padding * 2)));
    }

    public static void Icon(DrawingContext context, string name, string? color, Point origin, double size, bool uiPreview = false)
    {
        if (!Icons.TryGetValue(name, out var spec)) return;
        var brush = Brush(color);
        var scale = size / 24;
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y));
        using var flip = context.PushTransform(spec.FlipY ? Matrix.CreateScale(1, -1) * Matrix.CreateTranslation(0, 24) : Matrix.Identity);
        foreach (var path in spec.Paths)
        {
            if (!GeometryCache.TryGetValue(path, out var geometry)) GeometryCache[path] = geometry = Geometry.Parse(path);
            context.DrawGeometry((spec.Fill ?? !spec.Stroke) ? brush : null,
                // Lucide UI previews stroke every shape at 2 SVG units; sheet icons retain cfRenderer's strokes.
                spec.Stroke || uiPreview ? new Pen(brush, uiPreview ? 2 : spec.StrokeWidth / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round) : null, geometry);
        }
    }

    public static void Rating(DrawingContext context, Rect rect, ConditionalRating rating, double size)
    {
        for (var index = 0; index < rating.Count; index++)
            Icon(context, rating.Icon, rating.Color, new Point(rect.X + 3 + index * size, rect.Y + (rect.Height - size) / 2), size);
    }

    public static void Border(DrawingContext context, BorderItem? border, Point from, Point to)
    {
        if (border is null || border.Style is "none" or "") return;
        var style = border.Style.ToLowerInvariant();
        var width = style.StartsWith("medium") ? 2 : style == "thick" ? 3 : 1;
        var dash = style switch
        {
            "dotted" => new DashStyle([1, 2], 0),
            "dashed" or "mediumdashed" => new DashStyle([4, 2], 0),
            "dashdot" or "mediumdashdot" or "slantdashdot" => new DashStyle([4, 2, 1, 2], 0),
            "dashdotdot" or "mediumdashdotdot" => new DashStyle([4, 2, 1, 2, 1, 2], 0),
            _ => null
        };
        var pen = new Pen(Brush(border.Color), width, dash);
        if (style == "double")
        {
            var delta = from.X == to.X ? new Vector(1, 0) : new Vector(0, 1);
            context.DrawLine(pen, from - delta, to - delta);
            context.DrawLine(pen, from + delta, to + delta);
        }
        else context.DrawLine(pen, from, to);
    }
}
