using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CyanCalc.Controls.Preview;

namespace CyanCalc.Controls;

// EditNamedStyle.tsx / FormatStylePicker.tsx. Colors retain the engine Color union.
internal sealed class StyleEditor : StackPanel
{
    private readonly WorkbookText _text;
    private readonly JsonObject _original;
    private readonly bool _differential;
    private readonly UserModel? _model;
    private readonly PanelColorButton _fontColor, _fill;
    private readonly ToggleButton _bold, _italic, _underline, _strike;
    private readonly TextBox? _number;
    private readonly PanelColorButton? _borderColor;
    private readonly ComboBox? _format;
    private string _horizontal = "general", _vertical = "bottom", _borderStyle = "thin";
    private readonly Dictionary<string, ToggleButton> _sides = new();
    private readonly Dictionary<string, ToggleSwitch> _includes = new();
    private readonly ContentControl _preview = new() { Name = "StylePreview" };
    private readonly string[] _formats = [];
    private readonly TextBlock? _customFormatError;
    private bool _customFormatTouched;
    private string _numberFormat = "general";
    internal sealed record Draft(JsonObject Style, JsonElement Includes, string CustomFormat, bool CustomFormatTouched, string BorderStyle, JsonNode? BorderColor);
    internal Draft CaptureDraft() => new(Value(false), JsonSerializer.SerializeToElement(Includes), _number?.Text ?? "", _customFormatTouched, _borderStyle, _borderColor?.Value?.DeepClone());
    private bool _ready;
    private bool _detached;
    internal event Action? Changed;
    internal bool CanApply => _differential || !Includes["number_format"] || _format!.SelectedIndex != 8 || !string.IsNullOrWhiteSpace(_number!.Text);
    internal Dictionary<string, bool> Includes => _includes.ToDictionary(x => x.Key, x => x.Value.IsChecked == true);
    internal Control TakePreview() { Children.Remove(_preview); return _preview; }

