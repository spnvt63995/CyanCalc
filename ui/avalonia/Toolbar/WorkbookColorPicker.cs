using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    private readonly Dictionary<string, List<PickerColor>> _recentColors = new();
    private readonly Dictionary<string, Dictionary<string, string>> _recentDisplayHex = new();

    private static PickerColor? ReadPickerColor(JsonElement raw) => raw.ValueKind switch
    {
        JsonValueKind.Array when raw.GetArrayLength() == 2 => PickerColor.Theme(raw[0].GetInt32(), raw[1].GetDouble()),
        JsonValueKind.String => PickerColor.Rgb(raw.GetString()!),
        _ => null
    };

    private void ShowColorPicker(Control anchor, LocalizedText title, string path)
    {
        if (!CanEdit || Session is not { } model || !CommitEdit() || Snapshot is not { } snapshot) return;
        var rawStyle = model.GetCellStyle(snapshot.View.Sheet, snapshot.View.Row, snapshot.View.Column).GetProperty("style");
        PickerColor? selected = rawStyle.TryGetProperty(path.Split('.')[0], out var part) && part.TryGetProperty("color", out var raw)
            ? ReadPickerColor(raw) : null;
        var panel = BuildColorPicker(model, path, selected, color =>
        {
            if (CanEdit && ReferenceEquals(Session, model)) Style(path, color.StyleValue);
        }, () => { CloseToolbarPopups(); FocusCell(); });
        title.Describe(panel);
        ShowToolbarFlyout(anchor, panel);
    }

    private void ShowSheetColorPicker(Control anchor, int sheet)
    {
        if (!CanEdit || Session is not { } model || Snapshot is not { } snapshot) return;
        var sheetId = snapshot.Worksheets[sheet].SheetId;
        var panel = BuildColorPicker(model, "sheet", ReadPickerColor(snapshot.Worksheets[sheet].Color), color =>
        {
            if (!CanEdit || !ReferenceEquals(Session, model)) return;
            var current = Array.FindIndex(model.Snapshot().Worksheets, item => item.SheetId == sheetId);
            if (current >= 0) Command("sheet_color", new { sheet = current, color = ColorValue(color) });
        }, () => { CloseToolbarPopups(); FocusCell(); }, defaultValue: "#FFFFFF", defaultLabel: Text.Key("color_picker.no_fill"));
        ShowToolbarFlyout(anchor, panel, PlacementMode.TopEdgeAlignedLeft);
    }

    private static string ThemeColorLabel(WorkbookText text, int slot) => slot < 4
        ? text[slot % 2 == 0 ? "themes.light_color_label" : "themes.dark_color_label"] + " " + (slot / 2 + 1)
        : text.Format("themes.accent_color_label", "n", slot - 3);

    private static object? ColorValue(PickerColor color) => color.Slot is int slot
        ? new[] { (double)slot, color.Tint } : color.Hex;

    private StackPanel BuildColorPicker(UserModel model, string path, PickerColor? selected, Action<PickerColor> choose, Action close, string defaultValue = "", LocalizedText? defaultLabel = null)
    {
        var panel = new StackPanel { Width = 220 };
        panel.Classes.Add("color-picker");
        var palette = new StackPanel();
        var advanced = new StackPanel { IsVisible = false };
        panel.Children.Add(palette);
        panel.Children.Add(advanced);
        if (!_recentColors.TryGetValue(path, out var recent))
            _recentColors[path] = recent = new List<PickerColor>();
        if (!_recentDisplayHex.TryGetValue(path, out var recentHex))
            _recentDisplayHex[path] = recentHex = new Dictionary<string, string>();

        void Choose(PickerColor color, string displayHex)
        {
            choose(color);
            if (!recent.Any(item => WorkbookColorPalette.Same(item, color)))
                recentHex[color.StyleValue] = displayHex;
            WorkbookColorPalette.Remember(recent, color);
            foreach (var key in recentHex.Keys.ToArray())
                if (!recent.Any(item => string.Equals(item.StyleValue, key, StringComparison.OrdinalIgnoreCase)))
                    recentHex.Remove(key);
            close();
        }

        var defaultContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var defaultChip = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        defaultChip.Bind(Border.BorderBrushProperty, defaultChip.GetResourceObservable("Menu-grey-300"));
        if (defaultValue.Length > 0) defaultChip.Background = ConditionalRenderer.Brush(defaultValue);
        else if (path != "fill.color") defaultChip.Bind(Border.BackgroundProperty, defaultChip.GetResourceObservable("Menu-sheet-default-text-color"));
        defaultContent.Children.Add(defaultChip);
        defaultContent.Children.Add((defaultLabel ?? Text.Key("color_picker.default")).Bind(new TextBlock { VerticalAlignment = VerticalAlignment.Center }, TextBlock.TextProperty));
        var defaultButton = WorkbookMenus.RowButton(defaultContent);
        defaultButton.Bind(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, defaultButton.GetResourceObservable("Menu-sheet-default-text-color"));
        (defaultLabel ?? Text.Key("color_picker.default")).Describe(defaultButton);
        defaultButton.Margin = new Thickness(4);
        defaultButton.Click += (_, _) => Choose(PickerColor.Rgb(defaultValue), defaultValue);
        palette.Children.Add(defaultButton);
        palette.Children.Add(WorkbookMenus.Divider());
        StackPanel Section(LocalizedText title)
        {
            var section = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
            var heading = title.Block(); heading.Bind(TextBlock.ForegroundProperty, heading.GetResourceObservable("Menu-grey-600"));
            section.Children.Add(heading); palette.Children.Add(section); return section;
        }
        var navigation = new List<(Button Button, PickerPosition Position)>
        {
            (defaultButton, new PickerPosition(0, 0))
        };
        string DisplayHex(PickerColor color) => color.Slot is int slot
            ? model.ResolveColor(System.Text.Json.JsonSerializer.SerializeToElement(new[] { (double)slot, color.Tint }))
            : WorkbookColorPalette.ValidHex(color.Hex) ? color.Hex! : "#FFFFFF";
        Button Chip(PickerColor color, LocalizedText name, int row, int column, bool markSelected = true, string? storedHex = null)
        {
            var hex = storedHex ?? DisplayHex(color);
            var active = markSelected && selected is PickerColor current && WorkbookColorPalette.Same(current, color);
            var chip = new WorkbookButton { Width = 16, Height = 16, Content = active ? new WorkbookIcon("Check", 10) : null };
            chip.Classes.Add("picker-chip");
            chip.Click += (_, _) => Choose(color, hex);
            chip.Background = hex.Length == 0 ? Brushes.Transparent : ConditionalRenderer.Brush(hex);
            if (string.Equals(hex, "#FFFFFF", StringComparison.OrdinalIgnoreCase))
            { chip.BorderThickness = new Thickness(1); chip.Bind(Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, chip.GetResourceObservable("Menu-grey-300")); }
            chip.Foreground = ConditionalRenderer.Brush(hex.Length > 0 && WorkbookColorPalette.IsLight(hex) ? "#272525" : "#FFFFFF");
            name.Describe(chip);
            navigation.Add((chip, new PickerPosition(row, column)));
            return chip;
        }

        var themed = Section(Text.Key("color_picker.themed_colors"));
        var tints = new StackPanel { Spacing = 4 };
        for (var row = 0; row < 6; row++)
        {
            var colors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            for (var slot = 0; slot < 10; slot++)
            {
                var color = WorkbookColorPalette.ThemeColors[row * 10 + slot];
                var slotIndex = slot; var tint = color.Tint;
                var name = Text.Compose(t => ThemeColorLabel(t, slotIndex) + (tint == 0 ? "" : $" {tint:+0.##;-0.##}"));
                colors.Children.Add(Chip(color, name, row + 1, slot));
            }
            if (row == 0) themed.Children.Add(colors); else tints.Children.Add(colors);
        }
        themed.Children.Add(tints);
        palette.Children.Add(WorkbookMenus.Divider());
        var standardSection = Section(Text.Key("color_picker.standard_colors"));
        var standards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (var column = 0; column < WorkbookColorPalette.StandardHex.Length; column++)
        {
            var hex = WorkbookColorPalette.StandardHex[column];
            standards.Children.Add(Chip(PickerColor.Rgb(hex), Text.Compose(t => t["color_picker.standard_colors"] + " " + hex), 7, column));
        }
        standardSection.Children.Add(standards);
        palette.Children.Add(WorkbookMenus.Divider());
        var recentSection = Section(Text.Key("color_picker.recent"));
        var history = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
        recentSection.Children.Add(history);
        for (var column = 0; column < recent.Count; column++)
        {
            var color = recent[column];
            recentHex.TryGetValue(color.StyleValue, out var displayHex);
            history.Children.Add(Chip(color, Text.Compose(t => t["color_picker.recent"] + " " + color.StyleValue), 8, column, false, displayHex));
        }

        var selectedHex = selected is PickerColor chosen && WorkbookColorPalette.ValidHex(DisplayHex(chosen))
            ? DisplayHex(chosen) : "#272525";
        Button? more = null;
        void CancelAdvanced()
        {
            advanced.IsVisible = false; palette.IsVisible = true; panel.Width = 220; more?.Focus();
        }
        var advancedPicker = new WorkbookAdvancedColorPicker(hex => Choose(PickerColor.Rgb(hex), hex), CancelAdvanced, Text);
        advanced.Children.Add(advancedPicker);
        more = Button(Text.Key("color_picker.add"), () =>
        {
            palette.IsVisible = false;
            advanced.IsVisible = true;
            panel.Width = 240;
            advancedPicker.Reset(selectedHex);
            advancedPicker.HexInput.Focus(); advancedPicker.HexInput.SelectAll();
        }, Text.Key("color_picker.add"));
        more.Content = new WorkbookIcon("Plus"); more.Width = more.Height = 16;
        more.Padding = new Thickness(0); more.MinHeight = more.MinWidth = 0;
        more.Classes.Add("picker-chip"); history.Children.Add(more);
        navigation.Add((more, new PickerPosition(8, recent.Count)));
        panel.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (advanced.IsVisible) CancelAdvanced();
                else close();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Tab && e.Source is Control source)
            {
                var stops = advanced.IsVisible
                    ? advancedPicker.FocusStops
                    : navigation.Select(item => (Control)item.Button).ToArray();
                var current = Array.IndexOf(stops, source);
                if (current >= 0)
                {
                    var offset = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
                    stops[(current + offset + stops.Length) % stops.Length].Focus();
                    e.Handled = true;
                }
                return;
            }
            if (advanced.IsVisible || e.Source is not Button focused) return;
            var index = navigation.FindIndex(item => item.Button == focused);
            if (index < 0) return;
            if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
            var target = WorkbookColorPalette.Next(navigation.Select(item => item.Position).ToArray(), index, e.Key);
            if (target != index) navigation[target].Button.Focus();
            e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Dispatcher.UIThread.Post(() => { if (panel.IsEffectivelyVisible) defaultButton.Focus(); });
        return panel;
    }
}
