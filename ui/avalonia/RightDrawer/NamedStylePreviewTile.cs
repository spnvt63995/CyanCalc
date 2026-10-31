using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CyanCalc;
using CyanCalc.Controls;

namespace CyanCalc.Controls.Preview;

public sealed class NamedStylePreviewTile : Button
{
    protected override global::Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => MacOSButtonAutomationPeer.Create(this);
    private readonly WorkbookText _text;
    public string StyleName { get; }
    public bool MatchesActiveCell { get; private set; }
    public CellStyle PreviewStyle { get; private set; } = new();

    internal NamedStylePreviewTile(string name, CellStyle style, bool current, bool custom, WorkbookText? text = null)
    {
        _text = text ?? new WorkbookText();
        StyleName = name;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Padding = new Thickness(0); MinWidth = MinHeight = 0;
        Classes.Add("workbook-flat"); Height = 32;
        Update(style, current, custom);
    }

    internal void Update(CellStyle style, bool current, bool custom)
    {
        if (Content is not null && style == PreviewStyle && current == MatchesActiveCell) return;
        PreviewStyle = style; MatchesActiveCell = current;
        var name = StyleName;
        // The web panel displays percentages for accent variants, a translated
        // accent label for the base tile, and literal names for all other styles.
        var accent = System.Text.RegularExpressions.Regex.Match(name,
            @"^(?:(\d+%) - )?accent([1-6])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var displayName = !custom && accent.Success
            ? accent.Groups[1].Success ? _text.Compose(_ => accent.Groups[1].Value)
                : _text.Key("named_styles.accent_" + accent.Groups[2].Value)
            : _text.Compose(_ => name);
        displayName.Bind(this, AutomationProperties.NameProperty);
        var status = _text.Compose(t => $"{t[custom ? "named_styles.section_custom" : "named_styles.section_theme"]} · {t["named_styles.format_label"]}: {style.NumFmt}" + (current ? " · " + t["native.matches_cell"] : ""));
        status.Bind(this, AutomationProperties.HelpTextProperty);
        _text.Compose(_ => displayName.Value + "\n" + status.Value).Bind(this, ToolTip.TipProperty);
        Content = CreateSample(displayName, style, compact: false, frame: custom || !accent.Success);
    }

    internal static Control CreatePreview(string sample, CellStyle style, WorkbookText text, bool compact = true)
    {
        var preview = CreateSample(text.Compose(_ => sample), style, compact, frame: true);
        preview.Height = 32;
        preview.IsHitTestVisible = false;
        return preview;
    }

    private static Control CreateSample(LocalizedText displayName, CellStyle style, bool compact, bool frame)
    {
        var text = new TextBlock
        {
            Text = displayName.Value, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = ConditionalRenderer.Brush(style.Font.Color, "#000000"),
            // Upstream previews use UI typography, independent of the stored cell font.
            FontFamily = WorkbookDialog.Font, FontSize = 12,
            FontWeight = style.Font.B ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = style.Font.I ? FontStyle.Italic : FontStyle.Normal,
            HorizontalAlignment = style.Alignment?.Horizontal switch { "left" => HorizontalAlignment.Left, "center" => HorizontalAlignment.Center, "right" => HorizontalAlignment.Right, _ => compact ? HorizontalAlignment.Center : HorizontalAlignment.Left },
            VerticalAlignment = style.Alignment?.Vertical switch { "top" => VerticalAlignment.Top, "bottom" => VerticalAlignment.Bottom, _ => VerticalAlignment.Center }
        };
        if (string.IsNullOrEmpty(style.Font.Color))
        {
            if (compact || string.IsNullOrEmpty(style.Fill.Color)) text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Dialog-common-black"));
            else text.Foreground = ConditionalRenderer.Brush("#2E414D"); // Upstream defaultThemeVariables sheet text on an explicit fill.
        }
        displayName.Bind(text, TextBlock.TextProperty);
        var decorations = new TextDecorationCollection();
        if (style.Font.U) decorations.Add(TextDecorations.Underline[0]);
        if (style.Font.Strike) decorations.Add(TextDecorations.Strikethrough[0]);
        text.TextDecorations = decorations;
        var sample = new Grid { ClipToBounds = true };
        var background = new Border { Child = text, Padding = compact ? new Thickness(4) : new Thickness(6, 0), Background = ConditionalRenderer.Brush(style.Fill.Color, "#FFFFFF"), CornerRadius = new CornerRadius(compact ? 4 : 0) };
        var edges = style.Border;
        if (edges.Top is not null || edges.Right is not null || edges.Bottom is not null || edges.Left is not null)
            background.BorderThickness = new Thickness(StyleSample.BorderWidth(edges.Left), StyleSample.BorderWidth(edges.Top), StyleSample.BorderWidth(edges.Right), StyleSample.BorderWidth(edges.Bottom));
        if (string.IsNullOrEmpty(style.Fill.Color)) background.Bind(Border.BackgroundProperty, background.GetResourceObservable("Dialog-common-white"));
        sample.Children.Add(background);
        var borders = new StyleSample { Style = style, Frame = frame, IsHitTestVisible = false };
        borders.Bind(StyleSample.FaintBrushProperty, borders.GetResourceObservable("Dialog-common-black")); sample.Children.Add(borders);
        return sample;
    }

    private sealed class StyleSample : Control
    {
        public static readonly StyledProperty<IBrush?> FaintBrushProperty = AvaloniaProperty.Register<StyleSample, IBrush?>(nameof(FaintBrush));
        public IBrush? FaintBrush { get => GetValue(FaintBrushProperty); set => SetValue(FaintBrushProperty, value); }
        public required CellStyle Style { get; init; }
        public bool Frame { get; init; }
        static StyleSample() => AffectsRender<StyleSample>(FaintBrushProperty);
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var border = Style.Border;
            if (!Frame && border.Top is null && border.Right is null && border.Bottom is null && border.Left is null) return;
            var r = new Rect(Bounds.Size).Deflate(new Thickness(BorderWidth(border.Left) / 2, BorderWidth(border.Top) / 2, BorderWidth(border.Right) / 2, BorderWidth(border.Bottom) / 2));
            Edge(border.Top, r.TopLeft, r.TopRight); Edge(border.Right, r.TopRight, r.BottomRight);
            Edge(border.Bottom, r.BottomLeft, r.BottomRight); Edge(border.Left, r.TopLeft, r.BottomLeft);
            void Edge(BorderItem? item, Point from, Point to)
            {
                if (item is not null) ConditionalRenderer.Border(context, item, from, to);
                else { using var opacity = context.PushOpacity(0.12); context.DrawLine(new Pen(FaintBrush, 1), from, to); }
            }
        }
        internal static double BorderWidth(BorderItem? edge) => edge?.Style.ToLowerInvariant() switch
        { "double" or "thick" => 3, "medium" or "mediumdashed" or "mediumdashdot" or "mediumdashdotdot" => 2, _ => 1 };
    }
}
