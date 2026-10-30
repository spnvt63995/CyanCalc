using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyanCalc.Controls;

public sealed partial class WorkbookShell
{
    private void BuildHeader()
    {
        var header = new Grid { ColumnDefinitions = new("*,Auto,Auto"), Margin = new Thickness(12, 0), ColumnSpacing = 2 };
        if (_searching)
        {
            var search = new TextBox { Text = _query, BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            Text.Key("left_drawer.search_workbook").Describe(search);
            Text.Key("left_drawer.search_placeholder").Bind(search, TextBox.WatermarkProperty);
            search.TextChanged += (_, _) => { _query = search.Text ?? ""; BuildList(); };
            void Clear() { _query = ""; _searching = false; BuildHeader(); BuildList(); }
            search.KeyDown += (_, e) => { if (e.Key == Key.Escape) { Clear(); e.Handled = true; } };
            header.Children.Add(search);
            var close = IconButton("X", Text.Key("left_drawer.clear_search"), Clear);
            Grid.SetColumn(close, 2); header.Children.Add(close);
            _drawerHeader.Child = header;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => search.Focus());
            return;
        }
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var brandIcon = new WorkbookIcon("Table2", 18);
        brandIcon.Bind(ForegroundProperty, brandIcon.GetResourceObservable("Dialog-primary-contrast-text"));
        var brandBadge = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6), Child = brandIcon };
        brandBadge.Bind(Border.BackgroundProperty, brandBadge.GetResourceObservable("Dialog-primary-main"));
        brand.Children.Add(brandBadge);
        brand.Children.Add(new TextBlock { Text = "CyanCalc", FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(brand);
        var searchButton = IconButton("Search", Text.Key("left_drawer.search_workbook"), () => { _searching = true; BuildHeader(); });
        Grid.SetColumn(searchButton, 1); header.Children.Add(searchButton);
        var add = IconButton("Plus", Text.Key("left_drawer.new_workbook"), () => Send("new"));
        Grid.SetColumn(add, 2); header.Children.Add(add);
        _drawerHeader.Child = header;
    }
    private void BuildList()
    {
        _list.Children.Clear(); _rows.Clear();
        var query = _query.Trim();
        var now = DateTimeOffset.UtcNow;
        IEnumerable<WorkbookListEntry> newest = _entries.OrderByDescending(e => e.CreatedAt);
        var groups = new List<(LocalizedText? Title, WorkbookListEntry[] Entries)>();
        if (query.Length > 0)
            groups.Add((null, newest.Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray()));
        else
        {
            groups.Add((Text.Key("left_drawer.pinned"), newest.Where(e => e.Pinned).ToArray()));
            groups.Add((Text.Key("left_drawer.today"), newest.Where(e => !e.Pinned && (now - e.CreatedAt).TotalDays < 1).ToArray()));
            groups.Add((Text.Key("left_drawer.last_30_days"), newest.Where(e => !e.Pinned && (now - e.CreatedAt).TotalDays is >= 1 and < 30).ToArray()));
            groups.Add((Text.Key("left_drawer.older"), newest.Where(e => !e.Pinned && (now - e.CreatedAt).TotalDays >= 30).ToArray()));
        }
        var ordered = groups.SelectMany(g => g.Entries).Select(e => e.Id).ToArray();
        if (ordered.Length == 0)
            _list.Children.Add(Text.Key(_entries.Length == 0 ? "left_drawer.empty_state" : "left_drawer.search_no_results").Bind(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) }, TextBlock.TextProperty));
        foreach (var (title, entries) in groups)
        {
            if (entries.Length == 0) continue;
            if (title is not null) _list.Children.Add(title.Bind(new TextBlock { FontSize = 13, Opacity = .65, Margin = new Thickness(8, 8, 0, 6) }, TextBlock.TextProperty));
            foreach (var entry in entries) AddWorkbookRow(entry, ordered);
        }
        RefreshRowColors(); BuildFooter();
    }
    private void AddWorkbookRow(WorkbookListEntry entry, Guid[] ordered)
    {
        var grid = new Grid { ColumnDefinitions = new("28,*,28"), Height = 32 };
        var row = new Border { Child = grid, CornerRadius = new CornerRadius(8) };
        var iconHost = new Grid { Margin = new Thickness(6, 0, 0, 0) };
        var table = new WorkbookIcon("Table2");
        var check = new CheckBox { IsChecked = _checked.Contains(entry.Id), MinWidth = 0, MinHeight = 0, Width = 20, Height = 24, Padding = new Thickness(0) };
        Text.Key("left_drawer.select_workbook", ("name", entry.Name)).Describe(check);
        var shift = false;
        check.AddHandler(PointerPressedEvent, (_, e) => shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift), RoutingStrategies.Tunnel);
        check.AddHandler(KeyDownEvent, (_, e) => shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift), RoutingStrategies.Tunnel);
        check.Click += (_, _) =>
        {
            var start = _lastChecked is { } last ? Array.IndexOf(ordered, last) : -1;
            var end = Array.IndexOf(ordered, entry.Id);
            if (shift && start >= 0 && end >= 0)
                foreach (var id in ordered.Skip(Math.Min(start, end)).Take(Math.Abs(end - start) + 1)) _checked.Add(id);
            else
            {
                if (!_checked.Add(entry.Id)) _checked.Remove(entry.Id);
                _lastChecked = _checked.Count > 0 ? entry.Id : null;
            }
            shift = false; BuildList();
        };
        void CheckboxVisibility(bool hovered)
        {
            var show = hovered || _checked.Count > 0 || check.IsFocused;
            check.Opacity = show ? 1 : 0; table.IsVisible = !show;
        }
        iconHost.PointerEntered += (_, _) => CheckboxVisibility(true);
        iconHost.PointerExited += (_, _) => CheckboxVisibility(false);
        check.GotFocus += (_, _) => CheckboxVisibility(true);
        check.LostFocus += (_, _) => CheckboxVisibility(false);
        iconHost.Children.Add(table); iconHost.Children.Add(check); CheckboxVisibility(false);
        grid.Children.Add(iconHost);
        var select = new WorkbookButton { Content = new TextBlock { Text = entry.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8, 0), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 0 };
        AutomationProperties.SetName(select, entry.Name);
        select.Click += (_, _) => Send("select", entry.Id); Grid.SetColumn(select, 1); grid.Children.Add(select);
        Button? more = null;
        more = IconButton("EllipsisVertical", Text.Compose(t => t["file_bar.file_menu.button"] + " · " + entry.Name), () =>
        {
            Send("select", entry.Id);
            ShowMenu(more!, [
                Item(Text.Key("left_drawer.workbook_menu.download"), "FileDown", () => Send("export", entry.Id)),
                Item(Text.Key(entry.Pinned ? "left_drawer.workbook_menu.unpin" : "left_drawer.workbook_menu.pin"), entry.Pinned ? "PinOff" : "Pin", () => Send("pin", entry.Id)),
                Item(Text.Key("left_drawer.workbook_menu.duplicate"), "Copy", () => Send("duplicate", entry.Id)),
                new Separator(),
                Item(Text.Key("left_drawer.workbook_menu.delete"), "Trash2", () => Send("delete", entry.Id), destructive: true)
            ]);
        });
        more.Width = 26; Grid.SetColumn(more, 2); grid.Children.Add(more);
        row.PointerEntered += (_, _) => row.Background = SeparatorBrush;
        row.PointerExited += (_, _) => RefreshRowColors();
        _rows.Add(entry.Id, row); _list.Children.Add(row);
    }
    private void BuildFooter()
    {
        _footer.Children.Clear();
        if (_checked.Count == 0)
        {
            var docs = TextButton(Text.Key("left_drawer.documentation"), () => Send("link", value: "https://docs.ironcalc.com/"));
            docs.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new WorkbookIcon("BookOpen"), Text.Key("left_drawer.documentation").Block() } };
            docs.HorizontalAlignment = HorizontalAlignment.Stretch; docs.HorizontalContentAlignment = HorizontalAlignment.Left;
            docs.Background = Brushes.Transparent; docs.BorderThickness = new Thickness(0); _footer.Children.Add(docs); return;
        }
        var key = _checked.Count == 1 ? "left_drawer.delete_workbooks_one" : "left_drawer.delete_workbooks_other";
        var delete = TextButton(Text.Key(key, ("count", _checked.Count)), () => Send("deleteMany", ids: _checked.ToArray()));
        delete.Bind(BackgroundProperty, delete.GetResourceObservable("Dialog-error-main"));
        delete.Bind(ForegroundProperty, delete.GetResourceObservable("Dialog-error-contrast-text"));
        delete.HorizontalAlignment = HorizontalAlignment.Stretch;
        _footer.Children.Add(delete);
        var cancel = TextButton(Text.Key("left_drawer.cancel"), ClearChecked); cancel.HorizontalAlignment = HorizontalAlignment.Stretch; _footer.Children.Add(cancel);
    }
    private void BuildNotice()
    {
        _notice.IsVisible = !_noticeDismissed;
        var panel = new DockPanel();
        var close = IconButton("X", Text.Key("left_drawer.alert.close"), () => Send("dismissNotice"));
        close.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(close, Dock.Right); panel.Children.Add(close);
        panel.Children.Add(Text.Key("native.storage_notice").Bind(new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap }, TextBlock.TextProperty));
        _notice.Child = panel;
    }
}
