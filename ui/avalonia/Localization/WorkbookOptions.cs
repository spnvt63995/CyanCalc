namespace CyanCalc.Controls;

internal static class WorkbookOptions
{
    internal static LocalizedText RuleType(WorkbookText text, string value) => text.Key(value switch
    {
        "CellIs" => "conditional_formatting.rule_type_cell_value", "Text" => "conditional_formatting.rule_type_text",
        "Formula" => "conditional_formatting.rule_type_formula", "TimePeriod" => "conditional_formatting.rule_type_date",
        "DuplicateValues" => "conditional_formatting.rule_type_duplicate_values", "UniqueValues" => "conditional_formatting.rule_type_unique_values",
        "Blanks" => "conditional_formatting.rule_type_blanks", "NotBlanks" => "conditional_formatting.rule_type_no_blanks",
        "Errors" => "conditional_formatting.rule_type_errors", "NoErrors" => "conditional_formatting.rule_type_no_errors",
        "ColorScale" => "conditional_formatting.tab_color_scale", "DataBar" => "conditional_formatting.tab_data_bars",
        "IconSet" => "conditional_formatting.icon_sets_mode_preset", "IconRating" => "conditional_formatting.icon_sets_ratings",
        _ => "native.rule_types." + value
    });
    internal static LocalizedText Operator(WorkbookText text, string value) => text.Key("conditional_formatting.op_" + (value switch
    {
        "Equal" or "Equals" => "equals", "GreaterThan" => "greater_than", "GreaterThanOrEqual" => "greater_than_or_equal",
        "LessThan" => "less_than", "LessThanOrEqual" => "less_than_or_equal", "NotEqual" => "does_not_equal",
        "NotBetween" => "not_between", "DoesNotContain" => "does_not_contain", "BeginsWith" => "begins_with", "EndsWith" => "ends_with",
        "Last7Days" => "in_last_7_days", "Next7Days" => "in_next_7_days",
        "LastWeek" => "last_week", "ThisWeek" => "this_week", "NextWeek" => "next_week",
        "LastMonth" => "last_month", "ThisMonth" => "this_month", "NextMonth" => "next_month",
        "LastYear" => "last_year", "ThisYear" => "this_year", "NextYear" => "next_year",
        _ => value.ToLowerInvariant()
    }));
    internal static LocalizedText Threshold(WorkbookText text, string value) => text.Key(value == "Auto"
        ? "conditional_formatting.data_bars_type_automatic" : "conditional_formatting.color_scale_type_" + value.ToLowerInvariant());
}
