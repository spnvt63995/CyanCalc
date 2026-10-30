using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CyanCalc.Controls.Preview;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    // NamedRanges/EditNamedRange and util.getFullRangeToString at the pinned upstream version.
    private string SelectedNamedRange()
    {
        var model = Session!;
        var view = model.GetSelectedView(); var area = CellArea.FromView(view);
        string Cell(int row, int column) => $"${CellAddress.ColumnName(column)}${row}";
        var start = Cell(area.Row, area.Column);
        var end = area.Width == 1 && area.Height == 1 ? "" : ":" + Cell(area.Row + area.Height - 1, area.Column + area.Width - 1);
        return ReferenceUtils.QuoteName(model.GetWorksheetsProperties()[view.Sheet].Name) + "!" + start + end;
    }

    public void ShowNames() => OpenDrawerFeature("name_manager_dialog.title", () => ShowNames("", "all"));
    private void ShowNames(string query, string scopeFilter)
    {
        if (Session is not { } model || Snapshot is null) return;
        var panel = OpenDrawer(Text.Key("name_manager_dialog.title"));
        panel.Margin = default; panel.Spacing = 0;
        var filterRow = new Grid { Name = "NamedRangeFilters", ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, Height = 40, Margin = new Thickness(8, 0) };
        var filters = new Border { Child = filterRow, BorderThickness = new Thickness(0, 0, 0, 1) };
        filters.Bind(Border.BorderBrushProperty, filters.GetResourceObservable("Dialog-grey-300")); panel.Children.Add(filters);
        var searchLabel = Text.Key("name_manager_dialog.search_placeholder");
        var search = searchLabel.Describe(new PanelTextBox { Name = "NamedRangeSearch", Text = query, Height = 28, MinWidth = 0,
            Padding = new Thickness(36, 0, 8, 0), BorderThickness = default, CornerRadius = default, Classes = { "named-range-search" } });
        searchLabel.Bind(search, TextBox.WatermarkProperty);
        search.GotFocus += (_, _) => { if (CanUseConditionalPanel(model, panel)) search.SelectAll(); };
        var searchIcon = new WorkbookIcon("Search", 16) { Margin = new Thickness(8, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        searchIcon.Bind(ForegroundProperty, searchIcon.GetResourceObservable("Dialog-grey-500"));
        filterRow.Children.Add(new Grid { Children = { search, searchIcon } });
        var scopeControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(scopeControls, 1); filterRow.Children.Add(scopeControls);
        var scopeLabel = Label(Text.Key("name_manager_dialog.scope_label")); scopeLabel.VerticalAlignment = VerticalAlignment.Center;
        scopeLabel.Bind(TextBlock.ForegroundProperty, scopeLabel.GetResourceObservable("Dialog-grey-600")); scopeControls.Children.Add(scopeLabel);
        var filter = Choice(scopeControls, Text.Key("name_manager_dialog.scope_label"), ["all", "global", "current"], scopeFilter,
            value => Text.Key("name_manager_dialog.scope_filter_" + value), "NamedRangeFilter", showLabel: false);
        filter.Height = 28; filter.MinHeight = 0; filter.MaxWidth = 100; filter.Padding = new Thickness(8, 0, 0, 0); filter.Classes.Add("named-range-scope");
        var list = new StackPanel { Name = "NamedRangeList" }; panel.Children.Add(list);
        NamesListState ListState() => new(search.Text ?? "", (string?)filter.SelectedItem ?? "all");
        _captureDrawerState = ListState;
        void Edit(DefinedName? name) { if (CanUseConditionalPanel(model, panel)) EditName(name, ListState()); }
        var add = Button(Text.Key("name_manager_dialog.new"), () => Edit(null)); add.IsEnabled = CanEdit; SetDrawerFooter(panel, add, "Plus");
        var empty = SetDrawerListBody(panel, list);
        (DefinedName Name, string Scope)[]? rendered = null;
        var rows = new List<(NamedRangeListItem Row, DefinedName Name)>();
        bool? renderedEditable = null;
        string Normalize(string value) => value.Trim().Replace("'", "").Replace("\"", "");
        void UpdateSelection()
        {
            var selected = Normalize(SelectedNamedRange());
            foreach (var (row, name) in rows) row.SetSelected(Normalize(name.Formula) == selected);
        }
        void RenderNames()
        {
            if (!ReferenceEquals(Session, model) || panel.GetVisualRoot() is null) return;
            var names = model.GetDefinedNameList(); var worksheets = model.GetWorksheetsProperties();
            filters.IsVisible = names.Length > 0;
            var q = search.Text?.Trim() ?? "";
            var entries = names.Where(name => !((filter.SelectedItem as string == "global" && name.Scope is not null) ||
                (filter.SelectedItem as string == "current" && name.Scope != model.GetSelectedView().Sheet)))
                .Select(name => (Name: name, Scope: name.Scope is { } i && i >= 0 && i < worksheets.Length ? worksheets[i].Name : ""))
                .Where(entry => entry.Name.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (entry.Name.Scope is null ? Text["name_manager_dialog.scope_filter_global"] : entry.Scope).Contains(q, StringComparison.OrdinalIgnoreCase)).ToArray();
            empty.IsVisible = entries.Length == 0;
            if (empty.IsVisible) empty.Content = names.Length == 0
                ? DrawerEmptyMessage("PackageOpen", "name_manager_dialog.empty_message1", "name_manager_dialog.empty_message2")
                : DrawerEmptyMessage("SearchX", "name_manager_dialog.no_search_results");
            // Selection changes retain the rows, their focus and hover transition.
            if (rendered is not null && rendered.SequenceEqual(entries) && renderedEditable == CanEdit) { UpdateSelection(); return; }
            rendered = entries; renderedEditable = CanEdit; list.Children.Clear(); rows.Clear();
            foreach (var (name, scope) in entries)
            {
                var row = new NamedRangeListItem(name.Name, name.Scope is null ? "[Global]" : scope.Length > 0 ? scope : "[Unknown]", name.Formula, () =>
                {
                    if (CanUseConditionalPanel(model, panel)) Run(() => { if (TryNavigateName(name)) FocusCell(); });
                });
                bool Current() => CanUseConditionalPanel(model, panel) && list.Children.Contains(row);
                row.AddAction(Text.Key("name_manager_dialog.edit"), "PencilLine", () => { if (Current()) Edit(name); }, CanEdit);
                row.AddAction(Text.Key("name_manager_dialog.delete"), "Trash2", () =>
                {
                    if (Current()) Command("delete_defined_name", new { name = name.Name, scope = name.Scope });
                }, CanEdit);
                list.Children.Add(row); rows.Add((row, name));
            }
            UpdateSelection();
        }
        search.TextChanged += (_, _) => RenderNames(); filter.SelectionChanged += (_, _) => RenderNames();
        PreviewPanelBinding.Bind(panel, model, RenderNames);
        Dispatcher.UIThread.Post(() => { if (filters.IsVisible && CanUseConditionalPanel(model, panel)) search.Focus(); });
    }

    private void EditName(DefinedName? existing, NamesListState? listState = null, NameDraftState? draft = null)
    {
        if (!CanEdit || Session is not { } model || Snapshot is null) return;
        listState ??= new("", "all");
        void Back() => ShowNames(listState.Query, listState.Filter);
        var panel = OpenDrawer(Text.Key(existing is null ? "name_manager_dialog.add_new_range" : "name_manager_dialog.edit_range"),
            feature: "name_manager_dialog.title", back: Back, backLabel: Text.Key("name_manager_dialog.back_to_list"));
        var prefix = Text["name_manager_dialog.default_range_prefix"]; var defaultName = prefix + "1";
        for (var number = 1; model.GetDefinedNameList().Any(n => n.Name == defaultName && n.Scope is null); ) defaultName = prefix + ++number;
        var preview = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Spacing = 16;
        var tag = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new WorkbookIcon("Tag", 16) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        tag.Bind(Border.BackgroundProperty, tag.GetResourceObservable("Dialog-grey-100"));
        ((WorkbookIcon)tag.Child).Bind(WorkbookIcon.ForegroundProperty, tag.GetResourceObservable("Dialog-grey-600"));
        panel.Children.Add(DrawerPreview(tag, preview));
        var name = Field(panel, Text.Key("name_manager_dialog.range_name"), draft?.Name ?? existing?.Name ?? defaultName, "NamedRangeName");
        // Match upstream Input.onFocus; pointer placement may then collapse the selection.
        name.GotFocus += (_, _) => { if (CanUseConditionalPanel(model, panel)) name.SelectAll(); };
        Text.Key("name_manager_dialog.enter_range_name").Bind(name, TextBox.WatermarkProperty);
        var nameError = new TextBlock { Name = "NamedRangeNameError", Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(nameError);
        var worksheets = model.GetWorksheetsProperties();
        int? scopeId = draft is not null ? draft.ScopeSheetId : existing?.Scope is { } index ? worksheets[index].SheetId : null;
        var scope = Choice(panel, Text.Key("name_manager_dialog.scope_label"), ["[Global]"], name: "NamedRangeScope");
        scope.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((value, _) =>
        {
            if (value != "[Global]") return new TextBlock { Text = value };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Text.Key("name_manager_dialog.workbook").Block());
            var global = Text.Compose(t => " " + t["name_manager_dialog.global"]).Block();
            global.Bind(TextBlock.ForegroundProperty, global.GetResourceObservable("Dialog-grey-400")); row.Children.Add(global); return row;
        });
        var helper = Label(Text.Key("name_manager_dialog.scope_helper")); helper.LineHeight = 16.8;
        helper.Bind(TextBlock.ForegroundProperty, helper.GetResourceObservable("Dialog-grey-500")); ((StackPanel)scope.Parent!).Children.Add(helper);
        var formula = Field(panel, Text.Key("name_manager_dialog.refers_to"), draft?.Formula ?? existing?.Formula ?? SelectedNamedRange(), "NamedRangeFormula");
        formula.AcceptsReturn = true; formula.TextWrapping = TextWrapping.Wrap; formula.Height = 82;
        formula.Padding = new Thickness(10, 8, 32, 8); formula.VerticalContentAlignment = VerticalAlignment.Top;
        Text.Key("name_manager_dialog.enter_formula").Bind(formula, TextBox.WatermarkProperty);
        var useSelection = IconButton("SquareMousePointer", () => formula.Text = SelectedNamedRange(), Text.Key("name_manager_dialog.use_selection"));
        useSelection.Width = useSelection.Height = 24; useSelection.Padding = new Thickness(4);
        useSelection.Classes.Remove("workbook-flat"); useSelection.Classes.Add("dialog-button");
        useSelection.HorizontalAlignment = HorizontalAlignment.Right; useSelection.VerticalAlignment = VerticalAlignment.Top; useSelection.Margin = new Thickness(4);
        var formulaField = (StackPanel)formula.Parent!; formulaField.Children.Remove(formula);
        formulaField.Children.Add(new Grid { Children = { formula, useSelection } });
        var formulaError = new TextBlock { Name = "NamedRangeFormulaError", Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(formulaError);
        nameError.Text = draft?.NameError ?? ""; formulaError.Text = draft?.FormulaError ?? "";
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        actions.Children.Add(Button(Text.Key("name_manager_dialog.cancel"), Back));
        var apply = Button(Text.Key("name_manager_dialog.apply"), () => { }); apply.Name = "NamedRangeApply"; actions.Children.Add(apply); SetDrawerFooter(panel, actions);
        _captureDrawerState = () => new NameDraftState(existing, name.Text ?? "", formula.Text ?? "", scopeId,
            nameError.Text ?? "", formulaError.Text ?? "", listState);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        bool syncing = false;
        int? ScopeIndex()
        { var i = Array.FindIndex(model.GetWorksheetsProperties(), s => s.SheetId == scopeId); return i < 0 ? null : i; }
        void Update()
        {
            preview.Text = string.IsNullOrEmpty(name.Text) ? Text["name_manager_dialog.new_named_range"] : name.Text;
            nameError.IsVisible = !string.IsNullOrEmpty(nameError.Text); formulaError.IsVisible = !string.IsNullOrEmpty(formulaError.Text);
            apply.IsEnabled = CanEdit && !nameError.IsVisible && !formulaError.IsVisible;
        }
        void Error(string message)
        {
            (nameError.Text, formulaError.Text) = message.StartsWith("Name: ", StringComparison.Ordinal) ? (message[6..], "") :
                message.StartsWith("Scope: ", StringComparison.Ordinal) ? (message[7..], "") :
                ("", message.StartsWith("Formula: ", StringComparison.Ordinal) ? message[9..] : message);
            Update();
        }
        void Validate()
        {
            timer.Stop(); if (!CanUseConditionalPanel(model, panel)) return;
            try { model.IsValidDefinedName(name.Text ?? "", ScopeIndex(), formula.Text ?? ""); }
            catch (IronCalcException e)
            {
                if (existing is null || !e.Message.Contains("already exists", StringComparison.Ordinal)) { Error(e.Message); return; }
            }
            nameError.Text = formulaError.Text = ""; Update();
        }
        void Schedule() { timer.Stop(); timer.Start(); Update(); }
        void Save()
        {
            if (!CanUseConditionalPanel(model, panel) || !apply.IsEnabled) return;
            try
            {
                if (existing is null) model.NewDefinedName((name.Text ?? "").Trim(), ScopeIndex(), formula.Text ?? "");
                else model.UpdateDefinedName(existing.Name, existing.Scope, (name.Text ?? "").Trim(), ScopeIndex(), formula.Text ?? "");
                Back();
            }
            catch (IronCalcException e) { Error(e.Message); }
        }
        timer.Tick += (_, _) => Validate();
        apply.Click += (_, _) => Save();
        name.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Schedule(); };
        formula.PropertyChanged += (_, e) => { if (e.Property != TextBox.TextProperty) return; formulaError.Text = ""; Schedule(); };
        scope.SelectionChanged += (_, _) =>
        {
            if (syncing || !ReferenceEquals(Session, model) || panel.GetVisualRoot() is null) return;
            scopeId = model.GetWorksheetsProperties().FirstOrDefault(s => s.Name == scope.SelectedItem as string)?.SheetId; Schedule();
        };
        PreviewPanelBinding.Bind(panel, model, () =>
        {
            var sheets = model.GetWorksheetsProperties(); syncing = true;
            scope.ItemsSource = new[] { "[Global]" }.Concat(sheets.Select(s => s.Name)).ToArray();
            scope.SelectedItem = sheets.FirstOrDefault(s => s.SheetId == scopeId)?.Name ?? "[Global]";
            syncing = false; Schedule();
        });
        panel.DetachedFromVisualTree += (_, _) => timer.Stop();
        panel.AddHandler(KeyDownEvent, (_, e) =>
        { if (e.Key == Key.Enter && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) { Save(); e.Handled = true; } }, RoutingStrategies.Tunnel);
        Update(); Schedule();
        if (draft is null) Dispatcher.UIThread.Post(() => { if (CanUseConditionalPanel(model, panel)) name.Focus(); });
    }
}
