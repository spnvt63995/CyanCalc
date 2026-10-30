using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyanCalc.Controls;

public sealed partial class WorkbookShell
{
    private void BuildFileBar()
    {
        if (_title.Parent is Panel previous) previous.Children.Remove(_title);
        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(8, 0) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(IconButton(_drawerOpen ? "PanelLeftClose" : "PanelLeftOpen", Text.Key(_drawerOpen ? "file_bar.close_sidebar" : "file_bar.open_sidebar"), ToggleDrawer));
        if (Bounds.Width is > 0 and < 768)
        {
            Button? combined = null;
            combined = IconButton("Menu", Text.Compose(t => t["file_bar.file_menu.button"] + " · " + t["file_bar.help_menu.button"]), () => ShowMenu(combined!, FileItems().Concat([new Separator()]).Concat(HelpItems())));
            left.Children.Add(combined);
        }
        else
        {
            _fileButton = TextButton(Text.Key("file_bar.file_menu.button"), () => ShowMenu(_fileButton!, FileItems()));
            _helpButton = TextButton(Text.Key("file_bar.help_menu.button"), () => ShowMenu(_helpButton!, HelpItems()));
            foreach (var button in new[] { _fileButton, _helpButton }) { button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); }
            _fileButton.PointerEntered += (_, _) => { if (_menu?.IsOpen == true) ShowMenu(_fileButton!, FileItems()); };
            _helpButton.PointerEntered += (_, _) => { if (_menu?.IsOpen == true) ShowMenu(_helpButton!, HelpItems()); };
            left.Children.Add(_fileButton); left.Children.Add(_helpButton);
        }
        Grid.SetColumnSpan(_title, 3); grid.Children.Add(_title); left.ZIndex = 1; grid.Children.Add(left);
        var storage = IconButton("CloudOff", Text.Key("native.saved_local"), () =>
        {
            var flyout = new Flyout { Content = Text.Key("native.storage_help").Bind(new TextBlock { Width = 280, TextWrapping = TextWrapping.Wrap }, TextBlock.TextProperty) };
            flyout.ShowAt(_fileBar);
        });
        Grid.SetColumn(storage, 2); storage.ZIndex = 1; grid.Children.Add(storage); _fileBar.Child = grid;
        grid.SizeChanged += (_, _) => _title.MaxWidth = Math.Max(32, grid.Bounds.Width - 2 * Math.Max(left.Bounds.Width, storage.Bounds.Width));
    }
    private IEnumerable<Control> FileItems()
    {
        yield return Item(Text.Key("file_bar.file_menu.new_blank_workbook"), "Plus", () => Send("new"));
        yield return Item(Text.Key("file_bar.file_menu.new_from_template"), "Table2", () => Send("templates"));
        yield return Item(Text.Key("file_bar.file_menu.import.button"), "FileUp", () => Send("import"));
        yield return new Separator();
        yield return Item(Text.Key("file_bar.file_menu.download"), "FileDown", () => Send("export"));
        yield return Item(Text.Key("file_bar.file_menu.delete_workbook.button"), "Trash2", () => Send("delete", _selected), destructive: true);
        yield return new Separator();
        var languages = Text.Key("file_bar.file_menu.default_language").Bind(new MenuItem { Icon = new WorkbookIcon("Globe") }, MenuItem.HeaderProperty);
        foreach (var (code, label) in new[] { ("en-US", "English"), ("en-GB", "English"), ("es-ES", "Español"), ("fr-FR", "Français"), ("de-DE", "Deutsch"), ("it-IT", "Italiano") })
        {
            var item = Item(label + "   " + code, null, () => Send("language", value: code));
            item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = Text.Language == code; languages.Items.Add(item);
        }
        yield return languages;
        var appearance = Text.Key("file_bar.file_menu.appearance").Bind(new MenuItem { Icon = new WorkbookIcon("Sun") }, MenuItem.HeaderProperty);
        foreach (var mode in new[] { "light", "dark", "system" })
        {
            var item = Item(Text.Key("file_bar.file_menu.appearance_" + mode), null, () => Send("appearance", value: mode));
            item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = _appearance == mode; appearance.Items.Add(item);
        }
        yield return appearance;

    }
    private IEnumerable<Control> HelpItems()
    {
        yield return Item(Text.Key("file_bar.help_menu.documentation"), "BookOpen", () => Send("link", value: "https://docs.ironcalc.com/web-application/about.html"));
        yield return Item(Text.Key("file_bar.help_menu.keyboard_shortcuts"), "Keyboard", () => Send("link", value: "https://docs.ironcalc.com/features/keyboard-shortcuts.html"));
        yield return new Separator();
        yield return Item(Text.Key("file_bar.help_menu.about"), "Info", () => Send("link", value: "https://www.ironcalc.com"));
    }
    private void ShowMenu(Control anchor, IEnumerable<Control> items)
    {
        _menu?.Hide();
        _menu = new WorkbookMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft, VerticalOffset = 4 };
        foreach (var item in items) _menu.Items.Add(item);
        _menu.ShowAt(anchor);
    }
    private static MenuItem Item(LocalizedText label, string? icon, Action action, bool destructive = false)
    {
        var item = Item("", icon, action, destructive);
        label.Bind(item, MenuItem.HeaderProperty); label.Bind(item, AutomationProperties.NameProperty); return item;
    }
    private static MenuItem Item(string label, string? icon, Action action, bool destructive = false)
    {
        var item = new MenuItem { Header = label };
        if (icon is not null) item.Icon = new WorkbookIcon(icon);
        if (destructive) item.Classes.Add("destructive");
        AutomationProperties.SetName(item, label);
        item.Click += (_, _) => action(); return item;
    }
}
