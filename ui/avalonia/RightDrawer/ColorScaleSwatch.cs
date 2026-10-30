using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CyanCalc.Controls;

// ColorScaleRule.steppedGradient: five horizontal bands, sampled from low to high.
internal sealed class ColorScaleSwatch(IReadOnlyList<string> colors) : Control
{
    public static readonly StyledProperty<IBrush?> OutlineBrushProperty =
        AvaloniaProperty.Register<ColorScaleSwatch, IBrush?>(nameof(OutlineBrush), Brushes.Black);
    public static readonly StyledProperty<double> InsetOpacityProperty =
        AvaloniaProperty.Register<ColorScaleSwatch, double>(nameof(InsetOpacity), .12);
    public IBrush? OutlineBrush { get => GetValue(OutlineBrushProperty); set => SetValue(OutlineBrushProperty, value); }
    public double InsetOpacity { get => GetValue(InsetOpacityProperty); set => SetValue(InsetOpacityProperty, value); }
    static ColorScaleSwatch() => AffectsRender<ColorScaleSwatch>(OutlineBrushProperty, InsetOpacityProperty);
    internal static Color Sample(IReadOnlyList<string> colors, double position)
    {
        var p = position * (colors.Count - 1); var low = (int)p; var high = Math.Min(low + 1, colors.Count - 1); var t = p - low;
        var a = Color.Parse(colors[low]); var b = Color.Parse(colors[high]);
        byte Mix(byte x, byte y) => (byte)Math.Floor(x + (y - x) * t + .5);
        return Color.FromRgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }
    internal static string Hex(Color color) => $"#{color.R:x2}{color.G:x2}{color.B:x2}";
    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        using (context.PushClip(new RoundedRect(rect, 4)))
            for (var i = 0; i < 5; i++)
                context.FillRectangle(new SolidColorBrush(Sample(colors, i / 4.0)), new Rect(0, i * rect.Height / 5, rect.Width, rect.Height / 5));
        using (context.PushOpacity(InsetOpacity))
            context.DrawRectangle(null, new Pen(OutlineBrush, 1), rect.Deflate(.5), 3.5, 3.5);
    }
}
