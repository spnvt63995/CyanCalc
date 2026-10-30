using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CyanCalc.Controls.Preview;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    private Flyout? _toolbarFlyout;
    private Flyout? _nestedToolbarFlyout;

    private static ToggleButton ToolbarToggle(string icon, string name, Action action)
    {
        var button = new ToggleButton { Classes = { "workbook-toggle" }, Content = new WorkbookIcon(icon), Padding = new Thickness(6), Width = 28,
            Height = 28, MinHeight = 0, BorderThickness = new Thickness(0) };
        button.Click += (_, _) => action();
        ToolTip.SetTip(button, name); AutomationProperties.SetName(button, name);
        return button;
    }

    private static void SetMenuExample(MenuItem item, string label, string? example)
    {
        AutomationProperties.SetName(item, label);
        if (string.IsNullOrEmpty(example)) { item.Header = label; return; }
        var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 20 };
        header.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var sample = new TextBlock { Text = example, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        header.SizeChanged += (_, _) => sample.MaxWidth = header.Bounds.Width / 2;
        WorkbookMenus.Secondary(sample);
        Grid.SetColumn(sample, 1); header.Children.Add(sample); item.Header = header;
    }

    private LocalizedText NumberFormatLabel(string id) => Text.Key("toolbar.format_menu." + (id switch
    {
        "Auto" => "auto",
        "Number" => "number",
        "Percentage" => "percentage",
        "Euro (EUR)" => "currency_eur",
        "Dollar (USD)" => "currency_usd",
        "British Pound (GBP)" => "currency_gbp",
        "Short date" => "date_short",
        "Long date" => "date_long",
        "Custom" => "custom",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    }));

    private void SetNumberFormatExample(MenuItem item, string label, string? example)
    {
        var translated = NumberFormatLabel(label);
        translated.Bind(item, AutomationProperties.NameProperty);
        if (string.IsNullOrEmpty(example)) { translated.Bind(item, HeaderedSelectingItemsControl.HeaderProperty); return; }
        var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        header.Children.Add(translated.Bind(new TextBlock { VerticalAlignment = VerticalAlignment.Center }, TextBlock.TextProperty));
        var sample = new TextBlock { Text = example, VerticalAlignment = VerticalAlignment.Center };
        WorkbookMenus.Secondary(sample);
        if (label == "Percentage") Text.Key("toolbar.format_menu.percentage_example").Bind(sample, TextBlock.TextProperty);
        Grid.SetColumn(sample, 1); header.Children.Add(sample); item.Header = header;
    }

    private static MenuItem IconMenuItem(string label, string icon, Action action, bool enabled = true)
    {
        var item = MenuItem(label, action);
        item.Icon = new WorkbookIcon(icon); item.IsEnabled = enabled;
        AutomationProperties.SetName(item, label);
        return item;
    }

    private Button ColorButton(string icon, LocalizedText title, string path, out Border colorLine)
    {
        Button? button = null;
        button = IconButton(icon, () => ShowColorPicker(button!, title, path), title);
        button.Padding = new Thickness(0);
        var content = new Grid { Width = 28, Height = 28, Children = { new WorkbookIcon(icon) } };
        colorLine = new Border { Width = 16, Height = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        content.Children.Add(colorLine); button.Content = content;
        return button;
    }

    private void ShowToolbarFlyout(Control anchor, Control content, PlacementMode placement = PlacementMode.BottomEdgeAlignedLeft)
    {
        CloseToolbarPopups();
        var flyout = new WorkbookPickerFlyout { Content = content, Placement = placement,
            VerticalOffset = placement == PlacementMode.TopEdgeAlignedLeft ? -4 : 4 };
        flyout.ConstrainColorPicker(TopLevel.GetTopLevel(this));
        _toolbarFlyout = flyout;
        flyout.Closed += (_, _) =>
        {
            _nestedToolbarFlyout?.Hide();
            if (ReferenceEquals(_toolbarFlyout, flyout)) _toolbarFlyout = null;
        };
        flyout.ShowAt(anchor);
    }

    private void ShowNestedFlyout(Control anchor, Control content)
    {
        _nestedToolbarFlyout?.Hide();
        var flyout = new WorkbookPickerFlyout { Content = content, Placement = PlacementMode.RightEdgeAlignedTop };
        flyout.ConstrainColorPicker(TopLevel.GetTopLevel(this));
        _nestedToolbarFlyout = flyout;
        flyout.Closed += (_, _) => { if (ReferenceEquals(_nestedToolbarFlyout, flyout)) _nestedToolbarFlyout = null; };
        flyout.ShowAt(anchor);
    }

    private void CloseToolbarPopups()
    {
        _nestedToolbarFlyout?.Hide(); _toolbarFlyout?.Hide();
    }

    private void OpenDrawerFeature(string title, Action show)
    {
        // Re-entering an already open drawer also finishes the current cell edit.
        CommitEdit();
        // Workbook.tsx openDrawer keeps the mounted panel when its type is unchanged.
        if (!_drawer.IsVisible || _drawerFeature != title) show();
        _nameMenuPreservesEdit = false;
        RefreshDrawerButtons();
    }

    private void RefreshDrawerButtons()
    {
        if (_conditionalButton is not null) _conditionalButton.IsChecked = _drawer.IsVisible && _drawerFeature == "conditional_formatting.title";
        if (_stylesButton is not null) _stylesButton.IsChecked = _drawer.IsVisible && _drawerFeature == "named_styles.panel_title";
    }

    private Control[] ThemeMenu()
    {
        if (Session is not { } model) return [];
        var current = model.GetTheme();
        var themes = model.Query("themes").EnumerateArray().Select(theme => theme.Clone()).ToList();
        if (!themes.Any(theme => StylePreviewResolver.SameTheme(theme, current))) themes.Add(current.Clone());
        var items = new List<Control>();
        foreach (var theme in themes)
        {
            var item = MenuItem(theme.GetProperty("name").GetString()!, () => Command("set_theme", new { theme }));
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = StylePreviewResolver.SameTheme(theme, current);
            var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            header.Children.Add(new TextBlock { Text = theme.GetProperty("name").GetString() });
            var colors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
            for (var i = 1; i <= 6; i++)
            {
                var swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
                    Margin = new Thickness(i == 1 ? 0 : -4, 0, 0, 0), Background = ConditionalRenderer.Brush(theme.GetProperty("accent" + i).GetString()) };
                swatch.Bind(Border.BorderBrushProperty, swatch.GetResourceObservable("Menu-surface-elevated")); colors.Children.Add(swatch);
            }
            Grid.SetColumn(colors, 1); header.Children.Add(colors); item.Header = header;
            AutomationProperties.SetName(item, theme.GetProperty("name").GetString());
            items.Add(item);
        }
        items.Add(new Separator());
        items.Add(IconMenuItem(Text.Key("themes.manage_themes"), "Settings", ShowThemes));
        return items.ToArray();
    }

    private (bool Merge, bool Across, bool Down, bool Unmerge) MergeAvailability()
    {
        if (Snapshot is not { } snapshot || !CanEdit) return (false, false, false, false);
        var area = CellArea.FromView(snapshot.View);
        if (area.Width >= CellAddress.LastColumn || area.Height >= CellAddress.LastRow) return (false, false, false, false);
        var intersects = snapshot.MergedCells.Any(merge => merge.Row < area.Row + area.Height && merge.LastRow >= area.Row
            && merge.Column < area.Column + area.Width && merge.LastColumn >= area.Column);
        var canMerge = !intersects && (area.Width > 1 || area.Height > 1);
        return (canMerge, canMerge && area.Width > 1, canMerge && area.Height > 1, intersects);
    }

    private Control[] MergeMenu()
    {
        var state = MergeAvailability();
        return [
            IconMenuItem(Text.Key("toolbar.merge_menu.merge"), "TableCellsMerge", () => MergeSelection("merge_cells"), state.Merge),
            IconMenuItem(Text.Key("toolbar.merge_menu.merge_center"), "AlignCenterHorizontal", () => MergeSelection("merge_cells_center"), state.Merge),
            IconMenuItem(Text.Key("toolbar.merge_menu.merge_across"), "Rows3", () => MergeSelection("merge_cells_across"), state.Across),
            IconMenuItem(Text.Key("toolbar.merge_menu.merge_down"), "Columns3", () => MergeSelection("merge_cells_down"), state.Down),
            new Separator(),
            IconMenuItem(Text.Key("toolbar.merge_menu.unmerge"), "TableCellsSplit", () => MergeSelection("unmerge_cells"), state.Unmerge)
        ];
    }
    private void MergeSelection(string operation)
    {
        if (!CanEdit || Session is null || Snapshot is null || !CommitEdit()) return;
        try { Session.Execute(operation, new { area = CellArea.FromView(Snapshot.View) }); }
        catch (IronCalcException error)
        {
            if (error.Message.Contains("more than one cell has content", StringComparison.Ordinal))
                ShowWorkbookAlert(Text.Key("error_dialog.error_merging_cells"), Text.Key("error_dialog.error_merging_cells_content"));
            // Workbook.tsx deliberately leaves other merge failures silent.
        }
        Refresh();
    }
}
