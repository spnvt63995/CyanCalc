using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CyanCalc.Controls.Preview;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    // NamedStylesPanel.tsx category and tile order, not the engine registry order.
    private static readonly (string Key, string[] Names)[] StyleCategories =
    [
        ("good_bad_neutral", ["normal", "bad", "good", "neutral"]),
        ("data_and_model", ["calculation", "check cell", "explanatory text", "input", "linked cell", "note", "output", "warning text"]),
        ("titles_and_headings", ["heading 1", "heading 2", "heading 3", "heading 4", "title", "total"]),
        ("themed_cell_styles", Enumerable.Range(1, 6).SelectMany(i => new[] { $"20% - accent{i}", $"40% - accent{i}", $"60% - accent{i}", $"80% - accent{i}", $"accent{i}" }).ToArray()),
        ("number_format", ["comma", "comma [0]", "currency", "currency [0]", "percent"])
    ];
    public void ShowNamedStyles() => OpenDrawerFeature("named_styles.panel_title", () => ShowNamedStyles(false));
    private void ShowNamedStyles(bool manage)
    {
        if (Session is not { } model) return;
        var panel = OpenDrawer(Text.Key(manage ? "named_styles.manage_styles" : "named_styles.panel_title"), preserveEdit: true,
            feature: "named_styles.panel_title", back: manage ? () => ShowNamedStyles(false) : null, backLabel: Text.Key("named_styles.back_to_list"));
        _captureDrawerState = () => new StylesListState(manage);
        if (!manage) panel.Margin = new Thickness(0);
        var list = new StackPanel { Spacing = manage ? 8 : 0 }; panel.Children.Add(list);
        var add = Button(Text.Key("named_styles.add_new_style"), () => EditNamedStyle(null)); add.IsEnabled = CanEdit; SetDrawerFooter(panel, add, "Plus");
        string? signature = null;
        PreviewPanelBinding.Bind(panel, model, () =>
        {
            var registered = model.GetNamedStyleList();
            var builtins = model.Query("builtin_styles").EnumerateArray().ToArray();
            var builtinNames = builtins.Select(x => x[0].GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var custom = registered.Where(n => !builtinNames.Contains(n)).ToArray();
            var nextSignature = string.Join("|", builtins.Select(x => x.GetRawText())) + string.Join("|", custom.Select(name => name + model.Query("get_named_style", new { name }).GetRawText()));
            if (signature == nextSignature) return;
            signature = nextSignature; list.Children.Clear();
            StackPanel? customSection = null;
            if (custom.Length > 0 && !manage)
            {
                customSection = Section();
                var heading = new Grid { ColumnDefinitions = new("*,Auto"), Height = 17 };
                heading.Children.Add(SectionTitle("named_styles.section_custom"));
                var manageButton = Text.Key("named_styles.manage_styles").Describe(new WorkbookButton { Content = new WorkbookIcon("Settings2", 14), Width = 24, Height = 24, Padding = new Thickness(0), Classes = { "workbook-flat" } });
                manageButton.Click += (_, _) => ShowNamedStyles(true);
                Grid.SetColumn(manageButton, 1); heading.Children.Add(manageButton); customSection.Children.Add(heading);
            }
            if (manage && custom.Length == 0)
            { list.Children.Add(Label(Text.Key("named_styles.empty_message1"))); list.Children.Add(Label(Text.Key("named_styles.empty_message2"))); }
            var customs = new List<Control>();
            foreach (var name in custom)
            {
                var named = model.Query("get_named_style", new { name }); var tile = Tile(name, named.GetProperty("style"), true);
                if (!manage) { customs.Add(tile); continue; }
                var row = new StackPanel { Spacing = 4 }; row.Children.Add(tile);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var edit = Button(Text.Key("named_styles.update_style"), () => EditNamedStyle(name)); edit.IsEnabled = CanEdit;
                var delete = Button(Text.Key("named_styles.delete_style"), () => Command("delete_named_style", new { name })); delete.IsEnabled = CanEdit;
                actions.Children.Add(edit); actions.Children.Add(delete); row.Children.Add(actions); customs.Add(row);
                tile.IsHitTestVisible = false;
            }
            if (manage) { list.Children.Add(StyleGrid(customs, 1)); return; }
            customSection?.Children.Add(StyleGrid(customs));
            var themeSection = Section(); themeSection.Children.Add(SectionTitle("named_styles.section_theme"));
            foreach (var (key, names) in StyleCategories)
            {
                var category = new StackPanel { Spacing = 8 }; themeSection.Children.Add(category);
                var label = Label(Text.Key("named_styles.category_" + key)); label.FontWeight = FontWeight.Medium; category.Children.Add(label);
                var tiles = new StackPanel { Spacing = 4 }; category.Children.Add(tiles);
                var rows = key == "themed_cell_styles" ? names.GroupBy(name => name[^1]).Select(group => group.ToArray()).ToArray() : [names];
                foreach (var namesInRow in rows)
                {
                    var matching = namesInRow.Select(name => builtins.FirstOrDefault(x => x[0].GetString()!.Equals(name, StringComparison.OrdinalIgnoreCase))).Where(x => x.ValueKind != JsonValueKind.Undefined).ToArray();
                    if (matching.Length == 0) continue;
                    tiles.Children.Add(StyleGrid(matching.Select(builtin => Tile(builtin[0].GetString()!, builtin[1], false)),
                        key == "themed_cell_styles" ? matching.Length : 4, key == "themed_cell_styles" ? 4 : 6));
                }
            }
            StackPanel Section()
            {
                var content = new StackPanel { Spacing = 12 };
                var frame = new Border { Child = content, Padding = new Thickness(12, 16), BorderThickness = new Thickness(0, 0, 0, 1) };
                frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Dialog-grey-300")); list.Children.Add(frame);
                return content;
            }
            TextBlock SectionTitle(string key)
            {
                var title = Label(Text.Key(key)); title.FontSize = 14; title.FontWeight = FontWeight.SemiBold; return title;
            }
            NamedStylePreviewTile Tile(string name, JsonElement style, bool customStyle)
            {
                var tile = new NamedStylePreviewTile(name, StylePreviewResolver.Resolve(model, style), false, customStyle, Text);
                tile.Click += (_, _) => { if (ReferenceEquals(Session, model)) ApplyNamedStyle(name); };
                return tile;
            }
        });
    }
    private static Grid StyleGrid(IEnumerable<Control> tiles, int columns = 4, double gap = 6)
    {
        var grid = new Grid { ColumnDefinitions = new(string.Join(",", Enumerable.Repeat("*", columns))), ColumnSpacing = gap, RowSpacing = gap };
        foreach (var tile in tiles)
        {
            var index = grid.Children.Count;
            if (index % columns == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(tile, index % columns); Grid.SetRow(tile, index / columns); grid.Children.Add(tile);
        }
        return grid;
    }
    private void ApplyNamedStyle(string name)
    {
        if (CanEdit && Snapshot is not null) Command("apply_named_style", new { name });
    }
    private void EditNamedStyle(string? existing, StyleDraftState? draft = null)
    {
        if (!CanEdit || Session is not { } model) return;
        var manage = draft?.Manage ?? existing is not null;
        void Back() => ShowNamedStyles(manage);
        var panel = OpenDrawer(Text.Key(existing is null ? "named_styles.add_new_style" : "named_styles.update_style"),
            feature: "named_styles.panel_title", back: Back, backLabel: Text.Key("named_styles.back_to_list"));
        panel.Spacing = 0;
        var builtin = model.Query("builtin_styles").EnumerateArray().ToArray();
        var allNames = model.GetNamedStyleList().Concat(builtin.Select(x => x[0].GetString()!)).ToArray();
        var prefix = Text["named_styles.default_name_prefix"]; var defaultName = prefix + "1";
        for (var n = 1; allNames.Contains(defaultName, StringComparer.OrdinalIgnoreCase);) defaultName = prefix + ++n;
        var nameSection = new StackPanel { Spacing = 4 };
        var nameFrame = new Border { Child = nameSection, Padding = new Avalonia.Thickness(12, 16), Margin = new Avalonia.Thickness(-12, 0), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1) };
        nameFrame.Bind(Border.BorderBrushProperty, nameFrame.GetResourceObservable("Dialog-grey-200")); panel.Children.Add(nameFrame);
        var name = Field(nameSection, Text.Key("named_styles.name_label"), draft?.Name ?? existing ?? defaultName, name: "NamedStyleName");
        // Upstream Input selects its text on focus; wait until pointer caret placement finishes.
        name.GotFocus += (_, _) => Dispatcher.UIThread.Post(() => { if (name.IsFocused && CanUseConditionalPanel(model, panel)) name.SelectAll(); });
        var nameError = new TextBlock { Name = "NamedStyleNameError", IsVisible = false, Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }; nameSection.Children.Add(nameError);
        var original = existing is null || draft is not null ? default : model.Query("get_named_style", new { name = existing });
        var style = draft?.Editor.Style ?? JsonNode.Parse((existing is null ? builtin.First(x => x[0].GetString()!.Equals("Normal", StringComparison.OrdinalIgnoreCase))[1] : original.GetProperty("style")).GetRawText())!.AsObject();
        var editor = new StyleEditor(style, false, model, Text, this, draft?.Editor.Includes ?? (existing is null ? default : original.GetProperty("includes")), draft?.Editor);
        _captureDrawerState = () => new StyleDraftState(existing, name.Text ?? "", editor.CaptureDraft(), nameError.Text ?? "", manage);
        var previewTitle = Text.Compose(t => name.Text?.Trim() is { Length: > 0 } value ? value : t["named_styles.new_style"]);
        var previewName = previewTitle.Bind(new TextBlock { Name = "NamedStylePreviewName", FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold }, TextBlock.TextProperty);
        panel.Children.Insert(0, DrawerPreview(editor.TakePreview(), previewName));
        var properties = Label(Text.Key("named_styles.style_properties")); properties.FontSize = 14; properties.FontWeight = FontWeight.SemiBold;
        properties.Margin = new Avalonia.Thickness(0, 16, 0, 0); panel.Children.Add(properties);
        panel.Children.Add(editor);
        nameError.Text = draft?.NameError ?? "";
        IDisposable? errorBinding = null;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        actions.Children.Add(Button(Text.Key("named_styles.cancel"), Back));
        var apply = Text.Key("named_styles.apply").Bind(new WorkbookButton { Name = "NamedStyleApply" }, ContentControl.ContentProperty); actions.Children.Add(apply); SetDrawerFooter(panel, actions);
        apply.Click += (_, _) => Save();
        void Update()
        {
            nameError.IsVisible = !string.IsNullOrEmpty(nameError.Text);
            apply.IsEnabled = CanEdit && editor.CanApply && !string.IsNullOrWhiteSpace(name.Text) && !nameError.IsVisible;
        }
        name.PropertyChanged += (_, e) => { if (e.Property != TextBox.TextProperty) return; previewName.SetCurrentValue(TextBlock.TextProperty, previewTitle.Value); errorBinding?.Dispose(); nameError.Text = ""; Update(); };
        editor.Changed += Update; Update();
        void Save()
        {
            if (!apply.IsEnabled || !CanUseConditionalPanel(model, panel)) return;
            var newName = name.Text!.Trim();
            if (allNames.Any(n => n.Equals(newName, StringComparison.OrdinalIgnoreCase) && !n.Equals(existing, StringComparison.OrdinalIgnoreCase)))
            { errorBinding?.Dispose(); errorBinding = Text.Key("named_styles.name_already_exists").BindTo(nameError, TextBlock.TextProperty); Update(); return; }
            try
            {
                var includes = editor.Includes; includes["protection"] = true;
                if (existing is null) model.Execute("create_named_style", new { name = newName, style = editor.Value(), includes });
                else model.Execute("update_named_style", new { name = existing, new_name = newName, style = editor.Value(), includes });
                Back();
            }
            catch (IronCalcException error) { nameError.Text = error.Message; Update(); }
        }
        panel.KeyDown += (_, e) => { if (e.Key == Key.Enter && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) { Save(); e.Handled = true; } };
        // Upstream Input autoFocus runs when the editor mounts, not when its model changes.
        if (draft is null) Dispatcher.UIThread.Post(() => { if (CanUseConditionalPanel(model, panel)) name.Focus(); });
    }
}
