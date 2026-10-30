using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;

namespace CyanCalc.Controls;

/// <summary>Workbook sheet navigation; mutations and edit policy stay with the host view.</summary>
public sealed class SheetTabBar : UserControl
{
    internal Action<Control, int>? ColorPickerRequested { get; set; }
    internal Func<bool>? PreserveEditorFocus { get; set; }
    private readonly WorkbookText _text;
    private readonly Action<int> _select;
    private readonly Func<string, object?, bool> _command;
    private readonly Action _settings;
    private readonly Action<string> _report;
    private readonly Func<(Guid Identity, long Revision)> _continuity;
    private readonly Func<WorkbookSnapshot?> _liveSnapshot;
    private readonly Button _add;
    private readonly Button _list;
    private readonly Button _regional;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 3 };
    private readonly ScrollViewer _scroller;
    private WorkbookSnapshot? _snapshot;
    private Guid _tabsIdentity;
    private Func<JsonElement, string>? _resolveColor;
    private bool _canEdit;
    private int? _renamingSheetId;
    private TextBox? _renameInput;
    private bool _finishingRename;

    public SheetTabBar(Action<int> select, Func<string, object?, bool> command, Action settings, Action<string> report,
        Func<(Guid Identity, long Revision)> continuity, Func<WorkbookSnapshot?> liveSnapshot, WorkbookText? text = null)
    {
        _text = text ?? new WorkbookText();
        _select = select;
        _command = command;
        _settings = settings;
        _report = report;
        _continuity = continuity;
        _liveSnapshot = liveSnapshot;
        Name = "SheetTabBar";
        MinHeight = 39;
        Background = SheetPalette.For(ActualThemeVariant).Chrome;
        _add = SmallButton(new WorkbookIcon("Plus"), _text.Key("navigation.add_sheet"), () => Mutate("new_sheet"));
        _add.Name = "AddSheet";
        _list = SmallButton(new WorkbookIcon("Menu"), _text.Key("navigation.sheet_list"), OpenList);
        _list.Name = "SheetList";
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(7, 0) };
        left.Children.Add(_add);
        left.Children.Add(_list);
        _scroller = new ScrollViewer
        {
            Name = "SheetTabsScroller", Content = _tabs,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(4, 0)
        };
        _regional = SmallButton(new WorkbookIcon("Settings"), _text.Key("regional_settings.open_regional_settings"), _settings);
        _regional.Name = "SheetRegionalSettings";
        _regional.Margin = new Thickness(6, 0);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        layout.Children.Add(left);
        Grid.SetColumn(_scroller, 1); layout.Children.Add(_scroller);
        Grid.SetColumn(_regional, 2); layout.Children.Add(_regional);
        Content = layout;
        SizeChanged += (_, _) => UpdateRegionalLabel();
        ActualThemeVariantChanged += (_, _) =>
        {
            Background = SheetPalette.For(ActualThemeVariant).Chrome;
            if (_snapshot is not null && _resolveColor is not null) Refresh(_snapshot, _canEdit, _resolveColor);
        };
    }

    public void Refresh(WorkbookSnapshot snapshot, bool canEdit, Func<JsonElement, string> resolveColor)
    {
        var previous = _snapshot;
        var reuseTabs = _renameInput is null && _canEdit == canEdit &&
            _tabsIdentity == _continuity().Identity && previous is not null &&
            SameSheets(previous.Worksheets, snapshot.Worksheets);
        _snapshot = snapshot;
        _canEdit = canEdit;
        _resolveColor = resolveColor;
        _add.IsVisible = canEdit;
        _add.IsEnabled = canEdit;
        _regional.IsVisible = canEdit;
        _regional.IsEnabled = canEdit;
        UpdateRegionalLabel();
        if (_renameInput is not null && _renamingSheetId is { } editingId &&
            snapshot.Worksheets.Any(sheet => sheet.SheetId == editingId) && canEdit)
            return; // Keep a live input and its focus while unrelated model updates arrive.
        _renamingSheetId = null;
        _renameInput = null;
        if (reuseTabs)
        {
            // A selection refresh must retain the pointer target between the two
            // clicks of DoubleTapped, including when the first click selects a tab.
            foreach (var tab in _tabs.Children.OfType<StackPanel>().SelectMany(host => host.Children).OfType<Button>())
                if (tab.Name?.StartsWith("SheetTab", StringComparison.Ordinal) == true &&
                    int.TryParse(tab.Name.AsSpan("SheetTab".Length), out var index))
                    UpdateTabAppearance(tab, snapshot.Worksheets[index], index,
                        previous!.View.Sheet != snapshot.View.Sheet);
            return;
        }
        BuildTabs();
    }

    private static bool SameSheets(WorksheetInfo[] before, WorksheetInfo[] after) =>
        before.Length == after.Length && before.Zip(after).All(pair =>
            pair.First.SheetId == pair.Second.SheetId && pair.First.Name == pair.Second.Name &&
            pair.First.State == pair.Second.State && pair.First.Color.ToString() == pair.Second.Color.ToString());

    public void Clear()
    {
        _snapshot = null;
        _resolveColor = null;
        _renameInput = null;
        _renamingSheetId = null;
        _tabs.Children.Clear();
    }

    private void UpdateRegionalLabel()
    {
        if (_snapshot is not { } snapshot) return;
        _regional.Content = Bounds.Width > 769 ? _text.Compose(t => WorkbookLocale.DisplayName(snapshot.Locale) + " · " + t["regional_settings.language.display_language." + snapshot.Language]).Block() : new WorkbookIcon("Settings");
        _text.Compose(t => $"{t["regional_settings.open_regional_settings"]} · {snapshot.Locale} · {snapshot.Language} · {snapshot.Timezone}").Bind(_regional, ToolTip.TipProperty);
    }

    private void BuildTabs()
    {
        _tabs.Children.Clear();
        if (_snapshot is not { } snapshot) return;
        _tabsIdentity = _continuity().Identity;
        for (var index = 0; index < snapshot.Worksheets.Length; index++)
        {
            var sheet = snapshot.Worksheets[index];
            if (!Visible(sheet)) continue;
            var sheetIndex = index;
            var host = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = sheet.Name, VerticalAlignment = VerticalAlignment.Center };
            var tab = SmallButton(label, sheet.Name, () => _select(sheetIndex));
            tab.Name = $"SheetTab{index}";
            // SheetTab.tsx prevents pointerdown's focus change in reference mode,
            // while still allowing the eventual click to select the sheet.
            tab.AddHandler(PointerPressedEvent, (_, _) =>
            {
                if (PreserveEditorFocus?.Invoke() == true) tab.Focusable = false;
            }, RoutingStrategies.Tunnel);
            tab.AddHandler(PointerReleasedEvent, (_, _) => tab.Focusable = true,
                RoutingStrategies.Bubble, handledEventsToo: true);
            tab.PointerCaptureLost += (_, _) => tab.Focusable = true;
            tab.Padding = new Thickness(8, 5);
            tab.FontSize = 12;
            UpdateTabAppearance(tab, sheet, index, bringIntoView: true);
            if (_canEdit)
            {
                tab.DoubleTapped += (_, _) => BeginRename(sheet.SheetId);
                var menu = SheetMenu(sheetIndex);
                tab.AddHandler(ContextRequestedEvent, (_, e) =>
                {
                    e.Handled = true;
                    menu.Close();
                    OpenSheetMenu(sheet.SheetId);
                }, global::Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
                tab.ContextMenu = menu;
                var dropdown = SmallButton(new WorkbookIcon("ChevronDown", 12), _text.Compose(t => t["sheet_tab.open_menu"] + " · " + sheet.Name), () => OpenSheetMenu(sheet.SheetId));
                dropdown.Name = $"SheetMenu{index}";
                dropdown.Padding = new Thickness(2, 4);
                host.Children.Add(tab);
                host.Children.Add(dropdown);
            }
            else host.Children.Add(tab);
            _tabs.Children.Add(host);
        }
    }

    private void UpdateTabAppearance(Button tab, WorksheetInfo sheet, int index, bool bringIntoView)
    {
        var selected = _snapshot!.View.Sheet == index;
        tab.Background = selected ? SheetPalette.For(ActualThemeVariant).HeaderSelected : Brushes.Transparent;
        tab.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
        if (selected && bringIntoView) Dispatcher.UIThread.Post(tab.BringIntoView);
        if (sheet.Color.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) && _resolveColor is not null)
        {
            tab.BorderBrush = ConditionalRenderer.Brush(_resolveColor(sheet.Color));
            tab.BorderThickness = new Thickness(0, 0, 0, 3);
        }
    }

    private void OpenList()
    {
        if (_snapshot is not { } snapshot) return;
        var identity = _continuity().Identity;
        var items = new List<Control>();
        for (var index = 0; index < snapshot.Worksheets.Length; index++)
        {
            var sheet = snapshot.Worksheets[index];
            var visible = Visible(sheet);
            var label = sheet.Name;
            var item = Item(label, () => SelectFromList(sheet.SheetId, identity));
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsEnabled = visible || _canEdit;
            item.IsChecked = snapshot.View.Sheet == index;
            if (!visible) item.Header = new TextBlock { Text = sheet.Name, TextDecorations = TextDecorations.Strikethrough };
            if (sheet.Color.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) && _resolveColor is not null)
                item.Icon = new Border { Width = 12, Height = 12, Background = ConditionalRenderer.Brush(_resolveColor(sheet.Color)) };
            AutomationProperties.SetName(item, sheet.Name);
            items.Add(item);
        }
        var menu = WorkbookMenus.Create(items, anchored: true);
        menu.Placement = PlacementMode.TopEdgeAlignedLeft;
        menu.VerticalOffset = -4;
        _list.ContextMenu = menu;
        menu.Open(_list);
    }

    private void SelectFromList(int sheetId, Guid identity, bool select = true)
    {
        if (_continuity().Identity != identity || _liveSnapshot() is not { } snapshot) return;
        var index = Array.FindIndex(snapshot.Worksheets, sheet => sheet.SheetId == sheetId);
        if (index < 0) return;
        if (!Visible(snapshot.Worksheets[index]) &&
            (!_canEdit || !_command("unhide_sheet", new { sheet = index }))) return;
        if (select) _select(index);
    }

    private void OpenSheetMenu(int sheetId)
    {
        if (!_canEdit || _liveSnapshot() is not { } snapshot) return;
        var index = Array.FindIndex(snapshot.Worksheets, sheet => sheet.SheetId == sheetId);
        if (index < 0 || !Visible(snapshot.Worksheets[index])) return;
        // Upstream selects the tab before opening its menu. Selection refreshes the
        // controls, so anchor the popup to the new tab, not the detached old button.
        _select(index);
        var tab = _tabs.Children.OfType<StackPanel>().SelectMany(host => host.Children)
            .OfType<Button>().SingleOrDefault(button => button.Name == $"SheetTab{index}");
        if (tab?.ContextMenu is { } menu && tab.Parent is Control host)
        {
            // SheetTab.tsx anchors both the chevron and right-click menu to
            // the tab's right edge minus 24px, above the bottom sheet bar.
            menu.Placement = PlacementMode.TopEdgeAlignedLeft;
            menu.HorizontalOffset = host.Bounds.Width - 24;
            menu.VerticalOffset = -4;
            menu.Open(tab);
        }
    }

    private ContextMenu SheetMenu(int sheet)
    {
        var snapshot = _snapshot!;
        var visibleCount = snapshot.Worksheets.Count(Visible);
        var identity = _tabsIdentity;
        var sheetId = snapshot.Worksheets[sheet].SheetId;
        var items = new List<Control>
        {
            Item(_text.Key("sheet_tab.move_left"), () => { if (sheet > 0) Mutate("move_sheet", new { sheet, index = sheet - 1 }); }),
            Item(_text.Key("sheet_tab.move_right"), () => { if (sheet < snapshot.Worksheets.Length - 1) Mutate("move_sheet", new { sheet, index = sheet + 1 }); }),
            Item(_text.Key("sheet_tab.rename"), () => BeginRename(snapshot.Worksheets[sheet].SheetId)),
            Item(_text.Key("sheet_tab.duplicate"), () => Mutate("duplicate_sheet", new { sheet })),
            Item(_text.Key("sheet_tab.hide_sheet"), () => Mutate("hide_sheet", new { sheet }), visibleCount > 1),
            new Separator(),
            Item(_text.Key("sheet_tab.delete"), () => ConfirmDelete(sheet), visibleCount > 1)
        };
        var colorItem = Item(_text.Key("sheet_tab.change_color"), () =>
        {
            // ContextMenu closes after its Click handler. Open the palette on the
            // sheet tab on the next UI pass so the dismissal does not close it.
            Dispatcher.UIThread.Post(() =>
            {
                if (_continuity().Identity != identity || _liveSnapshot() is not { } current) return;
                var index = Array.FindIndex(current.Worksheets, item => item.SheetId == sheetId);
                if (index < 0) return;
                var tab = _tabs.Children.OfType<StackPanel>().SelectMany(host => host.Children)
                    .OfType<Button>().SingleOrDefault(button => button.Name == $"SheetTab{index}");
                if (tab is not null && _canEdit) ColorPickerRequested?.Invoke(tab, index);
            });
        }, ColorPickerRequested is not null);
        items.Insert(3, colorItem);
        var icons = new[] { "ArrowLeft", "ArrowRight", "TextCursorInput", "PaintBucket", "Copy", "EyeOff", "Trash2" };
        foreach (var (item, icon) in items.OfType<MenuItem>().Zip(icons)) item.Icon = new WorkbookIcon(icon);
        items.OfType<MenuItem>().Last().Classes.Add("destructive");
        return WorkbookMenus.Create(items);
    }

    private void ConfirmDelete(int sheet)
    {
        if (!_canEdit || _snapshot is null) return;
        var name = _snapshot.Worksheets[sheet].Name;
        var identity = _continuity().Identity;
        var window = new Window();
        NativeDialogTheme.Follow(window, this);
        _text.Key("sheet_delete.title").Bind(window, Window.TitleProperty);
        var panel = WorkbookDialog.Body();
        panel.Children.Add(WorkbookDialog.Paragraph(_text.Key("sheet_delete.message", ("sheetName", name))));
        void Confirm()
        {
            window.Close();
            var current = _liveSnapshot();
            if (!_canEdit || current is null || _continuity().Identity != identity) return;
            // Workbook.tsx onSheetDeleted reads the selected sheet at confirm time.
            Mutate("delete_sheet", new { sheet = current.View.Sheet });
        }
        var confirm = WorkbookDialog.Button(_text.Key("sheet_delete.confirm"), Confirm, "destructive");
        var buttons = WorkbookDialog.Footer(WorkbookDialog.Button(_text.Key("sheet_delete.cancel"), window.Close), confirm);
        WorkbookDialog.Configure(window, _text.Key("sheet_delete.title"), _text.Key("link_dialog.close"), panel, buttons);
        window.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { window.Close(); e.Handled = true; }
            else if (e.Key == Key.Enter && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) { Confirm(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => { if (window.IsVisible) confirm.Focus(); }, DispatcherPriority.Input);
        if (TopLevel.GetTopLevel(this) is Window owner) window.ShowDialog(owner); else window.Show();
    }

    private void BeginRename(int sheetId)
    {
        if (!_canEdit || _snapshot is not { } snapshot) return;
        var index = Array.FindIndex(snapshot.Worksheets, sheet => sheet.SheetId == sheetId);
        if (index < 0 || !Visible(snapshot.Worksheets[index])) return;
        _renamingSheetId = sheetId;
        var original = snapshot.Worksheets[index].Name;
        var hostIndex = snapshot.Worksheets.Take(index).Count(Visible);
        if (_tabs.Children[hostIndex] is not StackPanel host) return;
        host.Children.Clear();
        var input = new TextBox
        {
            Name = "SheetRenameInput", Text = original, MinWidth = 6, FontSize = 12,
            FontWeight = snapshot.View.Sheet == index ? FontWeight.SemiBold : FontWeight.Normal,
            MinHeight = 27, Padding = new Thickness(5, 3), VerticalAlignment = VerticalAlignment.Center
        };
        _text.Key("sheet_tab.rename").Describe(input);
        input.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) SizeRenameInput(input); };
        input.AttachedToVisualTree += (_, _) => SizeRenameInput(input);
        input.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter) { FinishRename(true); args.Handled = true; }
            else if (args.Key == Key.Escape) { FinishRename(false); args.Handled = true; }
        };
        input.LostFocus += (_, _) => Dispatcher.UIThread.Post(() => { if (ReferenceEquals(input, _renameInput)) FinishRename(true); });
        _renameInput = input;
        host.Children.Add(input);
        Dispatcher.UIThread.Post(() => { if (ReferenceEquals(input, _renameInput)) { input.Focus(); input.SelectAll(); input.BringIntoView(); } });
    }

    private static void SizeRenameInput(TextBox input)
    {
        // Measure the live draft like SheetTab.tsx, including trailing spaces and native input chrome.
        using var text = new TextLayout(string.IsNullOrEmpty(input.Text) ? " " : input.Text,
            new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch), input.FontSize, Brushes.Black);
        input.Width = Math.Ceiling(text.WidthIncludingTrailingWhitespace) + input.Padding.Left + input.Padding.Right
            + input.BorderThickness.Left + input.BorderThickness.Right + 2;
    }

    private void FinishRename(bool apply)
    {
        if (_finishingRename || _renameInput is not { } input || _renamingSheetId is not { } sheetId || _snapshot is not { } snapshot) return;
        _finishingRename = true;
        try
        {
            var index = Array.FindIndex(snapshot.Worksheets, sheet => sheet.SheetId == sheetId);
            var name = (input.Text ?? "").Trim();
            // SheetTab.tsx ends editing after onRenamed, including an engine error.
            if (index >= 0 && apply && name.Length > 0 && name != snapshot.Worksheets[index].Name)
                _command("rename_sheet", new { sheet = index, name });
            _renameInput = null;
            _renamingSheetId = null;
            BuildTabs();
            if (index >= 0) _tabs.Children.OfType<StackPanel>().SelectMany(host => host.Children).OfType<Button>().FirstOrDefault(tab => tab.Name == $"SheetTab{index}")?.Focus();
        }
        finally { _finishingRename = false; }
    }

    private void Mutate(string operation, object? args = null)
    {
        if (_canEdit) _command(operation, args);
    }

    private static bool Visible(WorksheetInfo sheet) => sheet.State.Equals("visible", StringComparison.OrdinalIgnoreCase);

    private static Button SmallButton(object content, LocalizedText name, Action action)
    {
        var button = name.Describe(SmallButton(content is LocalizedText ? "" : content, "", action));
        if (content is LocalizedText label) label.Bind(button, ContentControl.ContentProperty);
        return button;
    }
    private static MenuItem Item(LocalizedText label, Action action, bool enabled = true)
    {
        var item = WorkbookView.MenuItem(label, action); item.IsEnabled = enabled; return item;
    }

    private static Button SmallButton(object content, string name, Action action)
    {
        var button = new WorkbookButton { Content = content, MinHeight = 28, Padding = new Thickness(7, 4), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    private static MenuItem Item(string label, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = label, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }
}
