using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CyanCalc.Controls.Preview;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    private const string EuroFormat = "\"€\"#,##0.00";
    private const string UsDollarFormat = "\"$\"#,##0.00";
    private const string BritishPoundFormat = "\"£\"#,##0.00";
    private TextBox? _fontSizeInput;
    private WorkbookIcon? _currencyIcon;
    private bool _skipFontSizeBlur;
    private readonly Dictionary<string, ToggleButton> _fontToggles = new();
    private readonly Dictionary<string, ToggleButton> _alignmentButtons = new();
    private readonly Dictionary<string, MenuItem> _numberFormatItems = new();
    private readonly Dictionary<string, string> _numberFormatExampleKeys = new();
    private Button? _pngButton;
    private Button? _mergeButton;
    private ToggleButton? _gridlinesButton;
    private ToggleButton? _conditionalButton;
    private ToggleButton? _stylesButton;
    private WorkbookIcon? _gridlinesIcon;
    private Border? _fontColorLine;
    private Border? _fillColorLine;
    private Control? _toolbarRoot;
    private string? _drawerFeature;
    private ScrollViewer? _toolbarScroll;
    private Button? _toolbarBack;
    private Button? _toolbarForward;
    private readonly List<Button> _toolbarWriteButtons = new();
    private Control BuildToolbar()
    {
        var tools = new StackPanel { Name = "WorkbookToolbarActions", Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 4) };
        void Divider() => tools.Children.Add(new Border { Width = 1, Height = 16, Margin = new Thickness(10, 0), Background = ConditionalRenderer.Brush("#E0E0E0") });
        _undoButton = IconButton("Undo2", Undo, Text.Key("toolbar.undo")); tools.Children.Add(_undoButton);
        _redoButton = IconButton("Redo2", Redo, Text.Key("toolbar.redo")); tools.Children.Add(_redoButton);
        Divider();
        tools.Children.Add(IconButton("PaintRoller", StartFormatPainter, Text.Key("toolbar.copy_styles")));
        tools.Children.Add(IconButton("RemoveFormatting", () => Clear("formatting"), Text.Key("toolbar.clear_formatting")));
        Divider();
        var currencyButton = IconButton("DollarSign", () => ApplyLocaleDateFormat("currency_format"), Text.Key("toolbar.currency"));
        _currencyIcon = (WorkbookIcon)currencyButton.Content!; tools.Children.Add(currencyButton);
        tools.Children.Add(IconButton("Percent", () => Style("num_fmt", "0.00%"), Text.Key("toolbar.percentage")));
        tools.Children.Add(IconButton("DecimalsArrowLeft", () => Style("num_fmt", NumberFormats.Decrease(Snapshot?.Cell.ExtendedStyle.Style.NumFmt ?? "general")), Text.Key("toolbar.decimal_places_decrease")));
        tools.Children.Add(IconButton("DecimalsArrowRight", () => Style("num_fmt", NumberFormats.Increase(Snapshot?.Cell.ExtendedStyle.Style.NumFmt ?? "general")), Text.Key("toolbar.decimal_places_increase")));
        var formatButton = MenuButton("123", new Control[]
        {
            NumberFormatItem("Auto", Text.Key("toolbar.format_menu.auto"), () => Style("num_fmt", "General")), new Separator(),
            NumberFormatItem("Number", Text.Key("toolbar.format_menu.number"), () => ApplyLocaleDateFormat("number_fmt"), "number_example"),
            NumberFormatItem("Percentage", Text.Key("toolbar.format_menu.percentage"), () => Style("num_fmt", "0.00%")), new Separator(),
            NumberFormatItem("Euro (EUR)", Text.Key("toolbar.format_menu.currency_eur"), () => Style("num_fmt", EuroFormat)),
            NumberFormatItem("Dollar (USD)", Text.Key("toolbar.format_menu.currency_usd"), () => Style("num_fmt", UsDollarFormat)),
            NumberFormatItem("British Pound (GBP)", Text.Key("toolbar.format_menu.currency_gbp"), () => Style("num_fmt", BritishPoundFormat)), new Separator(),
            NumberFormatItem("Short date", Text.Key("toolbar.format_menu.date_short"), () => ApplyLocaleDateFormat("short_date"), "short_date_example"),
            NumberFormatItem("Long date", Text.Key("toolbar.format_menu.date_long"), () => ApplyLocaleDateFormat("long_date"), "long_date_example"), new Separator(),
            NumberFormatItem("Custom", Text.Key("toolbar.format_menu.custom"), ShowNumberFormat)
        }, Text.Key("toolbar.format_number"));
        formatButton.ContextMenu!.Opened += (_, _) => RefreshNumberFormatSelection(Snapshot?.Cell.ExtendedStyle.Style.NumFmt);
        tools.Children.Add(formatButton);
        Divider();
        tools.Children.Add(IconButton("Minus", () => Style("font.size_delta", "-1"), Text.Key("toolbar.decrease_font_size")));
        var fontSize = new TextBox { Name = "FontSizeInput", Text = "12", Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), FontSize = 12,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        Text.Key("toolbar.font_size").Describe(fontSize);
        fontSize.GotFocus += (_, _) => fontSize.SelectAll();
        fontSize.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _skipFontSizeBlur = true;
                CommitFontSize();
                FocusCell();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _skipFontSizeBlur = true;
                RestoreFontSize();
                FocusCell();
                e.Handled = true;
            }
            else if (e.Key is Key.Up or Key.Down && TryParseFontSize(fontSize.Text, out var current))
            {
                var delta = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
                // The original toolbar bounds only the direction being stepped.
                var next = e.Key == Key.Up ? Math.Min(current + delta, 400) : Math.Max(current - delta, 1);
                fontSize.Text = next.ToString(CultureInfo.InvariantCulture);
                SetFontSize(next);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        fontSize.LostFocus += (_, _) =>
        {
            if (_skipFontSizeBlur) _skipFontSizeBlur = false;
            else CommitFontSize();
        };
        tools.Children.Add(fontSize);
        _fontSizeInput = fontSize;

        tools.Children.Add(IconButton("Plus", () => Style("font.size_delta", "1"), Text.Key("toolbar.increase_font_size")));
        Divider();
        foreach (var item in new[] { ("b", "Bold"), ("i", "Italic"), ("u", "Underline"), ("strike", "Strikethrough") })
        {
            var button = ToolbarToggle(item.Item2, Text.Key("toolbar." + (item.Item1 == "strike" ? "strike_through" : item.Item2.ToLowerInvariant())), () => ToggleFont(item.Item1));
            _fontToggles[item.Item1] = button; tools.Children.Add(button);
        }
        Divider();
        tools.Children.Add(ColorButton("Type", Text.Key("toolbar.font_color"), "font.color", out _fontColorLine));
        tools.Children.Add(ColorButton("PaintBucket", Text.Key("toolbar.fill_color"), "fill.color", out _fillColorLine));
        var borders = IconButton("Grid2X2", () => { }, Text.Key("toolbar.borders.title"));
        borders.Click += (_, _) => ShowBorderPicker(borders); tools.Children.Add(borders);
        _conditionalButton = ToolbarToggle("Brush", Text.Key("toolbar.conditional_formatting"), ShowConditionalFormats);
        tools.Children.Add(_conditionalButton);
        var themes = MenuButton("", [], Text.Key("toolbar.themes"), "SwatchBook", () =>
        {
            CommitEdit();
            // The popup must restore grid focus instead of reopening the formula editor.
            FocusCell();
        });
        themes.Content = new WorkbookIcon("SwatchBook"); themes.Padding = new Thickness(6); themes.Width = 28;
        themes.ContextMenu!.Opened += (_, _) => themes.ContextMenu.ItemsSource = ThemeMenu();
        tools.Children.Add(themes);
        _stylesButton = ToolbarToggle("Layers", Text.Key("toolbar.named_styles"), ShowNamedStyles);
        tools.Children.Add(_stylesButton);
        Divider();
        tools.Children.Add(AlignmentButton("AlignLeft", Text.Key("toolbar.align_left"), "horizontal:left"));
        tools.Children.Add(AlignmentButton("AlignCenter", Text.Key("toolbar.align_center"), "horizontal:center"));
        tools.Children.Add(AlignmentButton("AlignRight", Text.Key("toolbar.align_right"), "horizontal:right"));
        tools.Children.Add(AlignmentButton("ArrowUpToLine", Text.Key("toolbar.vertical_align_top"), "vertical:top"));
        tools.Children.Add(AlignmentButton("ArrowMiddleFromLine", Text.Key("toolbar.vertical_align_middle"), "vertical:center"));
        tools.Children.Add(AlignmentButton("ArrowDownToLine", Text.Key("toolbar.vertical_align_bottom"), "vertical:bottom"));
        tools.Children.Add(AlignmentButton("WrapText", Text.Key("toolbar.wrap_text"), "wrap_text:true"));
        _mergeButton = MenuButton("", MergeMenu(), Text.Key("toolbar.merge_cells"), "TableCellsMerge");
        _mergeButton.ContextMenu!.Opened += (_, _) => _mergeButton.ContextMenu.ItemsSource = MergeMenu();
        tools.Children.Add(_mergeButton);
        Divider();
        tools.Children.Add(IconButton("Link2", ShowLinkDialog, Text.Key("toolbar.add_link")));
        Divider();
        _pngButton = IconButton("ImageDown", () => ExportPngRequested?.Invoke(this, EventArgs.Empty), Text.Key("toolbar.selected_png"));
        tools.Children.Add(_pngButton);
        _gridlinesButton = ToolbarToggle("Grid2x2Check", Text.Key("toolbar.show_hide_grid_lines"), () => Command("show_grid_lines", new { show = !(Snapshot?.ShowGridLines ?? true) }));
        _gridlinesIcon = (WorkbookIcon)_gridlinesButton.Content!;
        tools.Children.Add(_gridlinesButton);
        _toolbarWriteButtons.AddRange(tools.Children.OfType<Button>().Where(button => button != _undoButton && button != _redoButton));
        _toolbarScroll = new ScrollViewer { Content = tools, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _toolbarBack = IconButton("ChevronLeft", () => ScrollToolbar(-200), Text.Key("toolbar.scroll_left"));
        _toolbarForward = IconButton("ChevronRight", () => ScrollToolbar(200), Text.Key("toolbar.scroll_right"));
        foreach (var arrow in new[] { _toolbarBack, _toolbarForward })
        {
            arrow.Width = 24; arrow.MinWidth = 0; arrow.Height = 39; arrow.Background = Brushes.White; arrow.IsVisible = false;
        }
        _toolbarBack.HorizontalAlignment = HorizontalAlignment.Left;
        _toolbarForward.HorizontalAlignment = HorizontalAlignment.Right;
        var toolbar = new Grid { Children = { _toolbarScroll, _toolbarBack, _toolbarForward } };
        _toolbarScroll.ScrollChanged += (_, _) => RefreshToolbarScroll();
        _toolbarScroll.SizeChanged += (_, _) => RefreshToolbarScroll();
        _toolbarRoot = new Border { Name = "WorkbookToolbar", Height = 40, Background = Brushes.White,
            BorderBrush = ConditionalRenderer.Brush("#E0E0E0"), BorderThickness = new Thickness(0, 0, 0, 1), Child = toolbar };
        return _toolbarRoot;
    }

    private void RefreshToolbar(CellStyle style)
    {
        if (_fontSizeInput is { IsFocused: false }) RestoreFontSize();
        if (_currencyIcon is not null && Session is { } model)
            _currencyIcon.IconName = model.GetFmtSettings().GetProperty("currency").GetString() switch
            {
                "EUR" => "Euro", "GBP" => "PoundSterling", _ => "DollarSign"
            };
        foreach (var pair in _fontToggles)
        {
            var active = pair.Key switch { "b" => style.Font.B, "i" => style.Font.I, "u" => style.Font.U, _ => style.Font.Strike };
            pair.Value.IsChecked = active;
        }
        RefreshNumberFormatSelection(style.NumFmt);
        foreach (var pair in _alignmentButtons) pair.Value.IsChecked = AlignmentSelected(style, pair.Key);
        foreach (var button in _toolbarWriteButtons) button.IsEnabled = CanEdit;
        if (_toolbarRoot is not null) _toolbarRoot.IsVisible = CanEdit;
        if (_fontSizeInput is not null) _fontSizeInput.IsEnabled = CanEdit;
        if (_pngButton is not null) _pngButton.IsEnabled = CanEdit && ExportPngRequested is not null;
        if (_mergeButton is not null) _mergeButton.IsEnabled = CanEdit && MergeAvailability() is var merge && (merge.Merge || merge.Unmerge);
        if (_gridlinesButton is not null) _gridlinesButton.IsChecked = Snapshot?.ShowGridLines ?? true;
        if (_gridlinesIcon is not null) _gridlinesIcon.IconName = Snapshot?.ShowGridLines == false ? "Grid2x2X" : "Grid2x2Check";
        if (_fontColorLine is not null) _fontColorLine.Background = ConditionalRenderer.Brush(style.Font.Color, "#000000");
        if (_fillColorLine is not null) _fillColorLine.Background = ConditionalRenderer.Brush(style.Fill.Color, "#FFFFFF");
        RefreshDrawerButtons();
    }

    private MenuItem NumberFormatItem(string label, LocalizedText translated, Action action, string? exampleKey = null)
    {
        var item = MenuItem(translated, action);
        item.ToggleType = MenuItemToggleType.CheckBox;
        _numberFormatItems.Add(label, item);
        if (exampleKey is not null) _numberFormatExampleKeys.Add(label, exampleKey);
        return item;
    }
    private void RefreshNumberFormatSelection(string? format)
    {
        string? selected = null;
        if (Session is { } model)
        {
            var settings = model.GetFmtSettings();
            foreach (var (label, key) in _numberFormatExampleKeys)
            {
                var example = settings.GetProperty(key).GetString();
                SetNumberFormatExample(_numberFormatItems[label], label, example);
            }
            // Pinned FormatMenu translations use "10 %" for French and "10%" for en/de/es/it.
            SetNumberFormatExample(_numberFormatItems["Percentage"], "Percentage", Text["toolbar.format_menu.percentage_example"]);
            if (format is not null)
            {
                if (format.Equals("general", StringComparison.OrdinalIgnoreCase)) selected = "Auto";
                else
                {
                    foreach (var (label, value) in new[]
                    {
                        ("Number", settings.GetProperty("number_fmt").GetString()), ("Percentage", "0.00%"),
                        ("Euro (EUR)", EuroFormat), ("Dollar (USD)", UsDollarFormat), ("British Pound (GBP)", BritishPoundFormat),
                        ("Short date", settings.GetProperty("short_date").GetString()),
                        ("Long date", settings.GetProperty("long_date").GetString())
                    })
                        if (format == value) { selected = label; break; }
                    selected ??= "Custom";
                }
            }
        }
        else
        {
            foreach (var label in _numberFormatExampleKeys.Keys) SetNumberFormatExample(_numberFormatItems[label], label, null);
            SetNumberFormatExample(_numberFormatItems["Percentage"], "Percentage", "10%");
        }
        SetNumberFormatExample(_numberFormatItems["Euro (EUR)"], "Euro (EUR)", "€");
        SetNumberFormatExample(_numberFormatItems["Dollar (USD)"], "Dollar (USD)", "$");
        SetNumberFormatExample(_numberFormatItems["British Pound (GBP)"], "British Pound (GBP)", "£");
        foreach (var (label, item) in _numberFormatItems) item.IsChecked = label == selected;
    }
    private ToggleButton AlignmentButton(string icon, LocalizedText name, string key)
    {
        var button = new ToggleButton { Classes = { "workbook-toggle" }, Content = new WorkbookIcon(icon), Padding = new Thickness(6), MinWidth = 28, MinHeight = 28, BorderThickness = new Thickness(0) };
        var parts = key.Split(':');
        button.Click += (_, _) => Style("alignment." + parts[0], parts[0] switch
        {
            "horizontal" => HorizontalAlignmentTarget(parts[1]),
            "wrap_text" => (!(Snapshot?.Cell.ExtendedStyle.Style.Alignment?.WrapText ?? false)).ToString().ToLowerInvariant(),
            _ => parts[1]
        });
        name.Describe(button);
        _alignmentButtons[key] = button;
        return button;
    }
    private static bool AlignmentSelected(CellStyle style, string key) => key switch
    {
        "horizontal:left" => style.Alignment?.Horizontal == "left", "horizontal:center" => style.Alignment?.Horizontal == "center", "horizontal:right" => style.Alignment?.Horizontal == "right",
        "vertical:top" => style.Alignment?.Vertical == "top", "vertical:center" => style.Alignment?.Vertical is "center" or "middle", "vertical:bottom" => style.Alignment?.Vertical is null or "" or "bottom",
        "wrap_text:true" => style.Alignment?.WrapText ?? false, _ => false
    };
    private void RestoreFontSize()
    {
        if (_fontSizeInput is not null) _fontSizeInput.Text = (Snapshot?.Cell.ExtendedStyle.Style.Font.Sz ?? 12).ToString(CultureInfo.InvariantCulture);
    }
    private void CommitFontSize()
    {
        if (_fontSizeInput is null) return;
        if (!TryParseFontSize(_fontSizeInput.Text, out var size) || size is < 1 or > 400)
        { RestoreFontSize(); return; }
        SetFontSize((int)size);
        RestoreFontSize();
    }
    private static bool TryParseFontSize(string? text, out double size)
    {
        // The pinned toolbar uses Number(text.trim()) followed by Number.isInteger.
        const string whitespace = "\t\n\v\f\r \u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000\ufeff";
        var input = (text ?? "").AsSpan().Trim(whitespace);
        size = 0;
        if (input.IsEmpty) return true;
        var radix = input.Length >= 2 && input[0] == '0' ? input[1] switch
        {
            'x' or 'X' => 16, 'b' or 'B' => 2, 'o' or 'O' => 8, _ => 0
        } : 0;
        if (radix != 0)
        {
            foreach (var ch in input[2..])
            {
                var digit = ch switch
                {
                    >= '0' and <= '9' => ch - '0', >= 'a' and <= 'f' => ch - 'a' + 10,
                    >= 'A' and <= 'F' => ch - 'A' + 10, _ => -1
                };
                if (digit < 0 || digit >= radix) return false;
                size = size * radix + digit;
            }
            return input.Length > 2 && double.IsFinite(size);
        }
        return Regex.IsMatch(input.ToString(), @"\A[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant)
            && double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out size) && double.IsInteger(size);
    }
    private void SetFontSize(double size)
    {
        if (!CanEdit) return;
        if (Snapshot?.Cell.ExtendedStyle.Style.Font.Sz == size) return;
        if (Snapshot is not { } snapshot) return;
        Run(() =>
        {
            if (!CommitEdit()) return;
            Session?.Execute("style", new { area = CellArea.FromView(snapshot.View), path = "font.size", value = size.ToString(CultureInfo.InvariantCulture) });
        });
    }
    private void ScrollToolbar(double delta)
    {
        if (_toolbarScroll is null) return;
        _toolbarScroll.Offset = new Vector(Math.Clamp(_toolbarScroll.Offset.X + delta, 0, Math.Max(0, _toolbarScroll.Extent.Width - _toolbarScroll.Viewport.Width)), 0);
        RefreshToolbarScroll();
    }
    private void RefreshToolbarScroll()
    {
        if (_toolbarScroll is null) return;
        if (_toolbarBack is not null) _toolbarBack.IsVisible = _toolbarScroll.Offset.X > 0;
        if (_toolbarForward is not null) _toolbarForward.IsVisible = _toolbarScroll.Offset.X + _toolbarScroll.Viewport.Width < _toolbarScroll.Extent.Width;
    }
    private static Button IconButton(string icon, Action action, string name)
    {
        var button = Button("", action, name);
        button.Content = new WorkbookIcon(icon);
        button.Padding = new Thickness(6);
        button.MinWidth = 28;
        return button;
    }
    private static Button MenuButton(string label, Control[] items, string? name = null, string? icon = null, Action? beforeOpen = null)
    {
        var button = Button(label, () => { }, name ?? label);
        button.Height = 28; button.Padding = new Thickness(6, 0);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        content.Children.Add(icon is null ? new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } : new WorkbookIcon(icon));
        content.Children.Add(new WorkbookIcon("ChevronDown", 12));
        button.Content = content;
        var menu = WorkbookMenus.Create(items, anchored: true);
        button.ContextMenu = menu;
        button.Click += (_, _) => { beforeOpen?.Invoke(); menu.Open(button); };
        return button;
    }
    internal void Style(string path, string value)
    {
        if (Snapshot is null) return;
        Command("style", new { area = CellArea.FromView(Snapshot.View), path, value });
    }
    private void ApplyLocaleDateFormat(string key)
    {
        if (Session is not { } model) return;
        Run(() =>
        {
            var format = model.GetFmtSettings().GetProperty(key).GetString();
            if (string.IsNullOrWhiteSpace(format)) throw new InvalidOperationException($"Missing {key} format for the workbook locale.");
            Style("num_fmt", format);
        });
    }
    internal void ToggleFont(string name)
    {
        var font = Snapshot?.Cell.ExtendedStyle.Style.Font ?? new();
        var active = name switch { "b" => font.B, "i" => font.I, "u" => font.U, "strike" => font.Strike, _ => false };
        Style("font." + name, (!active).ToString().ToLowerInvariant());
    }
    internal void Clear(string kind)
    {
        if (Snapshot is not null) Command("clear", new { area = CellArea.FromView(Snapshot.View), kind });
    }
    internal StackPanel OpenDrawer(LocalizedText title, string? feature = null, Action? back = null, LocalizedText? backLabel = null)
    {
        CommitEdit();
        _captureDrawerState = null;
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(12, 16) };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Height = 40, ColumnSpacing = 4, Margin = new Thickness(8, 0) };
        if (back is not null)
        {
            var previous = IconButton("ChevronLeft", back, backLabel ?? Text.Key("common.cancel"));
            header.Children.Add(previous);
        }
        var heading = title.Bind(new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, TextBlock.TextProperty);
        Grid.SetColumn(heading, 1); header.Children.Add(heading);
        var close = IconButton("X", () =>
        {
            _drawer.IsVisible = false; RefreshDrawerButtons();
            FocusCell();
        }, Text.Key("right_drawer.close"));
        Grid.SetColumn(close, 2); header.Children.Add(close);
        var headerFrame = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerFrame.Bind(Border.BorderBrushProperty, headerFrame.GetResourceObservable("Dialog-grey-300"));
        var layout = new Grid { RowDefinitions = new("40,*") };
        layout.Children.Add(headerFrame);
        var scroll = new DrawerScrollViewer { Content = panel };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        _drawer.Child = layout;
        _drawerFeature = feature ?? title.ResourceKey;
        _drawer.IsVisible = true; RefreshDrawerButtons(); return panel;
    }

    private void SetDrawerFooter(StackPanel panel, Control footer, string actionIcon = "Check")
    {
        if (_drawer.Child is not Grid layout || !layout.Children.OfType<ScrollViewer>().Any(scroll => ReferenceEquals(scroll.Content, panel))) return;
        void StyleActions()
        {
            var buttons = footer is Panel row ? row.Children.OfType<Button>().ToArray() : footer is Button one ? [one] : Array.Empty<Button>();
            for (var i = 0; i < buttons.Length; i++)
            {
                buttons[i].Classes.Remove("workbook-flat"); buttons[i].Classes.Add("dialog-button");
                buttons[i].Classes.Remove("primary"); buttons[i].Classes.Remove("ghost");
                buttons[i].ContentTemplate = null;
                if (i == buttons.Length - 1)
                {
                    buttons[i].Classes.Add("primary");
                    buttons[i].ContentTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<object>((label, _) =>
                    {
                        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        content.Children.Add(new WorkbookIcon(actionIcon)); content.Children.Add(new TextBlock { Text = label?.ToString(), VerticalAlignment = VerticalAlignment.Center });
                        return content;
                    });
                }
            }
        }
        StyleActions();
        if (footer is Panel actions) actions.Children.CollectionChanged += (_, _) => StyleActions();
        footer.HorizontalAlignment = footer is Panel ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        if (footer is StackPanel row)
            row.SizeChanged += (_, _) => { if (row.Children.Count == 2) row.Spacing = Math.Max(8, row.Bounds.Width - row.Children.Sum(c => c.DesiredSize.Width)); };
        var frame = new Border { Child = footer, Padding = new Thickness(8), BorderThickness = new Thickness(0, 1, 0, 0) };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Dialog-grey-300"));
        layout.RowDefinitions = new RowDefinitions("40,*,Auto");
        Grid.SetRow(frame, 2); layout.Children.Add(frame);
    }
}
