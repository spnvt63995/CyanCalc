using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CyanCalc.Controls.Preview;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    internal static Border DrawerPreview(Control sample, Control title)
    {
        var content = new StackPanel { Spacing = 8 }; content.Children.Add(sample); content.Children.Add(title);
        var border = new Border { Child = content, Padding = new Thickness(12, 24), Margin = new Thickness(-12, -16, -12, 0), BorderThickness = new Thickness(0, 0, 0, 1) };
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Dialog-grey-300")); return border;
    }
    public void ShowThemes() => OpenDrawerFeature("themes.panel_title", () => ShowThemes(null));
    private void ShowThemes(ThemesListState? state)
    {
        if (Session is not { } model) return;
        var panel = OpenDrawer(Text.Key("themes.panel_title"));
        panel.Margin = new Thickness(0); panel.Spacing = 0;
        var themes = state?.Themes ?? model.Query("themes").EnumerateArray().Select(t => t.Clone()).ToArray();
        JsonElement[] AllThemes()
        {
            var current = model.GetTheme();
            return themes.Any(theme => StylePreviewResolver.SameTheme(current, theme)) ? themes : [.. themes, current];
        }
        var selectedIndex = state?.SelectedIndex ?? Math.Max(0, Array.FindIndex(AllThemes(), theme => StylePreviewResolver.SameTheme(model.GetTheme(), theme)));
        ThemesListState ListState() => new(themes.Select(t => t.Clone()).ToArray(), selectedIndex);
        _captureDrawerState = ListState;
        var styles = model.Query("builtin_styles").EnumerateArray().ToArray();
        string Font(string name) => styles.First(x => x[0].GetString() == name)[1].GetProperty("font").GetProperty("name").GetString()!;
        PreviewPanelBinding.Bind(panel, model, () =>
        {
            panel.Children.Clear();
            var all = AllThemes();
            for (var i = 0; i < all.Length; i++)
            {
                var index = i; var theme = all[i].Clone();
                var tile = new ThemePreviewTile(theme, selectedIndex == index, Font("Title"), Font("Normal"), Text);
                tile.SetCustomize(() => { if (CanUseConditionalPanel(model, panel)) EditTheme(ListState(), theme); }, CanEdit);
                tile.Click += (_, _) =>
                {
                    if (!CanUseConditionalPanel(model, panel)) return;
                    selectedIndex = index; Command("set_theme", new { theme });
                };
                panel.Children.Add(tile);
            }
        });
    }
    private void EditTheme(ThemesListState listState, JsonElement editing, JsonObject? draft = null)
    {
        if (!CanEdit || Session is null) return;
        var theme = draft is null ? JsonNode.Parse(editing.GetRawText())!.AsObject() : (JsonObject)draft.DeepClone();
        var model = Session;
        var panel = OpenDrawer(Text.Key("themes.customize_theme"), feature: "themes.panel_title",
            back: () => ShowThemes(listState), backLabel: Text.Key("themes.back_to_list"));
        panel.Spacing = 0;
        _captureDrawerState = () => new ThemeDraftState((JsonObject)theme.DeepClone(), listState);
        var preview = new ContentControl { Width = 52, Height = 36, HorizontalAlignment = HorizontalAlignment.Center };
        var previewTitle = new TextBlock { Text = theme["name"]?.GetValue<string>(), FontSize = 14, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(DrawerPreview(preview, previewTitle));
        var styles = model.Query("builtin_styles").EnumerateArray().ToArray();
        string Font(string name) => styles.First(x => x[0].GetString() == name)[1].GetProperty("font").GetProperty("name").GetString()!;
        void UpdatePreview() => preview.Content = new ThemePreviewTile(JsonSerializer.SerializeToElement(theme), false, Font("Title"), Font("Normal"), Text, previewOnly: true) { IsHitTestVisible = false };
        UpdatePreview();
        StackPanel? colorSection = null;
        foreach (var (key, title) in new[] { ("dk1", Text.Key("themes.text_color_label")), ("lt1", Text.Key("themes.bg_color_label")), ("dk2", Text.Key("themes.dark_color_label")), ("lt2", Text.Key("themes.light_color_label")),
            ("accent1", Text.Key("themes.accent_color_label", ("n", 1))), ("accent2", Text.Key("themes.accent_color_label", ("n", 2))), ("accent3", Text.Key("themes.accent_color_label", ("n", 3))), ("accent4", Text.Key("themes.accent_color_label", ("n", 4))), ("accent5", Text.Key("themes.accent_color_label", ("n", 5))), ("accent6", Text.Key("themes.accent_color_label", ("n", 6))),
            ("hlink", Text.Key("themes.hlink_color_label")), ("fol_hlink", Text.Key("themes.fol_hlink_color_label")) })
        {
            if (key is "dk1" or "dk2" or "accent1" or "hlink")
            {
                colorSection = new StackPanel { Spacing = 12 };
                var frame = new Border { Child = colorSection, Padding = new Thickness(12, 16), Margin = new Thickness(-12, 0), BorderThickness = new Thickness(0, 0, 0, key == "hlink" ? 0 : 1) };
                frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Dialog-grey-300")); panel.Children.Add(frame);
            }
            var field = new StackPanel { Spacing = 4 }; var label = Label(title); label.FontWeight = FontWeight.Medium; field.Children.Add(label); colorSection!.Children.Add(field);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
            var color = theme[key]?.GetValue<string>() ?? "#000000";
            var input = new TextBox { Text = color };
            global::Avalonia.Automation.AutomationProperties.SetName(input, key);
            var swatch = new PanelColorButton(this, model, title, JsonValue.Create(color), "Theme" + key, defaultToCurrent: true);
            input.PropertyChanged += (_, e) => { if (e.Property != TextBox.TextProperty) return; theme[key] = input.Text; if (WorkbookColorPalette.ValidHex(input.Text)) swatch.Value = JsonValue.Create(input.Text); UpdatePreview(); };
            swatch.Changed += () => { var hex = model.ResolveColor(JsonSerializer.SerializeToElement(swatch.Value)); if (input.Text != hex) input.Text = hex; };
            row.Children.Add(swatch); Grid.SetColumn(input, 1); row.Children.Add(input); field.Children.Add(row);
        }
        void Save() => Run(() =>
        {
            if (!CanUseConditionalPanel(model, panel)) return;
            theme["name"] = Text["themes.custom_theme_name"];
            model.SetTheme(JsonSerializer.SerializeToElement(theme));
            ShowThemes(new ThemesListState([JsonSerializer.SerializeToElement(theme), .. listState.Themes.Where(t => t.GetProperty("name").GetString() != Text["themes.custom_theme_name"])], 0));
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button(Text.Key("common.cancel"), () => ShowThemes(listState)));
        actions.Children.Add(Button(Text.Key("themes.apply"), Save)); SetDrawerFooter(panel, actions);
        panel.KeyDown += (_, e) => { if (e.Key == Key.Enter && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) { Save(); e.Handled = true; } };
    }

    public void ShowSettings() => OpenDrawerFeature("regional_settings.title", () => ShowSettings(null));
    private void ShowSettings(SettingsState? draft)
    {
        if (Session is null || Snapshot is null) return;
        // RegionalSettings.tsx resets draft settings when the incoming values change.
        if (draft is not null && (draft.InitialLocale != Snapshot.Locale || draft.InitialTimezone != Snapshot.Timezone || draft.InitialLanguage != Snapshot.Language)) draft = null;
        var initialLocale = Snapshot.Locale; var initialTimezone = Snapshot.Timezone; var initialLanguage = Snapshot.Language;
        var panel = OpenDrawer(Text.Key("regional_settings.title"));
        panel.Spacing = 0;
        StackPanel Section(LocalizedText title, bool separator)
        {
            var body = new StackPanel { Spacing = 12 };
            var heading = Label(title); heading.FontSize = 14; heading.FontWeight = FontWeight.SemiBold; body.Children.Add(heading);
            var border = new Border { Child = body, Padding = new Thickness(12, 16), Margin = new Thickness(-12, 0), BorderThickness = new Thickness(0, 0, 0, separator ? 1 : 0) };
            border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Dialog-grey-300")); panel.Children.Add(border); return body;
        }
        // OpenDrawer owns the first section's top inset.
        panel.Margin = new Thickness(12, 0);
        var localeSection = Section(Text.Key("regional_settings.locale.title"), true);
        var localeFields = new StackPanel { Spacing = 8 }; localeSection.Children.Add(localeFields);
        var locale = Choice(localeFields, Text.Key("regional_settings.locale.locale_label"), Session.Query<string[]>("locales"), draft?.Locale ?? Snapshot.Locale, value => Text.Compose(_ => WorkbookLocale.DisplayName(value)));
        var model = Session; var language = draft?.Language ?? Snapshot.Language;
        var examples = new StackPanel { Spacing = 2 };
        var values = new List<TextBlock>();
        foreach (var key in new[] { "locale_example1", "locale_example2", "locale_example3" })
        {
            var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 4 };
            var label = Label(Text.Key("regional_settings.locale." + key)); label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("Dialog-grey-700")); row.Children.Add(label);
            var value = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, FontSize = 12 };
            value.Bind(TextBlock.ForegroundProperty, value.GetResourceObservable("Dialog-grey-500")); Grid.SetColumn(value, 1); row.Children.Add(value); values.Add(value); examples.Children.Add(row);
        }
        var helper = new Border { Child = examples, Padding = new Thickness(8), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
        helper.Bind(Border.BackgroundProperty, helper.GetResourceObservable("Dialog-grey-100"));
        helper.Bind(Border.BorderBrushProperty, helper.GetResourceObservable("Dialog-grey-300")); localeFields.Children.Add(helper);
        void RefreshExamples()
        {
            var code = locale.SelectedItem as string;
            var (number, date, separator) = code switch
            {
                "en" => ("1,234.56", "10/17/2026 09:21:06 PM", ","),
                "fr" => ("1 234,56", "17/10/2026 21:21:06", ";"),
                "de" => ("1.234,56", "17.10.2026 21:21:06", ";"),
                "es" or "it" => ("1.234,56", "17/10/2026 21:21:06", ";"),
                _ => ("1,234.56", "10/17/2026 09:21:06 PM", ",")
            };
            values[0].Text = number; values[1].Text = date;
            Text.Compose(t => $"{t["regional_settings.locale.delimiter_" + (separator == "," ? "comma" : "semicolon")]} ({separator})").Bind(values[2], TextBlock.TextProperty);
        }
        locale.SelectionChanged += (_, _) => RefreshExamples(); RefreshExamples();
        var timezoneSection = Section(Text.Key("regional_settings.timezone.title"), false);
        var timezone = Choice(timezoneSection, Text.Key("regional_settings.timezone.timezone_label"), model.Query<string[]>("timezones"), draft?.Timezone ?? Snapshot.Timezone);
        _captureDrawerState = () => new SettingsState((string)locale.SelectedItem!, (string)timezone.SelectedItem!, language, initialLocale, initialTimezone, initialLanguage);
        var timezoneHelper = Label(Text.Key("regional_settings.timezone.timezone_helper")); timezoneHelper.LineHeight = 16.8;
        timezoneHelper.Bind(TextBlock.ForegroundProperty, timezoneHelper.GetResourceObservable("Dialog-grey-500")); ((StackPanel)timezone.Parent!).Children.Add(timezoneHelper);
        var applySettings = Button(Text.Key("num_fmt.save"), () => Run(() =>
        {
            if (!CanUseConditionalPanel(model, panel)) return;
            Session.Execute("set_locale", new { locale = locale.SelectedItem });
            Session.Execute("set_timezone", new { timezone = timezone.SelectedItem });
            Session.Execute("set_language", new { language });
            _drawer.IsVisible = false; RefreshDrawerButtons(); FocusCell();
        }));
        applySettings.IsEnabled = CanEdit; SetDrawerFooter(panel, applySettings);
    }

    internal static TextBox Field(StackPanel panel, string label, string initial)
    {
        panel.Children.Add(Label(label)); var input = new TextBox { Text = initial, MinHeight = 30, Padding = new Thickness(7, 4) };
        global::Avalonia.Automation.AutomationProperties.SetName(input, label); panel.Children.Add(input); return input;
    }
    internal static ComboBox Choice(StackPanel panel, string label, string[] choices, string? selected = null)
    {
        panel.Children.Add(Label(label)); var input = new ComboBox { ItemsSource = choices, SelectedItem = selected ?? choices.FirstOrDefault(), HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 30 };
        global::Avalonia.Automation.AutomationProperties.SetName(input, label); panel.Children.Add(input); return input;
    }
}
