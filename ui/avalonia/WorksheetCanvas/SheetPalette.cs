using System.Text.Json;
using Avalonia.Media;
using Avalonia.Styling;

namespace CyanCalc.Controls;

// Keep upstream assets intact; CyanCalc owns the UI color overrides.
// Workbook/document themes and explicitly formatted cell colors are independent.
internal sealed class SheetPalette
{
    private static readonly SheetPalette Light = new(false);
    private static readonly SheetPalette Dark = new(true);
    private readonly Dictionary<string, string> _values;

    private SheetPalette(bool isDark)
    {
        IsDark = isDark;
        var assembly = typeof(SheetPalette).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".workbook-ui-assets.json", StringComparison.Ordinal)))!;
        using var data = JsonDocument.Parse(stream);
        var values = data.RootElement.GetProperty("themes").GetProperty(isDark ? "dark" : "light");
        _values = values.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString()!);
        using var colors = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".cyancalc-ui-palette.json", StringComparison.Ordinal)))!;
        using var palette = JsonDocument.Parse(colors);
        foreach (var item in palette.RootElement.GetProperty(isDark ? "dark" : "light").EnumerateObject())
            _values[item.Name] = item.Value.GetString()!;
        string Value(string name) => _values["--palette-" + name];
        IBrush Brush(string name) => new SolidColorBrush(Color.Parse(Value(name)));
        var accent = Color.Parse(Value("sheet-outline-color"));
        Accent = new SolidColorBrush(accent);
        AccentText = Brush("primary-contrast-text");
        Selection = Brush("sheet-outline-background-color");
        Background = Brush("sheet-background");
        Chrome = Brush("common-white");
        Input = Brush("input-background");
        Grid = Brush("sheet-grid-color");
        Separator = Brush("sheet-grid-separator-color");
        Header = Brush("sheet-header-background");
        HeaderCorner = Brush("sheet-header-corner-background");
        HeaderSelected = Brush("sheet-header-selected-background");
        HeaderBorder = Brush("sheet-header-border-color");
        HeaderText = Brush("sheet-header-text-color");
        HeaderSelectedText = Brush("sheet-header-selected-color");
        DefaultText = Brush("sheet-default-text-color");
        Surface = Brush("surface-elevated");
        SurfaceBorder = Brush("surface-elevated-border");
        BorderPreview = Value("grey-900");
    }

    internal bool IsDark { get; }
    internal IBrush Token(string name) => Avalonia.Media.Brush.Parse(_values["--palette-" + name]);
    internal IBrush Accent { get; }
    internal IBrush AccentText { get; }
    internal IBrush Selection { get; }
    internal IBrush Background { get; }
    internal IBrush Chrome { get; }
    internal IBrush Input { get; }
    internal IBrush Grid { get; }
    internal IBrush Separator { get; }
    internal IBrush Header { get; }
    internal IBrush HeaderCorner { get; }
    internal IBrush HeaderSelected { get; }
    internal IBrush HeaderBorder { get; }
    internal IBrush HeaderText { get; }
    internal IBrush HeaderSelectedText { get; }
    internal IBrush DefaultText { get; }
    internal IBrush Surface { get; }
    internal IBrush SurfaceBorder { get; }
    internal string BorderPreview { get; }
    internal IBrush FilledCellText => Light.DefaultText;

    internal static SheetPalette ForLight() => Light;
    internal static SheetPalette For(ThemeVariant actual) => actual == ThemeVariant.Dark ? Dark : Light;
}
