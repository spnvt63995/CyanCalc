using System.Text.Json;
using System.Text.Json.Nodes;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    // Only value snapshots cross document boundaries. Controls and callbacks bound
    // to the old model must be detached before the host can dispose that model.
    private Func<DrawerState>? _captureDrawerState;
    private abstract record DrawerState;
    private sealed record ConditionalListState(string Query, string Filter) : DrawerState;
    private sealed record ConditionalDraftState(JsonElement? Existing, string Range, ConditionalRuleEditor.Draft Rule, ConditionalListState List) : DrawerState;
    private sealed record NamesListState(string Query, string Filter) : DrawerState;
    private sealed record NameDraftState(DefinedName? Existing, string Name, string Formula, int? ScopeSheetId, string NameError, string FormulaError, NamesListState List) : DrawerState;
    private sealed record StylesListState(bool Manage) : DrawerState;
    private sealed record StyleDraftState(string? Existing, string Name, StyleEditor.Draft Editor, string NameError, bool Manage) : DrawerState;
    private sealed record ThemesListState(JsonElement[] Themes, int SelectedIndex) : DrawerState;
    private sealed record ThemeDraftState(JsonObject Theme, ThemesListState List) : DrawerState;
    private sealed record SettingsState(string Locale, string Timezone, string Language,
        string InitialLocale, string InitialTimezone, string InitialLanguage) : DrawerState;

    private void RestoreDrawerState(DrawerState state)
    {
        switch (state)
        {
            case ConditionalListState list: ShowConditionalFormats(list.Query, list.Filter); break;
            // Upstream retains editingRule/definedName/style identity when model changes.
            case ConditionalDraftState draft: EditConditionalFormat(draft.Existing, draft.List, draft); break;
            case NamesListState list: ShowNames(list.Query, list.Filter); break;
            case NameDraftState draft: EditName(draft.Existing, draft.List, draft); break;
            case StylesListState list: ShowNamedStyles(list.Manage); break;
            case StyleDraftState draft: EditNamedStyle(draft.Existing, draft); break;
            case ThemesListState list: ShowThemes(list); break;
            case ThemeDraftState draft: EditTheme(draft.List, JsonSerializer.SerializeToElement(draft.Theme), draft.Theme); break;
            case SettingsState settings: ShowSettings(settings); break;
        }
    }
}
