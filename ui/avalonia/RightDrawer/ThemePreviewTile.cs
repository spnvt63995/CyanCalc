using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CyanCalc.Controls;

namespace CyanCalc.Controls.Preview;

// ThemePreview.tsx: background/text sample and six equal accent sectors.
public sealed class ThemePreviewTile : Button
{
    protected override global::Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => MacOSButtonAutomationPeer.Create(this);
    private readonly WorkbookText _text;
    private readonly Button _edit;
    private Action? _customize;
    public string ThemeName { get; }
    public bool IsCurrentTheme { get; private set; }

    internal ThemePreviewTile(JsonElement theme, bool current, string headingFont, string bodyFont, WorkbookText? text = null)
    {
        _text = text ?? new WorkbookText();
        ThemeName = theme.GetProperty("name").GetString()!;
        HorizontalAlignment = HorizontalAlignment.Stretch; HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Classes.Add("workbook-flat"); Padding = new Thickness(12, 8); Height = 68; MinWidth = 0; CornerRadius = new CornerRadius(0);
        AutomationProperties.SetName(this, ThemeName);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        var sampleFrame = CreateSample(theme, compact: false);
        row.Children.Add(sampleFrame);
        var title = new TextBlock { Text = ThemeName, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1); row.Children.Add(title);
        _edit = _text.Key("themes.customize_theme").Describe(new WorkbookButton { Classes = { "workbook-flat" }, Content = new WorkbookIcon("PencilLine"), Padding = new Thickness(6), Width = 28, VerticalAlignment = VerticalAlignment.Center });
        _edit.Click += (_, e) => { e.Handled = true; _customize?.Invoke(); };
        Grid.SetColumn(_edit, 2); row.Children.Add(_edit); Content = row; SetCurrent(current);
    }
    internal static Control CreatePreview(JsonElement theme)
    {
        var preview = CreateSample(theme, compact: true);
        preview.IsHitTestVisible = false;
        return preview;
    }
    private static Border CreateSample(JsonElement theme, bool compact)
    {
        var sample = new Grid { ColumnDefinitions = new ColumnDefinitions("*,22"), ColumnSpacing = 4, Margin = new Thickness(4), ClipToBounds = true };
        var grid = new PreviewGrid(theme.GetProperty("dk1").GetString()!); Grid.SetColumnSpan(grid, 2); sample.Children.Add(grid);
        sample.Children.Add(new TextBlock { Text = "Aa", FontSize = 11, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = ConditionalRenderer.Brush(theme.GetProperty("dk1").GetString()) });
        var pie = new Viewbox { Child = new AccentPie(Enumerable.Range(1, 6).Select(i => theme.GetProperty("accent" + i).GetString()!).ToArray()), Width = 22, Height = 22 }; Grid.SetColumn(pie, 1); sample.Children.Add(pie);
        return new Border { Width = compact ? 52 : 56, Height = compact ? 36 : 40, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(31, 0, 0, 0)), Background = ConditionalRenderer.Brush(theme.GetProperty("lt1").GetString()), Child = sample };
    }
    internal void SetCustomize(Action customize, bool enabled) { _customize = customize; _edit.IsEnabled = enabled; SetCurrent(IsCurrentTheme); }
    internal void SetCurrent(bool current)
    {
        IsCurrentTheme = current; _edit.IsVisible = current && _customize is not null;
        BorderThickness = new Thickness(current ? 3 : 0, 0, 0, 1); Padding = new Thickness(current ? 20 : 12, 8, 12, 8);
        this.Bind(BorderBrushProperty, this.GetResourceObservable(current ? "Dialog-primary-main" : "Dialog-grey-200"));
        _text.Key(current ? "native.current_theme" : "themes.apply").Bind(this, AutomationProperties.HelpTextProperty);
    }
    private sealed class AccentPie(string[] colors) : Control
    {
        protected override Size MeasureOverride(Size availableSize) => new(28, 28);
        public override void Render(DrawingContext context)
        {
            for (var i = 0; i < 6; i++)
            {
                Point PointAt(double angle) => new(14 + 13 * Math.Cos((angle - 90) * Math.PI / 180), 14 + 13 * Math.Sin((angle - 90) * Math.PI / 180));
                var start = PointAt(i * 60); var end = PointAt((i + 1) * 60);
                var geometry = Geometry.Parse(FormattableString.Invariant($"M14,14 L{start.X},{start.Y} A13,13,0,0,1,{end.X},{end.Y} Z"));
                context.DrawGeometry(ConditionalRenderer.Brush(colors[i]), null, geometry);
            }
        }
    }
    private sealed class PreviewGrid(string textColor) : Control
    {
        public override void Render(DrawingContext context)
        {
            if (!Color.TryParse(textColor, out var color)) return;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(26, color.R, color.G, color.B)), 1);
            context.DrawRectangle(null, pen, new Rect(Bounds.Size));
            foreach (var x in new[] { .33, .67 }) context.DrawLine(pen, new Point(Bounds.Width * x, 0), new Point(Bounds.Width * x, Bounds.Height));
            foreach (var y in new[] { .3, .7 }) context.DrawLine(pen, new Point(0, Bounds.Height * y), new Point(Bounds.Width, Bounds.Height * y));
        }
    }
}