    public StyleEditor(JsonObject original, bool differential, UserModel? model = null, WorkbookText? text = null, WorkbookView? owner = null, JsonElement includes = default, Draft? draft = null)
    {
        _text = text ?? new WorkbookText(); _model = model;
        AttachedToVisualTree += (_, _) => _detached = false;
        DetachedFromVisualTree += (_, _) => _detached = true;
        _original = (JsonObject)original.DeepClone(); _differential = differential; Spacing = differential ? 12 : 0;
        if (!differential) { _preview.Width = 40; _preview.Height = 32; _preview.HorizontalAlignment = HorizontalAlignment.Center; }
        Children.Add(_preview);
        if (!differential)
        {
            var settings = model?.GetFmtSettings();
            string Setting(string key, string fallback) => settings?.GetProperty(key).GetString() ?? fallback;
            _formats = ["general", Setting("number_fmt", "#,##0.00"), "0.00%", "\"€\"#,##0.00", "\"$\"#,##0.00", "\"£\"#,##0.00", Setting("short_date", "m/d/yy"), Setting("long_date", "mmmm d, yyyy")];
            var numberGroup = Section("number_format", "number", true);
            string[] keys = ["auto", "number", "percentage", "currency_eur", "currency_usd", "currency_gbp", "date_short", "date_long", "custom"];
            var initial = Read("num_fmt")?.GetValue<string>() ?? "general";
            _format = WorkbookView.Choice(numberGroup, _text.Key("named_styles.format_label"), keys, keys[Math.Max(0, Array.IndexOf(_formats, initial))], key => _text.Key("toolbar.format_menu." + key));
            _format.Name = "StyleNumberFormat";
            _number = new TextBox { Name = "StyleCustomFormat", Text = draft?.CustomFormat ?? (_formats.Contains(initial) ? "" : initial), IsVisible = !_formats.Contains(initial) };
            _text.Key("named_styles.custom_format_placeholder").Bind(_number, TextBox.WatermarkProperty);
            numberGroup.Children.Add(_number);
            _format.SelectedIndex = _formats.Contains(initial) ? Array.IndexOf(_formats, initial) : 8;
            _numberFormat = initial;
            var syncingFormat = false;
            void SyncFormat()
            {
                syncingFormat = true;
                var index = Array.IndexOf(_formats, _numberFormat);
                _format.SelectedIndex = index < 0 ? 8 : index;
                _number.IsVisible = index < 0;
                syncingFormat = false;
            }
            _format.SelectionChanged += (_, _) =>
            {
                if (syncingFormat) return;
                if (_format.SelectedIndex == 8) _customFormatTouched = false;
                _numberFormat = _format.SelectedIndex == 8 ? _number.Text ?? "" : _formats[_format.SelectedIndex];
                SyncFormat(); Update();
            };
            _number.PropertyChanged += (_, e) =>
            {
                if (e.Property != TextBox.TextProperty || syncingFormat) return;
                _numberFormat = _number.Text ?? ""; SyncFormat(); Update();
            };
            _customFormatError = new TextBlock { Foreground = Brushes.Firebrick, IsVisible = false, TextWrapping = TextWrapping.Wrap };
            _text.Key("named_styles.custom_format_required").Bind(_customFormatError, TextBlock.TextProperty); numberGroup.Children.Add(_customFormatError);
            _customFormatTouched = draft?.CustomFormatTouched ?? false;
            _number.LostFocus += (_, _) => { _customFormatTouched = true; _customFormatError.IsVisible = !CanApply; };
        }
        var fontGroup = differential ? this : Section("font", "font", true);
        var fontRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var fontFlags = new StackPanel { Orientation = Orientation.Horizontal };
        _bold = Flag(fontFlags, "toolbar.bold", "Bold", "font.b");
        _italic = Flag(fontFlags, "toolbar.italic", "Italic", "font.i");
        _underline = Flag(fontFlags, "toolbar.underline", "Underline", "font.u");
        _strike = Flag(fontFlags, "toolbar.strike_through", "Strikethrough", "font.strike");
        JsonNode? InitialColor(string path) => !differential && model is not null ? JsonValue.Create(model.ResolveColor(JsonSerializer.SerializeToElement(Read(path)))) : Read(path);
        _fontColor = new PanelColorButton(owner, model, _text.Key("toolbar.font_color"), InitialColor("font.color"), "StyleFontColor"); fontRow.Children.Add(_fontColor);
        fontRow.Children.Insert(0, differential ? fontFlags : ToggleGroup(fontFlags));
        fontGroup.Children.Add(differential ? fontRow : Subrow("font_style_label", fontRow));
        var fillGroup = differential ? this : Section("fill", "fill", true);
        _fill = new PanelColorButton(owner, model, _text.Key("toolbar.fill_color"), InitialColor("fill.color"), "StyleFillColor"); fillGroup.Children.Add(differential ? _fill : Subrow("background_color_label", _fill));
        _fontColor.Changed += Update; _fill.Changed += Update;
        if (differential)
        {
            Children.Remove(_preview); Children.Remove(fontRow); Children.Remove(_fill);
            fontRow.Spacing = 2; fontRow.Children.Add(_fill); _fontColor.UseIcon("Type"); _fill.UseIcon("PaintBucket");
            var clear = WorkbookView.Button(_text.Key("toolbar.clear_formatting"), () =>
            { _bold.IsChecked = _italic.IsChecked = _underline.IsChecked = _strike.IsChecked = false; _fontColor.Value = _fill.Value = null; });
            clear.Content = new WorkbookIcon("RemoveFormatting"); clear.Width = 28; clear.Padding = new Thickness(6);
            var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4, 2) }; toolbar.Children.Add(fontRow); Grid.SetColumn(clear, 1); toolbar.Children.Add(clear);
            var control = new StackPanel(); control.Children.Add(_preview); control.Children.Add(toolbar);
            var frame = new Border { Child = control, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
            frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Dialog-grey-300")); Children.Add(frame);
            var presetSection = new StackPanel { Spacing = 4 };
            presetSection.Children.Add(WorkbookView.Label(_text.Key("conditional_formatting.default_styles")));
            var presets = new UniformGrid { Rows = 1, Columns = 8, Margin = new Thickness(-2, 0) };
            foreach (var (font, fill) in new[] { ("#D21D21", "#FBE7E8"), ("#D21D21", ""), ("#8B6F00", "#FEF9E8"), ("#8B6F00", ""), ("#A5530A", "#FCE7D4"), ("#A5530A", ""), ("#008121", "#F1F6EA"), ("#008121", "") })
            {
                var button = new WorkbookButton { Classes = { "workbook-flat" }, Height = 32, Padding = new Thickness(0), Content = "Aa", Foreground = ConditionalRenderer.Brush(font), Background = ConditionalRenderer.Brush(fill, "#FFFFFF"), Margin = new Thickness(2, 0), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(31, 0, 0, 0)) };
                button.Click += (_, _) => { _bold.IsChecked = _italic.IsChecked = _underline.IsChecked = _strike.IsChecked = false; _fontColor.Value = JsonValue.Create(font); _fill.Value = JsonValue.Create(fill); };
                presets.Children.Add(button);
            }
            presetSection.Children.Add(presets); Children.Add(presetSection);
        }
        else
        {
            var alignment = Section("alignment", "alignment", false);
            _horizontal = Read("alignment.horizontal")?.GetValue<string>() ?? "general";
            _vertical = Read("alignment.vertical")?.GetValue<string>() ?? "bottom";
            alignment.Children.Add(Subrow("horizontal_align_label", Choices(
                [("left", "AlignLeft", "toolbar.align_left"), ("center", "AlignCenter", "toolbar.align_center"), ("right", "AlignRight", "toolbar.align_right")],
                () => _horizontal, value => _horizontal = value, "general")));
            alignment.Children.Add(Subrow("vertical_align_label", Choices(
                [("top", "ArrowUpToLine", "toolbar.vertical_align_top"), ("center", "ArrowMiddleFromLine", "toolbar.vertical_align_middle"), ("bottom", "ArrowDownToLine", "toolbar.vertical_align_bottom")],
                () => _vertical, value => _vertical = value)));
            var borderGroup = Section("border", "border", false);
            JsonNode? first = null; var borders = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var side in new[] { "top", "right", "bottom", "left" })
            {
                var value = Read("border." + side); first ??= value;
                var box = new ToggleButton { Classes = { "workbook-toggle", "style-choice" }, Content = new WorkbookIcon("Border" + char.ToUpperInvariant(side[0]) + side[1..]), IsChecked = value?["style"] is not null };
                _text.Key("toolbar.borders." + side).Describe(box);
                _sides[side] = box; borders.Children.Add(box);
            }
            var all = new ToggleButton { Classes = { "workbook-toggle", "style-choice" }, Content = new WorkbookIcon("BorderOuter") };
            var none = new ToggleButton { Classes = { "workbook-toggle", "style-choice" }, Content = new WorkbookIcon("BorderNone") };
            _text.Key("toolbar.borders.all").Describe(all); _text.Key("toolbar.borders.clear").Describe(none);
            void RefreshSides()
            {
                all.IsChecked = _sides.Values.All(x => x.IsChecked == true);
                none.IsChecked = _sides.Values.All(x => x.IsChecked != true); Update();
            }
            foreach (var side in _sides.Values) side.IsCheckedChanged += (_, _) => RefreshSides();
            all.Click += (_, _) => { var enabled = all.IsChecked == true; foreach (var side in _sides.Values) side.IsChecked = enabled; RefreshSides(); };
            none.Click += (_, _) => { foreach (var side in _sides.Values) side.IsChecked = false; RefreshSides(); };
            RefreshSides();
            var borderControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            borderControls.Children.Add(ToggleGroup(new StackPanel { Orientation = Orientation.Horizontal, Children = { all, none } }));
            borderControls.Children.Add(ToggleGroup(borders)); borderGroup.Children.Add(Subrow("border_label", borderControls));
            _borderStyle = draft?.BorderStyle ?? first?["style"]?.GetValue<string>() ?? "thin";
            _borderColor = new PanelColorButton(owner, model, _text.Key("toolbar.borders.color"), draft is not null ? draft.BorderColor?.DeepClone() : first?["color"] ?? JsonValue.Create(""), "StyleBorderColor");
            var line = new WorkbookButton { Name = "StyleBorderLine", Classes = { "workbook-flat", "panel-swatch" }, Width = 88, Height = 32,
                Padding = new Thickness(8), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            _text.Key("toolbar.borders.style").Describe(line);
            void RefreshLine()
            {
                var color = Resolve(_borderColor.Value);
                line.Content = new WorkbookView.BorderLinePreview(_borderStyle) { Height = 16, Stroke = string.IsNullOrEmpty(color) ? SheetPalette.For(ActualThemeVariant).DefaultText : ConditionalRenderer.Brush(color) };
            }
            RefreshLine();
            line.Click += (_, _) => owner?.ShowPanelLineStyle(line, _borderStyle, value => { _borderStyle = value; RefreshLine(); Update(); });
            _borderColor.Changed += () => { RefreshLine(); Update(); };
            borderGroup.Children.Add(Subrow("line_style_label", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { line, _borderColor } }));

        }
        _ready = true; Update();

        StackPanel Section(string key, string label, bool fallback)
        {
            var toggle = new ToggleSwitch { Classes = { "workbook-switch" }, FontWeight = FontWeight.ExtraBold, MinHeight = 16, Name = "StyleInclude_" + key, OnContent = "", OffContent = "", IsChecked = includes.ValueKind == JsonValueKind.Object && includes.TryGetProperty(key, out var enabled) ? enabled.GetBoolean() : fallback };
            _text.Key("named_styles." + label + "_label").Bind(toggle, ContentControl.ContentProperty); _includes[key] = toggle;
            var body = new StackPanel { Spacing = 12, IsVisible = toggle.IsChecked == true };
            var section = new StackPanel { Spacing = 12 }; section.Children.Add(toggle); section.Children.Add(body);
            var frame = new Border { Child = section, Padding = new Thickness(12, 16), Margin = new Thickness(-12, 0), BorderThickness = new Thickness(0, 0, 0, 1) };
            frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Dialog-grey-200")); Children.Add(frame);
            toggle.IsCheckedChanged += (_, _) => { body.IsVisible = toggle.IsChecked == true; owner?.ClosePanelColor(); Update(); };
            return body;
        }
    }
    private Grid Subrow(string key, Control control)
    {
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        var label = _text.Key("named_styles." + key).Block(); label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label); Grid.SetColumn(control, 1); row.Children.Add(control); return row;
    }
    private static Control ToggleGroup(StackPanel buttons)
    {
        var frame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), IsHitTestVisible = false };
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Dialog-grey-50"));
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Dialog-grey-300")); return new Grid { Children = { frame, buttons } };
    }
    private Control Choices((string Value, string Icon, string Label)[] options, Func<string> get, Action<string> set, string? unpressed = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal }; var buttons = new Dictionary<string, ToggleButton>();
        foreach (var option in options)
        {
            var button = new ToggleButton { Classes = { "workbook-toggle", "style-choice" }, Content = new WorkbookIcon(option.Icon), IsChecked = get() == option.Value };
            _text.Key(option.Label).Describe(button);
            button.Click += (_, _) =>
            {
                set(unpressed is not null && button.IsChecked != true ? unpressed : option.Value);
                foreach (var item in buttons) item.Value.IsChecked = item.Key == get(); Update();
            };
            buttons.Add(option.Value, button); row.Children.Add(button);
        }
        return ToggleGroup(row);
    }
    private JsonNode? Read(string path) => path.Split('.').Aggregate((JsonNode?)_original, (value, key) => value?[key]);
    private ToggleButton Flag(Panel parent, string label, string icon, string path)
    {
        var box = new ToggleButton { Classes = { "workbook-toggle" }, Content = new WorkbookIcon(icon), IsChecked = Read(path)?.GetValue<bool>() ?? false };
        if (!_differential) box.Classes.Add("style-choice");
        _text.Key(label).Describe(box); box.IsCheckedChanged += (_, _) => Update(); parent.Children.Add(box); return box;
    }
    private void Update()
    {
        if (!_ready || _detached) return;
        if (_customFormatError is { } error) error.IsVisible = _customFormatTouched && !CanApply;
        var style = Value(false);
        if (_differential)
        {
            var label = new TextBlock { Text = "AaBbCc", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = _bold.IsChecked == true ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = _italic.IsChecked == true ? FontStyle.Italic : FontStyle.Normal,
                Foreground = ConditionalRenderer.Brush(Resolve(_fontColor.Value), "#000000") };
            if (string.IsNullOrEmpty(Resolve(_fontColor.Value))) label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("Dialog-sheet-default-text-color"));
            var decorations = new TextDecorationCollection();
            if (_underline.IsChecked == true) decorations.Add(TextDecorations.Underline[0]);
            if (_strike.IsChecked == true) decorations.Add(TextDecorations.Strikethrough[0]); label.TextDecorations = decorations;
            var preview = new Border { Child = label, Height = 33, Padding = new Thickness(12, 0), Background = ConditionalRenderer.Brush(Resolve(_fill.Value), "Transparent"), BorderThickness = new Thickness(0, 0, 0, 1) };
            preview.Bind(Border.BorderBrushProperty, preview.GetResourceObservable("Dialog-grey-200")); _preview.Content = preview;
        }
        else if (_model is not null)
        {
            var preview = (JsonObject)style.DeepClone(); var masks = Includes;
            var normal = _model.Query("builtin_styles").EnumerateArray().First(x => x[0].GetString()!.Equals("Normal", StringComparison.OrdinalIgnoreCase))[1];
            foreach (var key in new[] { "font", "fill", "border", "alignment" })
                if (!masks[key]) preview[key] = normal.TryGetProperty(key, out var normalValue) ? JsonNode.Parse(normalValue.GetRawText()) : null;
            var format = masks["number_format"] ? style["num_fmt"]!.GetValue<string>() : "general";
            var index = Array.IndexOf(_formats, format);
            var sample = index switch { 0 => "Aa", 1 => "#", 2 => "%", 3 => "€", 4 => "$", 5 => "£", 6 or 7 => "31/12", _ => "C" };
            _preview.Content = new NamedStylePreviewTile(sample, StylePreviewResolver.Resolve(_model, JsonSerializer.SerializeToElement(preview)), false, true, _text, compact: true) { IsHitTestVisible = false };
        }
        Changed?.Invoke();
    }
    private string Resolve(JsonNode? value) => _model?.ResolveColor(JsonSerializer.SerializeToElement(value)) ?? (value is JsonValue ? value.ToString() : "");
    public JsonObject Value(bool validate = true)
    {
        JsonNode? Color(PanelColorButton field) => field.Value is JsonValue v && v.TryGetValue<string>(out var s) && s.Length == 0 ? null : field.Value;
        if (_differential)
        {
            var result = new JsonObject(); var font = new JsonObject();
            if (_bold.IsChecked == true) font["b"] = true;
            if (_italic.IsChecked == true) font["i"] = true;
            if (_underline.IsChecked == true) font["u"] = true;
            if (_strike.IsChecked == true) font["strike"] = true;
            if (Color(_fontColor) is { } color) font["color"] = color;
            if (font.Count > 0) result["font"] = font;
            if (Color(_fill) is { } fill) result["fill"] = new JsonObject { ["color"] = fill };
            return result;
        }
        if (validate && !CanApply) throw new InvalidOperationException(_text["named_styles.custom_format_required"]);
        var style = (JsonObject)_original.DeepClone();
        style["num_fmt"] = _numberFormat;
        var fontStyle = style["font"]!.AsObject();
        fontStyle["b"] = _bold.IsChecked == true; fontStyle["i"] = _italic.IsChecked == true;
        fontStyle["u"] = _underline.IsChecked == true; fontStyle["strike"] = _strike.IsChecked == true;
        fontStyle["color"] = Color(_fontColor); style["fill"]!["color"] = Color(_fill);
        style["alignment"] = new JsonObject { ["horizontal"] = _horizontal, ["vertical"] = _vertical, ["wrap_text"] = Read("alignment.wrap_text")?.GetValue<bool>() ?? false };
        var borders = style["border"]!.AsObject();
        foreach (var (side, box) in _sides)
            borders[side] = box.IsChecked == true ? new JsonObject { ["style"] = _borderStyle, ["color"] = Color(_borderColor!) } : null;
        return style;
    }
}

public sealed partial class WorkbookView
{
    internal void ClosePanelColor() => CloseToolbarPopups();
}
