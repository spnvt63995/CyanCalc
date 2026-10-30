using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using CyanCalc.Controls.Preview;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    private readonly ConditionalRuleReorder _conditionalReorder = new();
    private string SelectedConditionalRange()
    {
        var area = CellArea.FromView(Session!.GetSelectedView());
        return area.Width == 1 && area.Height == 1 ? CellAddress.Format(area.Row, area.Column) : area.Address;
    }
    public void ShowConditionalFormats() => OpenDrawerFeature("conditional_formatting.title", () => ShowConditionalFormats("", "sheet"));
    private void ShowConditionalFormats(string query, string scopeFilter)
    {
        if (Session is not { } model) return;
        var panel = OpenDrawer(Text.Key("conditional_formatting.title"));
        panel.Margin = default; panel.Spacing = 0;
        var filterRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 4, Height = 40, Margin = new Thickness(8, 0) };
        var filters = new Border { Child = filterRow, BorderThickness = new Thickness(0, 0, 0, 1) };
        filters.Bind(Border.BorderBrushProperty, filters.GetResourceObservable("Dialog-grey-300")); panel.Children.Add(filters);
        var searchLabel = Text.Key("conditional_formatting.search_placeholder");
        var search = searchLabel.Describe(new PanelTextBox { Name = "ConditionalSearch", Text = query, Height = 28, MinWidth = 0,
            Padding = new Thickness(36, 0, 8, 0), BorderThickness = default, CornerRadius = default, Classes = { "conditional-search" } });
        searchLabel.Bind(search, TextBox.WatermarkProperty);
        var searchIcon = new WorkbookIcon("Search", 16) { Margin = new Thickness(8, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        searchIcon.Bind(WorkbookIcon.ForegroundProperty, searchIcon.GetResourceObservable("Dialog-grey-400"));
        filterRow.Children.Add(new Grid { Children = { search, searchIcon } });
        var scope = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(scope, 1); filterRow.Children.Add(scope);
        var scopeLabel = Label(Text.Key("conditional_formatting.filter_label")); scopeLabel.VerticalAlignment = VerticalAlignment.Center;
        scopeLabel.Bind(TextBlock.ForegroundProperty, scopeLabel.GetResourceObservable("Dialog-grey-600")); scope.Children.Add(scopeLabel);
        var filter = Choice(scope, Text.Key("conditional_formatting.filter_label"), ["sheet", "selection"], scopeFilter,
            value => Text.Key("conditional_formatting.filter_" + (value == "sheet" ? "this_sheet" : "selection")), name: "ConditionalScope", showLabel: false);
        filter.Height = 28; filter.MinHeight = 0; filter.MaxWidth = 100; filter.Padding = new Thickness(8, 0, 0, 0); filter.Classes.Add("conditional-scope");
        ConditionalListState ListState() => new(search.Text ?? "", (string?)filter.SelectedItem ?? "sheet");
        _captureDrawerState = ListState;
        var list = new StackPanel { Name = "ConditionalRuleList" }; panel.Children.Add(list);
        var addRule = Button(Text.Key("conditional_formatting.new"), () => EditConditionalFormat(null, ListState())); addRule.IsEnabled = CanEdit; SetDrawerFooter(panel, addRule, "Plus");
        var empty = SetDrawerListBody(panel, list);
        string? signature = null;
        var rows = new List<(ConditionalRuleListItem Row, string Range, int Index)>();
        void RenderRules()
        {
            if (!ReferenceEquals(Session, model) || panel.GetVisualRoot() is null) return;
            var sheet = model.GetSelectedView().Sheet;
            var rules = model.GetConditionalFormattingList(sheet).EnumerateArray().Where(rule => ConditionalRuleEditor.Types.Contains(rule.GetProperty("cf_rule").GetProperty("type").GetString())).ToArray();
            var selection = SelectedConditionalRange();
            var entries = rules.Select(stored =>
            {
                var data = JsonNode.Parse(stored.GetProperty("cf_rule").GetRawText())!.AsObject();
                if (data.Remove("dxf_id")) data["format"] = JsonNode.Parse(model.Query("cf_format", new { sheet, index = stored.GetProperty("index").GetInt32() }).GetRawText());
                return (Rule: stored.Clone(), Data: data, Description: Text.Compose(t => ConditionalRulePresentation.Description(data, t, ResolveConditionalValue)));
            }).ToArray();
            // Selection changes only update row decoration; retain controls and scroll position.
            var nextSignature = JsonSerializer.Serialize(new { sheet, Query = search.Text, Filter = filter.SelectedItem,
                Selection = filter.SelectedItem as string == "selection" ? selection : "", Theme = model.GetTheme(),
                Rules = entries.Select(e => new { e.Rule, e.Data, Description = e.Description.Value }) });
            if (signature == nextSignature)
            {
                foreach (var (row, range, _) in rows) row.SetSelected(ConditionalRulePresentation.Covers(selection, range));
                return;
            }
            // Replacing the animated rows cancels their presentation, but keeps the
            // accepted move, just like upstream's animationcancel handler.
            if (_conditionalReorder.IsRunning) { _conditionalReorder.Finish(); RenderRules(); return; }
            signature = nextSignature; list.Children.Clear(); rows.Clear(); empty.IsVisible = false;
            filters.IsVisible = rules.Length > 0;
            if (rules.Length == 0)
            {
                empty.Content = DrawerEmptyMessage("PackageOpen", "conditional_formatting.empty_message1", "conditional_formatting.empty_message2");
                empty.IsVisible = true; return;
            }
            foreach (var (entry, position) in entries.Select((entry, position) => (entry, position)))
            {
                var rule = entry.Rule; var data = entry.Data; var description = entry.Description;
                var index = rule.GetProperty("index").GetInt32(); var range = rule.GetProperty("range").GetString()!;
                var selected = ConditionalRulePresentation.Covers(selection, range);
                if (filter.SelectedItem as string == "selection" && !selected) continue;
                // Upstream searches getRuleDescription even when the displayed title is Ratings.
                var queryText = search.Text?.Trim() ?? "";
                if (!range.Contains(queryText, StringComparison.OrdinalIgnoreCase) && !description.Value.Contains(queryText, StringComparison.OrdinalIgnoreCase)) continue;
                var title = data["type"]!.GetValue<string>() == "IconRating" ? Text.Key("conditional_formatting.icon_sets_ratings") : description;
                var row = new ConditionalRuleListItem(position + 1, title, range, new ConditionalRuleListPreview(data, model), () =>
                {
                    if (!ReferenceEquals(Session, model) || panel.GetVisualRoot() is null) return;
                    var area = ConditionalRulePresentation.ParseRange(range.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0]);
                    if (area is null) return;
                    model.SetSelectedCell(area.Value.Top, area.Value.Left); model.SetSelectedRange(area.Value.Top, area.Value.Left, area.Value.Bottom, area.Value.Right);
                    FocusCell(); // Upstream onUpdate returns focus to the workbook after selecting a row.
                });
                row.SetSelected(selected);
                bool Current() => CanUseConditionalPanel(model, panel) && list.Children.Contains(row);
                void Add(string key, string icon, Action action, bool enabled = true, bool order = false) =>
                    row.AddAction(Text.Key("conditional_formatting." + key), icon, action, CanEdit && enabled, order);
                Add("move_up", "ChevronUp", () => Move(false), position > 0, order: true);
                Add("move_down", "ChevronDown", () => Move(true), position < rules.Length - 1, order: true);
                Add("edit_rule", "PencilLine", () => { if (Current()) EditConditionalFormat(rule, ListState()); });
                Add("duplicate_rule", "Copy", () => Run(() => { if (Current()) { model.Execute("add_cf", new { sheet, range, rule = data }); FocusCell(); } }));
                Add("delete_rule", "Trash2", () => Change("delete_cf")); list.Children.Add(row); rows.Add((row, range, index));
                void Change(string operation) { if (Current()) { Command(operation, new { sheet, index }); FocusCell(); } }
                void Move(bool lower)
                {
                    if (_conditionalReorder.IsRunning || !Current() || !CommitEdit() || !Current()) return;
                    var neighborPosition = position + (lower ? 1 : -1);
                    if (neighborPosition < 0 || neighborPosition >= rules.Length) return;
                    var neighborIndex = rules[neighborPosition].GetProperty("index").GetInt32();
                    var neighbor = rows.FirstOrDefault(item => item.Index == neighborIndex).Row;
                    _conditionalReorder.Start(row, neighbor, lower, ConditionalRuleReorder.PrefersReducedMotion, () => Run(() =>
                    {
                        // Retain the originating document/sheet until completion; never
                        // dispatch a delayed command through a newly assigned Session.
                        model.Execute(lower ? "lower_cf" : "raise_cf", new { sheet, index });
                        if (ReferenceEquals(Session, model) && panel.GetVisualRoot() is not null) FocusCell();
                    }));
                }
            }
            if (list.Children.Count == 0)
            {
                empty.Content = DrawerEmptyMessage("SearchX", "conditional_formatting.no_search_results"); empty.IsVisible = true;
            }
        }
        search.TextChanged += (_, _) => RenderRules(); filter.SelectionChanged += (_, _) => RenderRules();
        PreviewPanelBinding.Bind(panel, model, RenderRules);
    }
    private string ResolveConditionalValue(string value)
    {
        if (Session is null || string.IsNullOrWhiteSpace(value)) return value;
        var tokens = ReferenceUtils.GetTokens(value);
        if (tokens.GetArrayLength() == 0) return value;
        var token = tokens[0].GetProperty("token");
        if (token.ValueKind != JsonValueKind.Object) return value;
        JsonElement reference; JsonElement cell;
        if (token.TryGetProperty("Range", out reference)) cell = reference.GetProperty("left");
        else if (token.TryGetProperty("Reference", out reference)) cell = reference;
        else return value;
        if (reference.GetProperty("sheet").ValueKind != JsonValueKind.String) return value;
        var sheet = Array.FindIndex(Session.GetWorksheetsProperties(), x => x.Name == reference.GetProperty("sheet").GetString());
        if (sheet < 0) return value;
        var result = Session.GetFormattedCellValue(sheet, cell.GetProperty("row").GetInt32(), cell.GetProperty("column").GetInt32());
        return result.Length == 0 ? "\"\"" : result;
    }
    private void EditConditionalFormat(JsonElement? existing, ConditionalListState? listState = null, ConditionalDraftState? draft = null)
    {
        if (!CanEdit || Session is not { } model || Snapshot is null) return;
        listState ??= new("", "sheet");
        void Back() => ShowConditionalFormats(listState.Query, listState.Filter);
        var panel = OpenDrawer(Text.Key(existing is null ? "conditional_formatting.add_new_rule" : "conditional_formatting.edit_rule"),
            feature: "conditional_formatting.title", back: Back, backLabel: Text.Key("conditional_formatting.back_to_list"));
        var rangePanel = new StackPanel { Spacing = 12 };
        var rangeTitle = Label(Text.Key("conditional_formatting.apply_to")); rangeTitle.FontSize = 14; rangeTitle.LineHeight = 21; rangeTitle.FontWeight = FontWeight.SemiBold; rangePanel.Children.Add(rangeTitle);
        var range = Field(rangePanel, Text.Key("conditional_formatting.apply_to_range"), draft?.Range ?? existing?.GetProperty("range").GetString() ?? SelectedConditionalRange(), name: "ConditionalApplyTo");
        var useSelection = IconButton("SquareMousePointer", () => range.Text = SelectedConditionalRange(), Text.Key("conditional_formatting.use_selection"));
        useSelection.Width = useSelection.Height = 24; useSelection.Padding = new Avalonia.Thickness(4); useSelection.HorizontalAlignment = HorizontalAlignment.Right; useSelection.Margin = new Avalonia.Thickness(0, 0, 4, 0);
        var rangeField = (StackPanel)range.Parent!;
        var rangeLabel = (TextBlock)rangeField.Children[0]; rangeLabel.LineHeight = 18; rangeLabel.FontWeight = FontWeight.Medium;
        rangeField.Children.Remove(range);
        range.Padding = new Avalonia.Thickness(10, 0, 32, 0); rangeField.Children.Add(new Grid { Children = { range, useSelection } });
        JsonObject? rule = draft is null && existing is { } value ? JsonNode.Parse(value.GetProperty("cf_rule").GetRawText())!.AsObject() : null;
        if (rule is not null && rule.Remove("dxf_id")) rule["format"] = JsonNode.Parse(model.Query("cf_format", new { sheet = Snapshot.View.Sheet, index = existing!.Value.GetProperty("index").GetInt32() }).GetRawText());
        var editor = new ConditionalRuleEditor(rule, Text, model, this, SelectedConditionalRange, ResolveConditionalValue, rangePanel, draft?.Rule); panel.Children.Add(editor);
        _captureDrawerState = () => new ConditionalDraftState(existing?.Clone(), range.Text ?? "", editor.CaptureDraft(), listState);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 }; SetDrawerFooter(panel, actions);
        actions.Children.Add(Button(Text.Key("conditional_formatting.cancel"), Back));
        var apply = Button(Text.Key("conditional_formatting.apply"), () => Run(() =>
        {
            if (!CanUseConditionalPanel(model, panel) || !editor.CanApply || !editor.HasSerializableRule || !ConditionalRulePresentation.ValidRanges(range.Text ?? "")) return;
            var sheet = model.GetSelectedView().Sheet;
            if (existing is { } original) model.Execute("update_cf", new { sheet, index = original.GetProperty("index").GetInt32(), range = range.Text, rule = editor.Value() });
            else model.Execute("add_cf", new { sheet, range = range.Text, rule = editor.Value() });
            Back();
        })); apply.Name = "ConditionalApply"; actions.Children.Add(apply);
        void Update() => apply.IsEnabled = CanEdit && !string.IsNullOrWhiteSpace(range.Text) && editor.CanApply;
        editor.Changed += Update; range.TextChanged += (_, _) => Update(); Update();
    }
    private bool CanUseConditionalPanel(UserModel model, StackPanel panel) =>
        CanEdit && ReferenceEquals(Session, model) && _drawer.IsVisible && _drawer.Child?.GetVisualDescendants().Contains(panel) == true;
}
