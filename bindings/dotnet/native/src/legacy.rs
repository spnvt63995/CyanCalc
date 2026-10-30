//! Compatibility names used by the Avalonia view adapter. Every workbook action
//! is translated to the generated WASM API, never independently implemented here.
use crate::{
    protocol::{arg, text},
    upstream_api::Model,
    upstream_dispatch,
};
use serde_json::{json, Value};

pub fn request(model: &Model, input: &Value) -> Result<Option<Value>, String> {
    let op = text(input, "op")?;
    let name = match op {
        // These return desktop-specific aggregate shapes, handled in protocol.rs.
        "get_named_style" | "builtin_styles" => return Ok(None),
        "set_array_formula" => "setUserArrayFormula",
        "select_sheet" => "setSelectedSheet",
        "select_cell" => "setSelectedCell",
        "select_range" => "setSelectedRange",
        "select_area" => "onAreaSelecting",
        "expand_selection" => "onExpandSelectedRange",
        "navigate_edge" => "onNavigateToEdgeInDirection",
        "scroll" => "setTopLeftVisibleCell",
        "sheet_color" => "setSheetColor",
        "move_rows" => "moveRows",
        "move_columns" => "moveColumns",
        "row_height" => "setRowsHeight",
        "column_width" => "setColumnsWidth",
        "hide_rows" => "setRowsHidden",
        "hide_columns" => "setColumnsHidden",
        "freeze_rows" => "setFrozenRowsCount",
        "freeze_columns" => "setFrozenColumnsCount",
        "show_grid_lines" => "setShowGridLines",
        "style" => "updateRangeStyle",
        "border" => "setAreaWithBorder",
        "paste_styles" => "onPasteStyles",
        "autofill_rows" => "autoFillRows",
        "autofill_columns" => "autoFillColumns",
        "copy" => "copyToClipboard",
        "paste" => "pasteFromClipboard",
        "paste_csv" => "pasteCsvText",
        "serialize" => "toBytes",
        "deserialize" => "fromBytes",
        "import_xlsx" => "fromXlsx",
        "export_xlsx" => "toXlsx",
        "defined_names" => "getDefinedNameList",
        "delete_defined_name" => "deleteDefinedName",
        "validate_defined_name" => "isValidDefinedName",
        "named_styles" => "getNamedStyleList",
        "apply_named_style" => "onApplyNamedStyle",
        "conditional_formats" => "getConditionalFormattingList",
        "cf_format" => "getDxfForConditionalFormatting",
        "add_cf" => "addConditionalFormatting",
        "update_cf" => "updateConditionalFormatting",
        "delete_cf" => "deleteConditionalFormatting",
        "raise_cf" => "raiseConditionalFormattingPriority",
        "lower_cf" => "lowerConditionalFormattingPriority",
        "format_settings" => "getFmtSettings",
        "completion" => "getFormulaCompletion",
        "last_non_empty_before" => "getLastNonEmptyInRowBeforeColumn",
        "first_non_empty_after" => "getFirstNonEmptyInRowAfterColumn",
        "navigate" => match text(input, "key")? {
            "Right" => "onArrowRight",
            "Left" => "onArrowLeft",
            "Up" => "onArrowUp",
            "Down" => "onArrowDown",
            "PageDown" => "onPageDown",
            "PageUp" => "onPageUp",
            key => return Err(format!("Unknown navigation key {key}")),
        },
        "clear" => match text(input, "kind")? {
            "all" => "rangeClearAll",
            "contents" => "rangeClearContents",
            "formatting" => "rangeClearFormatting",
            kind => return Err(format!("Unknown clear kind {kind}")),
        },
        _ => match upstream_dispatch::legacy_name(op) {
            Some(name) => name,
            None => return Ok(None),
        },
    };
    let mut result = input.clone();
    let output = result.as_object_mut().ok_or("Expected request object")?;
    output.insert("op".into(), json!(name));
    let view = model.model.get_selected_view();
    for (key, value) in [
        ("sheet", json!(view.sheet)),
        ("row", json!(view.row)),
        ("column", json!(view.column)),
    ] {
        output.entry(key.to_owned()).or_insert(value);
    }
    let renames: &[(&str, &str)] = match op {
        "set_array_formula" => &[("input", "formula")],
        "apply_external_diffs" => &[("data", "diffs")],
        "select_area" => &[("row", "target_row"), ("column", "target_column")],
        "scroll" => &[("row", "top_row"), ("column", "top_column")],
        "move_sheet" => &[("index", "new_index")],
        "insert_rows" | "delete_rows" | "move_rows" => &[("count", "row_count")],
        "insert_columns" | "delete_columns" | "move_columns" => &[("count", "column_count")],
        "row_height" | "hide_rows" => &[("row", "row_start"), ("end", "row_end")],
        "column_width" | "hide_columns" => &[("column", "column_start"), ("end", "column_end")],
        "show_grid_lines" => &[("show", "show_grid_lines")],
        "merge_cells" | "merge_cells_center" | "merge_cells_across" | "merge_cells_down"
        | "unmerge_cells" => &[("area", "range")],
        "style" => &[("area", "range"), ("path", "style_path")],
        "border" => &[("border", "border_area")],
        "autofill_rows" => &[("area", "source_area"), ("to", "to_row")],
        "autofill_columns" => &[("area", "source_area"), ("to", "to_column")],
        "paste" => &[("data", "clipboard"), ("cut", "is_cut")],
        "paste_csv" => &[("text", "csv")],
        "deserialize" | "import_xlsx" => &[("data", "bytes")],
        "update_defined_name" => &[("formula", "new_formula")],
        "update_cf" => &[("range", "new_range"), ("rule", "new_rule")],
        "cycle_reference" => &[("formula", "value")],
        _ => &[],
    };
    for (old, new) in renames {
        if let Some(value) = output.get(*old).cloned() {
            output.insert((*new).into(), value);
        }
    }
    if op == "select_range" {
        let [r1, c1, r2, c2]: [i32; 4] = arg(input, "range")?;
        for (key, value) in [
            ("start_row", r1),
            ("start_column", c1),
            ("end_row", r2),
            ("end_column", c2),
        ] {
            output.insert(key.into(), json!(value));
        }
    }
    if op == "navigate_edge" {
        output.insert(
            "direction".into(),
            json!(format!("Arrow{}", text(input, "key")?)),
        );
    }
    if op == "clear" {
        let area: ironcalc::base::expressions::types::Area = arg(input, "area")?;
        output.insert("sheet".into(), json!(area.sheet));
        output.insert("start_row".into(), json!(area.row));
        output.insert("start_column".into(), json!(area.column));
        output.insert("end_row".into(), json!(area.row + area.height - 1));
        output.insert("end_column".into(), json!(area.column + area.width - 1));
    }
    if matches!(op, "deserialize" | "import_xlsx") {
        for (key, value) in [("language_id", "en"), ("locale", "en"), ("timezone", "UTC")] {
            output.entry(key.to_owned()).or_insert(json!(value));
        }
    }
    Ok(Some(result))
}
