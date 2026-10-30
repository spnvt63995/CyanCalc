using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CyanCalc.Controls;

// DataBarsRule.tsx: four cells, with separate header and padded preset frames.
internal sealed class DataBarSwatch(string color, bool gradient, bool header = false) : Control
{
    internal static readonly double[] BarWidths = [.6, .85, .35, .55];
    public static readonly StyledProperty<IBrush?> OutlineBrushProperty =
        AvaloniaProperty.Register<DataBarSwatch, IBrush?>(nameof(OutlineBrush), Brushes.Black);
    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<DataBarSwatch, IBrush?>(nameof(GridBrush), Brushes.LightGray);
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<DataBarSwatch, IBrush?>(nameof(Background), Brushes.White);
    public static readonly StyledProperty<double> InsetOpacityProperty =
        AvaloniaProperty.Register<DataBarSwatch, double>(nameof(InsetOpacity), .12);
    public IBrush? OutlineBrush { get => GetValue(OutlineBrushProperty); set => SetValue(OutlineBrushProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public double InsetOpacity { get => GetValue(InsetOpacityProperty); set => SetValue(InsetOpacityProperty, value); }
    static DataBarSwatch() => AffectsRender<DataBarSwatch>(OutlineBrushProperty, GridBrushProperty, BackgroundProperty, InsetOpacityProperty);
    public override void Render(DrawingContext context)
    {
        var outer = new Rect(Bounds.Size);
        if (outer.Width <= 6 || outer.Height <= 6) return;
        context.DrawRectangle(Background, null, outer, 4, 4);
        var chart = outer.Deflate(header ? 1 : 2);
        var inside = header ? chart : chart.Deflate(1);
        var fill = Color.TryParse(color, out var parsed) ? parsed : Colors.Transparent;
        IBrush bar = gradient ? new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(fill, 0), new(Color.FromArgb(26, fill.R, fill.G, fill.B), 1) }
        } : new SolidColorBrush(fill);
        using (context.PushClip(new RoundedRect(chart, header ? 3 : 2)))
        {
            var height = (inside.Height - 3) / 4;
            for (var i = 0; i < BarWidths.Length; i++)
            {
                var y = inside.Y + i * (height + 1);
                if (i > 0) context.DrawRectangle(GridBrush, null, new Rect(inside.X, y - 1, inside.Width, 1));
                context.DrawRectangle(bar, null, new Rect(inside.X, y, inside.Width * BarWidths[i], height));
            }
            if (!header) context.DrawRectangle(null, new Pen(GridBrush, 1), chart.Deflate(.5), 1.5, 1.5);
        }
        using (context.PushOpacity(InsetOpacity))
            context.DrawRectangle(null, new Pen(OutlineBrush, 1), outer.Deflate(.5), 3.5, 3.5);
    }
}
