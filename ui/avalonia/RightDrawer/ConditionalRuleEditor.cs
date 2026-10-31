using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyanCalc.Controls;

// Port of the four EditRule tabs. Values remain engine CfRuleInput; only the
// controls, native flyout lifetime and JSON representation differ from the web.
internal sealed class ConditionalRuleEditor : StackPanel
{
    internal static readonly string[] ClassicTypes = ["CellIs", "Text", "TimePeriod", "Formula", "DuplicateValues", "UniqueValues", "Blanks", "NotBlanks", "Errors", "NoErrors"];
    internal static readonly string[] Types = [.. ClassicTypes, "ColorScale", "DataBar", "IconSet", "IconRating"];
    private readonly WorkbookText _text;
    private readonly UserModel? _model;
    private readonly WorkbookView? _owner;
    private readonly Func<string> _selection;
    private readonly Func<string, string>? _resolve;
    private readonly JsonObject _initial;
    private JsonObject _rule;
    private JsonObject _sharedFormat;
    private readonly StackPanel _fields = new() { Spacing = 8 };
    private readonly ContentControl _preview = new();
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.WrapWithOverflow, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly List<Action> _save = new();
    private readonly List<TextBox> _draftInputs = new();
    private readonly List<ComboBox> _draftChoices = new();
    internal sealed record Draft(JsonObject Initial, JsonObject Rule, JsonObject SharedFormat, JsonArray? IconRows,
        string RatingIcon, JsonNode? RatingColor, int PresetCount, string? SelectedIconPreset, string SelectedScalePreset, string? DataBarColor, string?[] Inputs, string?[] Choices);
    internal Draft CaptureDraft()
    {
        if (_format is not null) _rule["format"] = _format.Value(false);
        return new((JsonObject)_initial.DeepClone(), (JsonObject)_rule.DeepClone(), (JsonObject)_sharedFormat.DeepClone(),
            (JsonArray?)_iconRows?.DeepClone(), _ratingIcon, _ratingColor?.DeepClone(), _presetCount, _selectedIconPreset, _selectedScalePreset, _dataBarColor,
            _draftInputs.Select(input => input.Text).ToArray(), _draftChoices.Select(choice => choice.SelectedItem as string).ToArray());
    }
    private readonly List<Func<bool>> _valid = new();
    private StyleEditor? _format;
    private bool _building;
    private bool _detached;
    private JsonArray? _iconRows;
    private string _ratingIcon = "Star";
    private JsonNode? _ratingColor = JsonValue.Create("#FFD700");
    private int _presetCount = 2;
    private string? _selectedIconPreset;
    private string _selectedScalePreset = "r-y-g";
    private string? _dataBarColor;
    internal event Action? Changed;
    internal bool CanApply => _valid.All(test => test());
    internal bool HasSerializableRule => Type != "IconSet" || _rule["thresholds"]!.AsArray().Count == _presetCount;
    private string Type => _rule["type"]!.GetValue<string>();
    private static int Group(string type) => type switch { "ColorScale" => 1, "DataBar" => 2, "IconSet" or "IconRating" => 3, _ => 0 };
    internal ConditionalRuleEditor(JsonObject? rule, WorkbookText? text = null, UserModel? model = null, WorkbookView? owner = null, Func<string>? selection = null, Func<string, string>? resolve = null, Control? rangeControl = null, Draft? draft = null)
    {
        _text = text ?? new WorkbookText(); _model = model; _owner = owner; _selection = selection ?? (() => ""); _resolve = resolve;
        AttachedToVisualTree += (_, _) => _detached = false;
        DetachedFromVisualTree += (_, _) => _detached = true;
        _initial = (JsonObject)(draft?.Initial ?? rule ?? DefaultRule("CellIs")).DeepClone();
        _rule = (JsonObject)(draft?.Rule ?? _initial).DeepClone();
        _sharedFormat = (JsonObject)(draft?.SharedFormat ?? _rule["format"] ?? DefaultRule("CellIs")["format"]!).DeepClone(); Spacing = 10;
        if (draft is not null)
        {
            _iconRows = (JsonArray?)draft.IconRows?.DeepClone(); _ratingIcon = draft.RatingIcon;
            _ratingColor = draft.RatingColor?.DeepClone(); _presetCount = draft.PresetCount; _selectedIconPreset = draft.SelectedIconPreset; _selectedScalePreset = draft.SelectedScalePreset; _dataBarColor = draft.DataBarColor;
        }
        _preview.Width = 40; _preview.Height = 32; _preview.HorizontalAlignment = HorizontalAlignment.Center;
        _description.FontSize = 14; _description.LineHeight = 21; _description.FontWeight = FontWeight.SemiBold; _description.TextAlignment = TextAlignment.Center;
        Children.Add(WorkbookView.DrawerPreview(_preview, _description));
        var tabs = new ConditionalRuleTabs { Classes = { "rule-tabs" }, Name = "ConditionalTabs", Margin = new Avalonia.Thickness(-12, -10, -12, 0), SelectedIndex = Group(Type), ItemsSource = new[] { "classic", "color_scale", "data_bars", "icon_sets" }.Select(key =>
        {
            var label = _text.Key("conditional_formatting.tab_" + key).Block(); label.FontSize = 12; label.LineHeight = 14.4;
            return new TabItem { Header = label };
        }).ToArray() };
        Children.Add(tabs);
        if (rangeControl is not null)
        {
            var section = new Border { Child = rangeControl, Padding = new Avalonia.Thickness(12, 16), Margin = new Avalonia.Thickness(-12, -10, -12, 0), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1) };
            section.Bind(Border.BorderBrushProperty, section.GetResourceObservable("Dialog-grey-300")); Children.Add(section);
        }
        Children.Add(_fields);
        tabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, tabs) || tabs.SelectedIndex == Group(Type)) return;
            if (_format is not null) _sharedFormat = _format.Value();
            var type = new[] { "CellIs", "ColorScale", "DataBar", "IconSet" }[tabs.SelectedIndex];
            _rule = Group(_initial["type"]!.GetValue<string>()) == tabs.SelectedIndex ? (JsonObject)_initial.DeepClone() : DefaultRule(type);
            if (Group(Type) == 0) _rule["format"] = _sharedFormat.DeepClone();
            _iconRows = null; _selectedScalePreset = "r-y-g"; _dataBarColor = null;
            BuildFields();
        };
        BuildFields();
        if (draft is not null)
        {
            // Keep incomplete numeric text too; serializing a CfRule would normalize
            // it to zero or reject it before the user has finished typing.
            _building = true;
            foreach (var (field, value) in _draftChoices.Zip(draft.Choices)) field.SelectedItem = value;
            foreach (var (field, value) in _draftInputs.Zip(draft.Inputs)) field.Text = value;
            _building = false; Notify();
        }
    }
    internal static JsonObject DefaultRule(string type)
    {
        var result = new JsonObject { ["type"] = type };
        if (ClassicTypes.Contains(type))
        {
            result["format"] = new JsonObject { ["font"] = new JsonObject { ["color"] = "#D21D21" }, ["fill"] = new JsonObject { ["color"] = "#FBE7E8" } };
            result["stop_if_true"] = false;
        }
        switch (type)
        {
            case "CellIs": result["operator"] = "Between"; result["formula"] = ""; result["formula2"] = ""; break;
            case "Text": result["operator"] = "Contains"; result["value"] = ""; break;
            case "Formula": result["formula"] = ""; break;
            case "TimePeriod": result["time_period"] = "Between"; result["date1"] = ""; result["date2"] = ""; break;
            case "DataBar": result["min"] = null; result["max"] = null; result["positive_color"] = "#6AAB42"; result["negative_color"] = "#E05C53"; result["is_gradient"] = false; result["show_value"] = true; break;
            case "ColorScale": result["thresholds"] = new JsonArray(new JsonObject { ["cfvo"] = "Min", ["color"] = "#ec5753" }, new JsonObject { ["cfvo"] = new JsonObject { ["Percentile"] = 50 }, ["color"] = "#f8cd3d" }, new JsonObject { ["cfvo"] = "Max", ["color"] = "#8cb354" }); break;
            case "IconSet": result["show_value"] = true; return ConditionalPreset.All.Single(p => p.Id == "ind-2-arrows").Apply(result);
            case "IconRating": result["show_value"] = true; return ConditionalPreset.All.Single(p => p.Id == "rating-3-stars").Apply(result);
        }
        return result;
    }
    private void BuildFields()
    {
        _building = true; _fields.Spacing = 8; _fields.Margin = default; _fields.Children.Clear(); ClearFieldState();
        if (Group(Type) == 0) BuildClassic();
        else if (Type == "ColorScale") BuildScale();
        else if (Type == "DataBar") BuildDataBars();
        else BuildIcons();
        _building = false; Notify();
    }
    private void ClearFieldState()
    {
        _save.Clear(); _valid.Clear(); _draftInputs.Clear(); _draftChoices.Clear(); _format = null;
    }
    private void BuildClassic()
    {
        var title = WorkbookView.Label(_text.Key("conditional_formatting.format_rules")); title.FontSize = 14; title.FontWeight = FontWeight.SemiBold; _fields.Children.Add(title);
        var choice = WorkbookView.Choice(_fields, _text.Key("conditional_formatting.highlight_cells_if"), ClassicTypes, Type, v => WorkbookOptions.RuleType(_text, v), name: "ConditionalRuleType");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        var choicePanel = (Control)choice.Parent!;
        _fields.Children.Remove(choicePanel); row.Children.Add(choicePanel); _fields.Children.Add(row);
        var details = new StackPanel { Spacing = 8 }; _fields.Children.Add(details);
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedItem is not string type || type == Type) return;
            var old = Value(); var first = Get("formula", Get("value", Get("date1"))); var second = Get("formula2", Get("date2"));
            _rule = DefaultRule(type); _rule["format"] = old["format"]?.DeepClone(); _rule["stop_if_true"] = old["stop_if_true"]?.DeepClone();
            if (type is "CellIs" or "Formula") _rule["formula"] = first;
            if (type == "CellIs") _rule["formula2"] = second;
            if (type == "Text") _rule["value"] = first;
            if (type == "TimePeriod") { _rule["date1"] = first; _rule["date2"] = second; }
            // Keep the selector attached while its popup closes and restores focus.
            _building = true; details.Children.Clear();
            if (row.Children.Count > 1) row.Children.RemoveAt(1);
            ClearFieldState(); BuildClassicDetails(row, details);
            _building = false; Notify();
        };
        BuildClassicDetails(row, details);
    }
    private void BuildClassicDetails(Grid row, StackPanel details)
    {
        ComboBox? op = null;
        if (Type == "CellIs") op = Pick(details, "operator", ["Between", "NotBetween", "LessThan", "LessThanOrEqual", "GreaterThan", "GreaterThanOrEqual", "Equal", "NotEqual"]);
        if (Type == "Text") op = Pick(details, "operator", ["Contains", "DoesNotContain", "BeginsWith", "EndsWith", "Equals"]);
        if (Type == "TimePeriod") op = Pick(details, "time_period", ["Between", "NotBetween", "Yesterday", "Today", "Tomorrow", "Last7Days", "Next7Days", "LastWeek", "ThisWeek", "NextWeek", "LastMonth", "ThisMonth", "NextMonth", "LastYear", "ThisYear", "NextYear"]);
        if (op is not null)
        {
            var right = (Control)op.Parent!; details.Children.Remove(right);
            Grid.SetColumn(right, 1); right.VerticalAlignment = VerticalAlignment.Bottom; row.Children.Add(right);
        }
        Grid.SetColumnSpan(row.Children[0], op is null ? 2 : 1);
        if (Type is "CellIs" or "Text" or "TimePeriod" or "Formula")
        {
            var firstKey = Type == "Text" ? "value" : Type == "TimePeriod" ? "date1" : "formula";
            var first = TextField(details, _text.Key("conditional_formatting.rule_value_placeholder"), Get(firstKey), v => _rule[firstKey] = v, "ConditionalValue");
            TextBox? second = null;
            Grid? values = null;
            if (Type is "CellIs" or "TimePeriod")
            {
                var key = Type == "CellIs" ? "formula2" : "date2";
                second = TextField(details, _text.Key("conditional_formatting.rule_value_placeholder"), Get(key), v => _rule[key] = v, "ConditionalSecondValue");
                details.Children.Remove(first); details.Children.Remove(second);
                values = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 }; values.Children.Add(first); Grid.SetColumn(second, 1); values.Children.Add(second); details.Children.Add(values);
            }
            void Visibility()
            {
                var between = (string?)op?.SelectedItem is "Between" or "NotBetween";
                first.IsVisible = Type != "TimePeriod" || between;
                if (second is not null) second.IsVisible = between;
                if (values is not null) { values.ColumnDefinitions[1].Width = between ? new GridLength(1, GridUnitType.Star) : new GridLength(0); values.IsVisible = first.IsVisible; }
                Notify();
            }
            if (op is not null) op.SelectionChanged += (_, _) => Visibility(); Visibility();
            _valid.Add(() => (!first.IsVisible || !string.IsNullOrWhiteSpace(first.Text)) && (second is null || !second.IsVisible || !string.IsNullOrWhiteSpace(second.Text)));
            if (Type == "Formula")
            {
                var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }; details.Children.Add(error);
                string? Error()
                {
                    var formula = first.Text?.Trim() ?? "";
                    if (formula.Length == 0) return null;
                    if (!formula.StartsWith('=')) return "formula_error_must_start_with_equals";
                    return ReferenceUtils.GetTokens(formula[1..]).EnumerateArray().Any(x => x.GetProperty("token").ValueKind == JsonValueKind.String && x.GetProperty("token").GetString() == "Illegal") ? "formula_error_invalid" : null;
                }
                _text.Compose(t => Error() is { } key ? t["conditional_formatting." + key] : "").Bind(error, TextBlock.TextProperty);
                first.TextChanged += (_, _) => { _text.Compose(t => Error() is { } key ? t["conditional_formatting." + key] : "").Bind(error, TextBlock.TextProperty); Notify(); };
                _valid.Add(() => Error() is null);
            }
        }
        var divider = new Border { Height = 1, Margin = new Avalonia.Thickness(-12, 8) }; divider.Bind(Border.BackgroundProperty, divider.GetResourceObservable("Dialog-grey-300")); details.Children.Add(divider);
        var formatTitle = WorkbookView.Label(_text.Key("conditional_formatting.format_style")); formatTitle.FontSize = 14; formatTitle.FontWeight = FontWeight.SemiBold; details.Children.Add(formatTitle);
        _format = new StyleEditor(_rule["format"]!.AsObject(), true, _model, _text, _owner); _format.Changed += Notify; details.Children.Add(_format);
        Flag("conditional_formatting.stop_if_true", "stop_if_true", parent: details);
    }
    private void BuildPresets(string type, StackPanel? target = null)
    {
        var parent = target ?? _fields;
        var icons = type is "IconSet" or "IconRating";
        if (!icons) parent.Children.Add(WorkbookView.Label(_text.Key("conditional_formatting.default_styles")));
        foreach (var group in ConditionalPreset.All.Where(p => p.Type == type).GroupBy(p => type == "ColorScale" ? 0 : p.Icons.Length).OrderBy(g => g.Key))
        {
            var section = new StackPanel { Spacing = 4 }; parent.Children.Add(section);
            if (group.Key != 0) section.Children.Add(WorkbookView.Label(_text.Compose(t => group.Key + " " + t["conditional_formatting.icon_sets_icons"])));
            Panel tiles = icons ? new Grid
            {
                ColumnDefinitions = new("*,*,*,*"), ColumnSpacing = 6, RowSpacing = 6,
                RowDefinitions = new(string.Join(",", Enumerable.Repeat("32", (group.Count() + 3) / 4)))
            } : new WrapPanel();
            section.Children.Add(tiles);
            foreach (var preset in group)
            {
                var tile = new ConditionalPresetTile(preset, _text);
                if (icons)
                {
                    Grid.SetColumn(tile, tiles.Children.Count % 4); Grid.SetRow(tile, tiles.Children.Count / 4);
                    if (type == "IconSet" ? preset.Id == _selectedIconPreset :
                        preset.Icons.Length == _rule["thresholds"]!.AsArray().Count && preset.Colors[0] == _ratingColor?.ToString())
                        tile.Classes.Add("selected");
                }
                tile.Click += (_, _) =>
                {
                    _rule = preset.Apply(Value());
                    if (type == "IconSet") { _selectedIconPreset = preset.Id; _presetCount = preset.Icons.Length; _iconRows = (JsonArray)_rule["thresholds"]!.DeepClone(); }
                    if (type == "IconRating")
                    {
                        _ratingIcon = preset.Icons[0]; _ratingColor = JsonValue.Create(preset.Colors[0]);
                        _iconRows = new JsonArray(preset.Icons.Reverse().Select((icon, i) => (JsonNode)new JsonObject { ["icon"] = icon, ["color"] = preset.Colors[preset.Colors.Length - 1 - i] }).ToArray());
                    }
                    BuildFields();
                };
                tiles.Children.Add(tile);
            }
        }
    }
    private void BuildScale()
    {
        // ColorScaleRule.tsx: two sections, eight equal preset columns at every width.
        _fields.Spacing = 0; _fields.Margin = new Thickness(0, -10, 0, 0);
        var presets = EditorSection(true);
        void Title(StackPanel parent, string key)
        {
            var title = WorkbookView.Label(_text.Key("conditional_formatting." + key));
            title.FontSize = 14; title.LineHeight = 21; title.FontWeight = FontWeight.SemiBold; parent.Children.Add(title);
        }
        Title(presets, "tab_color_scale");
        var presetGroup = new StackPanel { Spacing = 4 }; presets.Children.Add(presetGroup);
        var presetLabel = WorkbookView.Label(_text.Key("conditional_formatting.default_styles"));
        presetLabel.LineHeight = 18; presetLabel.FontWeight = FontWeight.Medium; presetGroup.Children.Add(presetLabel);
        var tiles = new Grid { Name = "ColorScalePresets", ColumnDefinitions = new("*,*,*,*,*,*,*,*"),
            RowDefinitions = new("32,32"), ColumnSpacing = 4, RowSpacing = 4 };
        presetGroup.Children.Add(tiles);
        var settings = EditorSection(false); Title(settings, "color_scale_settings");
        var thresholds = _rule["thresholds"]!.AsArray();
        if (thresholds.Count == 2) thresholds.Insert(1, new JsonObject { ["cfvo"] = "None", ["color"] = "#FFFFFF" });
        var stops = new List<(ComboBox Type, TextBox Input, PanelColorButton Color)>();
        for (var i = 0; i < 3; i++)
        {
            var item = thresholds[i]!;
            var label = _text.Key("conditional_formatting.color_scale_" + (i == 0 ? "minimum" : i == 1 ? "midpoint" : "maximum"));
            var group = new StackPanel { Spacing = 4 };
            var stopLabel = WorkbookView.Label(label); stopLabel.LineHeight = 18; stopLabel.FontWeight = FontWeight.Medium;
            group.Children.Add(stopLabel); settings.Children.Add(group);
            var row = new Grid { Name = "ColorScaleStop" + i, ColumnSpacing = 6, Height = 32 }; group.Children.Add(row);
            var choices = i == 0 ? new[] { "Min", "Number", "Percent", "Formula", "Percentile" } :
                i == 1 ? ["Number", "Percent", "Formula", "Percentile", "None"] : ["Max", "Number", "Percent", "Formula", "Percentile"];
            var original = item["cfvo"];
            var kind = original is JsonObject obj ? obj.First().Key : original!.GetValue<string>();
            var type = label.Describe(new ComboBox { Name = "ColorScaleType" + i, Classes = { "dialog-select", "color-scale-type" },
                ItemsSource = choices, SelectedItem = kind, MinWidth = 0,
                ItemTemplate = LocalizedText.Options(value => WorkbookOptions.Threshold(_text, value)) });
            row.Children.Add(type); _draftChoices.Add(type);
            var input = label.Describe(new PanelTextBox { Name = "ColorScaleValue" + i, MinWidth = 0,
                Text = ThresholdText(original), Padding = new Thickness(10, 0, 32, 0) });
            input.GotFocus += (_, _) => input.SelectAll();
            var useSelection = _text.Key("conditional_formatting.use_selection").Describe(new WorkbookButton {
                Name = "ColorScaleSelection" + i, Content = new WorkbookIcon("SquareMousePointer"),
                Classes = { "workbook-flat" }, Width = 24, Height = 24, Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0) });
            useSelection.Click += (_, _) => input.Text = _selection();
            var valueField = new Grid { Children = { input, useSelection } }; row.Children.Add(valueField); _draftInputs.Add(input);
            var color = new PanelColorButton(_owner, _model, label, item["color"], "ColorScale" + i, defaultToCurrent: true);
            color.Classes.Add("color-scale-color");
            void RefreshColorBorder()
            {
                if (color.Content is Border swatch && color.TryFindResource("Dialog-common-black", out var brush) && brush is ISolidColorBrush solid)
                    swatch.BorderBrush = new SolidColorBrush(solid.Color, .12);
            }
            color.AttachedToVisualTree += (_, _) => RefreshColorBorder();
            color.ActualThemeVariantChanged += (_, _) => RefreshColorBorder();
            row.Children.Add(color); stops.Add((type, input, color));
            bool NeedsValue() => type.SelectedItem as string is not ("Min" or "Max" or "None");
            void Layout()
            {
                var hasColor = type.SelectedItem as string != "None";
                valueField.IsVisible = NeedsValue(); color.IsVisible = hasColor;
                row.ColumnDefinitions = new(NeedsValue() ? "*,*,32" : hasColor ? "*,32" : "*");
                Grid.SetColumn(valueField, NeedsValue() ? 1 : 0); Grid.SetColumn(color, NeedsValue() ? 2 : hasColor ? 1 : 0);
            }
            _valid.Add(() => !NeedsValue() || !string.IsNullOrWhiteSpace(input.Text));
            _save.Add(() => item["cfvo"] = (string?)type.SelectedItem switch {
                "None" => JsonValue.Create("None"),
                "Min" or "Max" => JsonValue.Create((string)type.SelectedItem),
                "Formula" => new JsonObject { ["Formula"] = input.Text ?? "" },
                var selected => new JsonObject { [selected!] = ParseFloat(input.Text ?? "", false) }
            });
            type.SelectionChanged += (_, _) => { Layout(); Notify(); };
            input.TextChanged += (_, _) => Notify();
            color.Changed += () => { item["color"] = color.Value; Notify(); };
            Layout();
        }
        foreach (var preset in ConditionalPreset.All.Where(p => p.Type == "ColorScale"))
        {
            var tile = new ConditionalPresetTile(preset, _text);
            Grid.SetColumn(tile, tiles.Children.Count % 8); Grid.SetRow(tile, tiles.Children.Count / 8); tiles.Children.Add(tile);
            if (preset.Id == _selectedScalePreset) tile.Classes.Add("selected");
            tile.Click += (_, _) =>
            {
                // A preset changes colors and the midpoint mode, not the user's min/max text.
                // Keep the controls alive, including incomplete numeric/formula drafts.
                _building = true;
                _selectedScalePreset = preset.Id;
                foreach (var other in tiles.Children.OfType<ConditionalPresetTile>()) other.Classes.Set("selected", ReferenceEquals(other, tile));
                stops[0].Color.Value = JsonValue.Create(preset.Colors[0].ToLowerInvariant());
                stops[2].Color.Value = JsonValue.Create(preset.Colors[^1].ToLowerInvariant());
                stops[1].Color.Value = JsonValue.Create(ColorScaleSwatch.Hex(ColorScaleSwatch.Sample(preset.Colors, .5)));
                if (preset.Colors.Length == 2) { stops[1].Type.SelectedItem = "None"; stops[1].Input.Text = ""; }
                else if ((string?)stops[1].Type.SelectedItem == "None") { stops[1].Type.SelectedItem = "Percentile"; stops[1].Input.Text = "50"; }
                _building = false; Notify();
            };
        }
    }
    private void BuildDataBars()
    {
        _fields.Spacing = 0; _fields.Margin = new Thickness(0, -10, 0, 0);
        // Upstream keeps the selected preset color separate from the editable positive color.
        _dataBarColor ??= _initial["type"]?.ToString() == "DataBar"
            ? _initial["positive_color"] is JsonValue colorValue ? colorValue.ToString() : ""
            : "#8CB354";
        void Title(StackPanel parent, string key)
        {
            var label = WorkbookView.Label(_text.Key("conditional_formatting." + key));
            label.FontSize = 14; label.LineHeight = 21; label.FontWeight = FontWeight.SemiBold; parent.Children.Add(label);
        }
        StackPanel Group(Panel parent, string key)
        {
            var group = new StackPanel { Spacing = 4 }; parent.Children.Add(group);
            var label = WorkbookView.Label(_text.Key("conditional_formatting.data_bars_" + key));
            label.LineHeight = 18; label.FontWeight = FontWeight.Medium; group.Children.Add(label); return group;
        }
        var presets = EditorSection(true); Title(presets, "tab_data_bars");
        var buttons = new List<(Button Button, string Color, bool Gradient)>();
        PanelColorButton? positive = null;
        void SelectPreset()
        {
            foreach (var item in buttons)
                item.Button.Classes.Set("selected", item.Color == _dataBarColor && item.Gradient == _rule["is_gradient"]!.GetValue<bool>());
        }
        foreach (var gradient in new[] { false, true })
        {
            var group = Group(presets, gradient ? "gradient_fill" : "solid_fill");
            var tiles = new Grid { Name = gradient ? "DataBarGradientPresets" : "DataBarSolidPresets",
                ColumnDefinitions = new("*,*,*,*,*,*,*,*"), ColumnSpacing = 4, Height = 32 };
            group.Children.Add(tiles);
            foreach (var color in new[] { "#8CB354", "#EC5753", "#3458B7", "#F2994A", "#F8CD3D", "#523E93", "#FF2D55", "#333333" })
            {
                var button = _text.Compose(_ => color).Describe(new WorkbookButton {
                    Classes = { "data-bar-preset" }, Height = 32, MinWidth = 0, Padding = default,
                    HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                    Content = new DataBarSwatch(color, gradient) { IsHitTestVisible = false }
                });
                Grid.SetColumn(button, tiles.Children.Count); tiles.Children.Add(button); buttons.Add((button, color, gradient));
                button.Click += (_, _) =>
                {
                    _dataBarColor = color; _rule["is_gradient"] = gradient;
                    positive!.Value = JsonValue.Create(color); SelectPreset(); Notify();
                };
            }
        }
        SelectPreset();
        var settings = EditorSection(false); Title(settings, "data_bars_settings");
        var bounds = new Grid { Name = "DataBarBounds", ColumnDefinitions = new("*,*"), ColumnSpacing = 6 };
        settings.Children.Add(bounds);
        foreach (var key in new[] { "min", "max" })
        {
            var group = Group(bounds, key == "min" ? "minimum" : "maximum"); Grid.SetColumn(group, key == "min" ? 0 : 1);
            var label = _text.Key("conditional_formatting.data_bars_" + (key == "min" ? "minimum" : "maximum"));
            var original = _rule[key];
            var type = label.Describe(new ComboBox {
                Name = "DataBarType_" + key, Classes = { "dialog-select", "data-bar-type" }, MinWidth = 0,
                ItemsSource = new[] { "Auto", key == "min" ? "Min" : "Max", "Number", "Percent", "Percentile", "Formula" },
                SelectedItem = original is JsonObject obj ? obj.First().Key : original?.GetValue<string>() ?? "Auto",
                ItemTemplate = LocalizedText.Options(value => WorkbookOptions.Threshold(_text, value))
            });
            group.Children.Add(type); _draftChoices.Add(type);
            var input = label.Describe(new PanelTextBox { Name = "DataBarValue_" + key, MinWidth = 0, Classes = { "data-bar-value" },
                Text = ThresholdText(original) });
            input.GotFocus += (_, _) => input.SelectAll();
            group.Children.Add(input); _draftInputs.Add(input);
            void Layout() => input.IsEnabled = type.SelectedItem as string is not ("Auto" or "Min" or "Max");
            type.SelectionChanged += (_, _) => { input.Text = ""; Layout(); Notify(); };
            input.TextChanged += (_, _) => Notify(); Layout();
            _save.Add(() => _rule[key] = (string?)type.SelectedItem switch {
                "Auto" => null, "Min" or "Max" => JsonValue.Create((string)type.SelectedItem),
                "Formula" => new JsonObject { ["Formula"] = input.Text ?? "" },
                var selected => new JsonObject { [selected!] = ParseFloat(input.Text ?? "", true) }
            });
        }
        var colors = new Grid { Name = "DataBarColors", ColumnDefinitions = new("*,*"), ColumnSpacing = 6 };
        settings.Children.Add(colors);
        foreach (var key in new[] { "positive", "negative" })
        {
            var group = Group(colors, key); Grid.SetColumn(group, key == "positive" ? 0 : 1);
            var property = key + "_color"; var label = _text.Key("conditional_formatting.data_bars_" + key);
            var row = new Grid { ColumnDefinitions = new("*,32"), ColumnSpacing = 6, Height = 32 }; group.Children.Add(row);
            var input = label.Describe(new PanelTextBox { Name = "DataBarColor_" + key, MinWidth = 0, Text = ResolveColor(_rule[property]) });
            input.GotFocus += (_, _) => input.SelectAll(); row.Children.Add(input); _draftInputs.Add(input);
            var color = new PanelColorButton(_owner, _model, label, _rule[property], "DataBar" + key, defaultToCurrent: true);
            color.Classes.Add("data-bar-color"); Grid.SetColumn(color, 1); row.Children.Add(color);
            if (key == "positive") positive = color;
            void RefreshBorder()
            {
                if (color.Content is Border swatch && color.TryFindResource("Dialog-common-black", color.ActualThemeVariant, out var resource) && resource is ISolidColorBrush brush)
                    swatch.BorderBrush = new SolidColorBrush(brush.Color, .12);
            }
            color.AttachedToVisualTree += (_, _) => RefreshBorder(); color.ActualThemeVariantChanged += (_, _) => RefreshBorder();
            var syncing = false;
            input.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty && !syncing) color.Value = JsonValue.Create(input.Text ?? "");
            };
            color.Changed += () =>
            {
                _rule[property] = color.Value; syncing = true;
                try { input.Text = ResolveColor(color.Value); } finally { syncing = false; }
                Notify();
            };
        }
        var preferences = Group(settings, "preferences");
        Flag("conditional_formatting.data_bars_show_value", "show_value", parent: preferences);
        preferences.Children.OfType<CheckBox>().Single().Classes.Add("data-bar-preference");
    }
    private void BuildIcons()
    {
        _fields.Spacing = 0; _fields.Margin = new Thickness(0, -10, 0, 0);
        if (_iconRows is null)
        {
            var rows = _rule["thresholds"]!.AsArray();
            if (Type == "IconSet")
            {
                _iconRows = (JsonArray)rows.DeepClone();
                var preset = ConditionalPreset.All.FirstOrDefault(p => p.Type == "IconSet" && p.Icons.Reverse().SequenceEqual(rows.Select(x => x!["icon"]!.GetValue<string>())));
                _selectedIconPreset = preset?.Id; _presetCount = rows.Count;
                _ratingIcon = "Star"; _ratingColor = JsonValue.Create("#FFD700");
            }
            else
            {
                _ratingIcon = _rule["icon"]!.GetValue<string>(); _ratingColor = _rule["color"]?.DeepClone(); _presetCount = 3;
                _iconRows = new JsonArray(rows.Select(_ => (JsonNode)new JsonObject { ["icon"] = _ratingIcon, ["color"] = _ratingColor?.DeepClone() }).ToArray());
            }
        }
        var presets = EditorSection(true);
        presets.Children.Add(IconChoice("IconMode", ["IconSet", "IconRating"], Type,
            value => _text.Key("conditional_formatting.icon_sets_" + (value == "IconSet" ? "mode_preset" : "ratings")), type =>
        {
            var current = Value(); var old = current["thresholds"]!.AsArray();
            if (Type == "IconSet") _iconRows = (JsonArray)old.DeepClone();
            else { _ratingIcon = current["icon"]!.GetValue<string>(); _ratingColor = current["color"]?.DeepClone(); }
            var next = DefaultRule(type); next["show_value"] = current["show_value"]?.DeepClone();
            next["thresholds"] = new JsonArray(old.Select((x, i) => type == "IconRating"
                ? (JsonNode)new JsonArray(i == 0 ? new JsonObject { ["Percent"] = 0 } : x!["cfvo"]!.DeepClone(), x!["is_strict"]!.DeepClone())
                : new JsonObject { ["cfvo"] = i == 0 ? JsonValue.Create("Min") : x![0]!.DeepClone(), ["is_strict"] = x![1]!.DeepClone(), ["icon"] = _iconRows![i]!["icon"]!.DeepClone(), ["color"] = _iconRows[i]!["color"]?.DeepClone() }).ToArray());
            if (type == "IconRating") { next["icon"] = _ratingIcon; next["color"] = _ratingColor?.DeepClone(); }
            _rule = next; BuildFields();
        }));
        BuildPresets(Type, presets);
        var settings = EditorSection(false);
        var title = WorkbookView.Label(_text.Key("conditional_formatting.icon_sets_settings")); title.FontSize = 14; title.FontWeight = FontWeight.SemiBold; settings.Children.Add(title);
        var rowsPanel = new StackPanel { Spacing = 10 }; settings.Children.Add(rowsPanel);
        var thresholds = _rule["thresholds"]!.AsArray();
        var ratingIcons = new List<PanelIconButton>(); var ratingColors = new List<PanelColorButton>(); var syncingRating = false;
        for (var i = thresholds.Count - 1; i >= 0; i--)
        {
            var rating = Type == "IconRating";
            if (rating && i == 0) continue;
            var item = thresholds[i]!; var last = i == 0;
            var group = new StackPanel { Spacing = 4 }; rowsPanel.Children.Add(group);
            var label = _text.Key("conditional_formatting.icon_sets_" + (last ? "else" : "when_value_is"));
            group.Children.Add(WorkbookView.Label(label));
            var row = new Grid { Name = "IconThresholdRow" + i, ColumnDefinitions = new("64,*,1.5*,32,32"), ColumnSpacing = 4, Height = 32 }; group.Children.Add(row);
            row.Children.Add(IconChoice("IconOperator" + i, [">=", ">"], (rating ? item[1] : item["is_strict"])?.GetValue<bool>() == true ? ">=" : ">",
                value => _text.Compose(_ => value == ">=" ? "≥" : ">"), value =>
                { if (rating) item[1] = value == ">="; else item["is_strict"] = value == ">="; Notify(); }));
            var original = rating ? item[0] : item["cfvo"];
            var kind = original is JsonObject obj ? obj.First().Key : "Min";
            var input = label.Describe(new PanelTextBox { Name = "IconValue" + i, Classes = { "icon-value" }, Text = ThresholdText(original), MinWidth = 0, IsEnabled = !last });
            var choices = i == thresholds.Count - 1 ? new[] { "Min", "Percent", "Number", "Percentile", "Formula" } : ["Percent", "Number", "Percentile", "Formula"];
            var type = _text.Key("conditional_formatting.icon_sets_settings").Describe(new ComboBox
            {
                Name = "IconType" + i, ItemsSource = choices, SelectedItem = last ? "Percent" : kind,
                ItemTemplate = LocalizedText.Options(value => WorkbookOptions.Threshold(_text, value)),
                Classes = { "dialog-select" }, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = !last
            });
            _draftInputs.Add(input); _draftChoices.Add(type);
            type.DropDownOpened += (_, _) => type.MaxDropDownHeight = Math.Max(32, (TopLevel.GetTopLevel(type)?.ClientSize.Height ?? 800) - 16);
            _save.Add(() =>
            {
                JsonNode value = last ? JsonValue.Create("Min")! : (string?)type.SelectedItem switch
                {
                    "Min" => new JsonObject { ["Percent"] = 0 },
                    "Formula" => new JsonObject { ["Formula"] = input.Text ?? "" },
                    var selected => new JsonObject { [selected ?? "Percent"] = ParseFloat(input.Text ?? "", true) }
                };
                if (rating) item[0] = value; else item["cfvo"] = value;
            });
            input.TextChanged += (_, _) => Notify(); type.SelectionChanged += (_, _) => Notify();
            Grid.SetColumn(input, 1); row.Children.Add(input); Grid.SetColumn(type, 2); row.Children.Add(type);
            var icon = new PanelIconButton(_owner, (rating ? _rule["icon"] : item["icon"])!.GetValue<string>(), ResolveColor(rating ? _rule["color"] : item["color"])) { Name = "IconPicker" + i, Classes = { "icon-rule-picker" } };
            Grid.SetColumn(icon, 3); row.Children.Add(icon); icon.IsEnabled = !rating || i == thresholds.Count - 1;
            if (rating) ratingIcons.Add(icon);
            icon.Changed += () =>
            {
                if (rating) { _rule["icon"] = _ratingIcon = icon.Value; foreach (var other in ratingIcons) other.SetValue(icon.Value); }
                else item["icon"] = icon.Value;
                Notify();
            };
            var color = new PanelColorButton(_owner, _model, _text.Key("color_picker.title"), rating ? _rule["color"] : item["color"], "IconColor" + i, defaultToCurrent: true);
            color.Classes.Add("icon-color");
            void RefreshBorder()
            {
                if (color.Content is Border swatch && color.TryFindResource("Dialog-common-black", color.ActualThemeVariant, out var resource) && resource is ISolidColorBrush brush)
                    swatch.BorderBrush = new SolidColorBrush(brush.Color, .12);
            }
            color.AttachedToVisualTree += (_, _) => RefreshBorder(); color.ActualThemeVariantChanged += (_, _) => RefreshBorder();
            Grid.SetColumn(color, 4); row.Children.Add(color); color.IsEnabled = !rating || i == thresholds.Count - 1;
            if (rating) ratingColors.Add(color);
            color.Changed += () =>
            {
                if (_building || _detached) return;
                if (rating) _rule["color"] = color.Value; else item["color"] = color.Value;
                icon.Color = ResolveColor(color.Value);
                if (rating && !syncingRating)
                {
                    syncingRating = true;
                    try
                    {
                        _ratingColor = color.Value;
                        foreach (var savedRow in _iconRows!) savedRow!["color"] = _ratingColor?.DeepClone();
                        foreach (var other in ratingColors) if (!ReferenceEquals(other, color)) other.Value = color.Value;
                    }
                    finally { syncingRating = false; }
                }
                Notify();
            };
        }
        var preferences = new StackPanel { Spacing = 4 }; settings.Children.Add(preferences);
        preferences.Children.Add(WorkbookView.Label(_text.Key("conditional_formatting.data_bars_preferences")));
        Flag("conditional_formatting.icon_sets_show_value", "show_value", parent: preferences);
    }
    private StackPanel EditorSection(bool separator)
    {
        var content = new StackPanel { Spacing = 12 };
        var frame = new Border { Child = content, Padding = new Thickness(12, 16), Margin = new Thickness(-12, 0), BorderThickness = new Thickness(0, 0, 0, separator ? 1 : 0) };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Dialog-grey-300")); _fields.Children.Add(frame); return content;
    }
    private static Border IconChoice(string name, string[] values, string selected, Func<string, LocalizedText> label, Action<string> changed)
    {
        var buttons = new UniformGrid { Rows = 1, Margin = new Thickness(-1) };
        foreach (var value in values)
        {
            var button = label(value).Bind(new ToggleButton
            {
                Name = name + "_" + value, IsChecked = value == selected, Classes = { "workbook-toggle", "rule-choice" }
            }, ContentControl.ContentProperty);
            label(value).Describe(button);
            button.Click += (_, _) =>
            {
                foreach (var other in buttons.Children.OfType<ToggleButton>()) other.IsChecked = ReferenceEquals(other, button);
                if (selected == value) return;
                selected = value; changed(value);
            };
            buttons.Children.Add(button);
        }
        var frame = new Border { Child = buttons, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Dialog-grey-300"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Dialog-grey-50")); return frame;
    }
    private ComboBox Threshold(StackPanel parent, LocalizedText label, JsonNode? original, string[] choices, Action<JsonNode?> save, bool requireValue = false, bool selection = false)
    {
        var kind = original is JsonObject obj ? obj.First().Key : original?.GetValue<string>() ?? "Auto";
        var type = WorkbookView.Choice(parent, label, choices, kind, v => WorkbookOptions.Threshold(_text, v));
        var input = new PanelTextBox { Text = ThresholdText(original) }; parent.Children.Add(input);
        _draftInputs.Add(input); _draftChoices.Add(type);
        Button? selected = null;
        if (selection) { selected = WorkbookView.Button(_text.Key("conditional_formatting.use_selection"), () => input.Text = _selection()); parent.Children.Add(selected); }
        bool NeedsValue() => type.SelectedItem as string is not ("Auto" or "Min" or "Max" or "None");
        void Update()
        {
            input.IsEnabled = NeedsValue();
            input.IsVisible = !requireValue || NeedsValue();
            if (selected is not null) selected.IsVisible = NeedsValue();
            Notify();
        }
        type.SelectionChanged += (_, _) => { if (Type == "DataBar") input.Text = ""; Update(); };
        input.TextChanged += (_, _) => Notify(); Update();
        if (requireValue) _valid.Add(() => !NeedsValue() || !string.IsNullOrWhiteSpace(input.Text));
        _save.Add(() =>
        {
            var selectedType = (string?)type.SelectedItem ?? "Auto";
            save(selectedType switch
            {
                "Auto" => null,
                "None" => JsonValue.Create("None"),
                "Min" or "Max" => JsonValue.Create(selectedType),
                "Formula" => new JsonObject { [selectedType] = input.Text ?? "" },
                _ => new JsonObject { [selectedType] = ParseFloat(input.Text ?? "", !requireValue) }
            });
        });
        return type;
    }
    private static string ThresholdText(JsonNode? cfvo)
    {
        if (cfvo is not JsonObject data) return "";
        var (kind, value) = data.First();
        if (value is null) return "";
        if (kind is not ("Number" or "Percent" or "Percentile")) return value.ToString();

        // cfRuleMapping uses JavaScript Number strings, not the JSON number spelling.
        // Preserve the shortest round-trip digits, with ECMAScript's decimal/exponent layout.
        var number = double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture);
        if (number == 0) return "0";
        var text = number.ToString("R", CultureInfo.InvariantCulture);
        var e = text.IndexOf('E');
        if (e < 0) return text;
        var exponent = int.Parse(text[(e + 1)..], CultureInfo.InvariantCulture);
        var mantissa = text[..e];
        if (exponent is < -6 or >= 21)
            return mantissa + "e" + (exponent >= 0 ? "+" : "") + exponent.ToString(CultureInfo.InvariantCulture);
        var sign = number < 0 ? "-" : "";
        var digits = mantissa.TrimStart('-').Replace(".", "");
        var point = exponent + 1;
        return sign + (point <= 0 ? "0." + new string('0', -point) + digits :
            point >= digits.Length ? digits + new string('0', point - digits.Length) : digits.Insert(point, "."));
    }
    internal static double ParseFloat(string value, bool zeroFallback)
    {
        var match = Regex.Match(value.TrimStart(), @"^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?");
        if (match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)) return result;
        if (zeroFallback) return 0;
        throw new FormatException("Invalid number: " + value);
    }
    private string Get(string key, string fallback = "") => _rule[key] is JsonValue value ? value.ToString() : fallback;
    private ComboBox Pick(StackPanel parent, string key, string[] choices)
    {
        var box = WorkbookView.Choice(parent, _text.Key("conditional_formatting.highlight_cells_if"), choices, Get(key), v => WorkbookOptions.Operator(_text, v), showLabel: false);
        box.SelectionChanged += (_, _) => { _rule[key] = (string?)box.SelectedItem; Notify(); }; return box;
    }
    private TextBox TextField(StackPanel parent, LocalizedText label, string initial, Action<string> save, string? name = null)
    {
        var input = new PanelTextBox { Name = name, Text = initial }; label.Describe(input); label.Bind(input, TextBox.WatermarkProperty); parent.Children.Add(input);
        _draftInputs.Add(input);
        input.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) { save(input.Text ?? ""); Notify(); } }; return input;
    }
    private PanelColorButton Color(StackPanel parent, LocalizedText label, JsonNode? value, Action<JsonNode?> save, string name)
    {
        var color = new PanelColorButton(_owner, _model, label, value, name, defaultToCurrent: true); parent.Children.Add(color);
        color.Changed += () => { save(color.Value); Notify(); }; return color;
    }
    private string ResolveColor(JsonNode? value) => _model?.ResolveColor(JsonSerializer.SerializeToElement(value)) ?? value?.ToString() ?? "";
    private void Flag(string label, string key, bool invert = false, StackPanel? parent = null)
    {
        var box = _text.Key(label).Bind(new CheckBox { Name = key, Classes = { "conditional-preference" }, IsChecked = (_rule[key]?.GetValue<bool>() ?? false) != invert }, ContentControl.ContentProperty); (parent ?? _fields).Children.Add(box);
        // checkbox.css uses a 14px content box; CSS floors the 1.5px border
        // to whole device pixels (16px outer size at 1x, 17px at 2x).
        TopLevel? root = null;
        void Metrics(object? sender, EventArgs e)
        {
            var scale = root?.RenderScaling ?? 1;
            var border = Math.Max(1, Math.Floor(1.5 * scale)) / scale;
            box.Resources["ConditionalCheckSize"] = 14 + 2 * border;
            box.Resources["ConditionalCheckBorder"] = new Thickness(border);
            box.Resources["ConditionalCheckFocusSize"] = 22 + 2 * border;
        }
        Metrics(null, EventArgs.Empty);
        box.AttachedToVisualTree += (_, _) =>
        {
            root = TopLevel.GetTopLevel(box);
            if (root is not null) root.ScalingChanged += Metrics;
            Metrics(null, EventArgs.Empty);
        };
        box.DetachedFromVisualTree += (_, _) => { if (root is not null) root.ScalingChanged -= Metrics; root = null; };
        box.IsCheckedChanged += (_, _) => { _rule[key] = (box.IsChecked == true) != invert; Notify(); };
    }
    private void Notify()
    {
        if (_building || _detached) return;
        if (Type == "DataBar")
        {
            _preview.Content = new DataBarSwatch(_dataBarColor ?? "", _rule["is_gradient"]!.GetValue<bool>(), header: true)
                { Name = "DataBarHeader", Width = 40, Height = 32 };
            _text.Key("conditional_formatting.tab_data_bars").Bind(_description, TextBlock.TextProperty);
            Changed?.Invoke(); return;
        }
        if (Type == "ColorScale")
        {
            // Upstream previews depend on colors, even while a numeric stop is incomplete.
            var stops = _rule["thresholds"]!.AsArray();
            var colors = stops.Where((_, i) => i != 1 || _draftChoices[1].SelectedItem as string != "None")
                .Select(stop => ResolveColor(stop!["color"])).ToArray();
            var swatch = new ColorScaleSwatch(colors) { Name = "ColorScaleHeader", Width = 40, Height = 32 };
            swatch.Bind(ColorScaleSwatch.OutlineBrushProperty, swatch.GetResourceObservable("Dialog-common-black"));
            _preview.Content = swatch; _text.Key("conditional_formatting.desc_color_scale").Bind(_description, TextBlock.TextProperty);
            Changed?.Invoke(); return;
        }
        try
        {
            var value = Value();
            if (Type is "IconSet" or "IconRating")
            {
                // EditRule uses the selected preset's first icon, not the editable thresholds.
                // Unmatched saved sets use cfRuleMapping's fallback preset by count.
                var fallback = _presetCount switch { 2 => "ind-3-checkx", 4 => "dir-4-arrows-color", 5 => "dir-5-arrows-color", _ => "dir-3-arrows-color" };
                var preset = ConditionalPreset.All.Single(p => p.Id == (_selectedIconPreset ?? fallback));
                var icon = Type == "IconSet" ? preset.Icons[0] : _ratingIcon == "Triangle" ? "TriangleUpFilled" : PanelIconButton.Icons.Contains(_ratingIcon) ? _ratingIcon : "Cross";
                _preview.Content = new ConditionalIconHeaderPreview(icon, Type == "IconSet" ? preset.Colors[0] : ResolveColor(_ratingColor));
                _text.Key("conditional_formatting.tab_icon_sets").Bind(_description, TextBlock.TextProperty);
            }
            else
            {
                _preview.Content = new ConditionalRulePreview(value, _model) { Width = 40, Height = 32, MinWidth = 0 };
                _text.Compose(t => ConditionalRulePresentation.Description(value, t, _resolve)).Bind(_description, TextBlock.TextProperty);
            }
        }
        catch (FormatException) { }
        Changed?.Invoke();
    }
    internal JsonObject Value()
    {
        foreach (var save in _save) save();
        if (_format is not null) _rule["format"] = _format.Value();
        var result = (JsonObject)_rule.DeepClone();
        if (Type == "CellIs" && Get("operator") is not ("Between" or "NotBetween")) result["formula2"] = null;
        if (Type == "TimePeriod" && Get("time_period") is not ("Between" or "NotBetween")) { result["date1"] = null; result["date2"] = null; }
        if (Type == "ColorScale" && result["thresholds"]!.AsArray().Count == 3 && result["thresholds"]![1]!["cfvo"]?.ToString() == "None") result["thresholds"]!.AsArray().RemoveAt(1);
        if (Type == "IconSet") { result["thresholds"]![0]!["cfvo"] = "Min"; result["thresholds"]![0]!["is_strict"] = true; }
        if (Type == "IconRating") { result["thresholds"]![0]![0] = new JsonObject { ["Percent"] = 0 }; result["thresholds"]![0]![1] = true; }
        return result;
    }
}
