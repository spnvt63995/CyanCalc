using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyanCalc.Controls;

// Platform adapter for unmodified Lucide SVG nodes and IronCalc's custom SVGs.
// Asset provenance and licenses are recorded in workbook-ui-assets.json and THIRD_PARTY.md.
internal sealed class WorkbookIcon : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<WorkbookIcon>();
    private sealed record ShapeData(Geometry Geometry, IBrush? Stroke, bool Filled);
    private sealed record Icon(double Size, double Width, double StrokeWidth, ShapeData[] Shapes);
    private static readonly Dictionary<string, Icon> Icons = LoadIcons();
    private string _iconName;
    private Icon _icon;

    static WorkbookIcon() => AffectsRender<WorkbookIcon>(ForegroundProperty);

    internal WorkbookIcon(string iconName, double size = 16)
    {
        _iconName = iconName;
        _icon = Icons[iconName];
        Width = Height = size;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
    }

    internal string IconName
    {
        get => _iconName;
        set
        {
            if (_iconName == value) return;
            _icon = Icons[value];
            _iconName = value;
            InvalidateVisual();
        }
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var scale = Math.Min(Bounds.Width / _icon.Width, Bounds.Height / _icon.Size);
        if (scale <= 0) return;
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) *
            Matrix.CreateTranslation((Bounds.Width - _icon.Width * scale) / 2, (Bounds.Height - _icon.Size * scale) / 2));
        var pen = new Pen(Foreground, _icon.StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        foreach (var shape in _icon.Shapes)
            context.DrawGeometry(shape.Filled ? Foreground : null, shape.Stroke is null ? pen : new Pen(shape.Stroke, _icon.StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), shape.Geometry);
    }

    private static Dictionary<string, Icon> LoadIcons()
    {
        var assembly = typeof(WorkbookIcon).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".workbook-ui-assets.json", StringComparison.Ordinal)))!;
        using var data = JsonDocument.Parse(stream);
        return data.RootElement.GetProperty("icons").EnumerateObject().ToDictionary(item => item.Name, item =>
            new Icon(item.Value.GetProperty("size").GetDouble(), item.Value.TryGetProperty("width", out var width) ? width.GetDouble() : item.Value.GetProperty("size").GetDouble(), item.Value.GetProperty("strokeWidth").GetDouble(),
                item.Value.GetProperty("nodes").EnumerateArray().Select(node => new ShapeData(Shape(node),
                    node.TryGetProperty("stroke", out var stroke) ? Brush.Parse(stroke.GetString()!) : null,
                    node.TryGetProperty("fill", out var fill) && fill.GetString() == "currentColor")).ToArray()));
    }

    private static Geometry Shape(JsonElement node)
    {
        double Number(string key, double fallback = 0) => node.TryGetProperty(key, out var value)
            ? double.Parse(value.ToString(), CultureInfo.InvariantCulture) : fallback;
        return node.GetProperty("type").GetString() switch
        {
            "path" => Geometry.Parse(node.GetProperty("d").GetString()!),
            "line" => new LineGeometry(new Point(Number("x1"), Number("y1")), new Point(Number("x2"), Number("y2"))),
            "circle" => new EllipseGeometry(new Rect(Number("cx") - Number("r"), Number("cy") - Number("r"), Number("r") * 2, Number("r") * 2)),
            "rect" => new RectangleGeometry(new Rect(Number("x"), Number("y"), Number("width"), Number("height")),
                Number("rx", Number("ry")), Number("ry", Number("rx"))),
            _ => throw new InvalidDataException("Unsupported upstream SVG node")
        };
    }
}
