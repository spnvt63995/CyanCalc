#[cfg(test)]
use base64::{engine::general_purpose::STANDARD, Engine};
use ironcalc::base::{
    cell::CellValue,
    expressions::types::Area,
    types::{CellType, Color},
    UserModel,
};
use serde::{de::DeserializeOwned, Serialize};
use serde_json::{json, Value};

pub(crate) fn arg<T: DeserializeOwned>(request: &Value, name: &str) -> Result<T, String> {
    serde_json::from_value(
        request
            .get(name)
            .cloned()
            .ok_or_else(|| format!("Missing {name}"))?,
    )
    .map_err(|error| format!("Invalid {name}: {error}"))
}
pub(crate) fn optional<T: DeserializeOwned>(
    request: &Value,
    name: &str,
) -> Result<Option<T>, String> {
    serde_json::from_value(request.get(name).cloned().unwrap_or(Value::Null))
        .map_err(|error| format!("Invalid {name}: {error}"))
}
pub(crate) fn value<T: Serialize>(input: T) -> Result<Value, String> {
    serde_json::to_value(input).map_err(|error| error.to_string())
}
fn cell_value(input: CellValue) -> Result<Value, String> {
    match input {
        CellValue::None => Ok(Value::Null),
        CellValue::String(text) => Ok(Value::String(text)),
        CellValue::Number(number) => serde_json::Number::from_f64(number)
            .map(Value::Number)
            .ok_or_else(|| "Non-finite cell number cannot be represented in JSON".into()),
        CellValue::Boolean(boolean) => Ok(Value::Bool(boolean)),
    }
}
pub(crate) fn text<'a>(request: &'a Value, name: &str) -> Result<&'a str, String> {
    request
        .get(name)
        .and_then(Value::as_str)
        .ok_or_else(|| format!("Missing string {name}"))
}

/// Module-level helpers do not require a workbook or touch its history.
pub fn dispatch_utility(request: &Value) -> Result<Value, String> {
    if let Some(result) = crate::upstream_dispatch::utility(request) {
        return result;
    }
    let mut canonical = request.clone();
    if let Some(name) = crate::upstream_dispatch::legacy_name(text(request, "op")?) {
        canonical["op"] = json!(name);
        if let Some(result) = crate::upstream_dispatch::utility(&canonical) {
            return result;
        }
    }
    Err(format!(
        "Unknown utility operation '{}'",
        text(request, "op")?
    ))
}

fn resolve_style_colors(model: &UserModel, data: &mut Value) {
    match data {
        Value::Object(map) => {
            for (name, child) in map {
                if name == "color" || name.ends_with("_color") {
                    if let Ok(color) = serde_json::from_value::<Color>(child.clone()) {
                        *child = json!(model.resolve_color(&color));
                    }
                } else {
                    resolve_style_colors(model, child);
                }
            }
        }
        Value::Array(items) => {
            for item in items {
                resolve_style_colors(model, item);
            }
        }
        _ => (),
    }
}

fn cell(model: &UserModel, sheet: u32, row: i32, column: i32) -> Result<Value, String> {
    let mut style = value(model.get_extended_cell_style(sheet, row, column)?)?;
    resolve_style_colors(model, &mut style);
    let cell_type = match model.get_cell_type(sheet, row, column)? {
        CellType::Number => 1,
        CellType::Text => 2,
        CellType::LogicalValue => 4,
        CellType::ErrorValue => 16,
        CellType::Array => 64,
        CellType::CompoundData => 128,
    };
    Ok(json!({ "row": row, "column": column,
        "content": model.get_cell_content(sheet, row, column)?,
        "display": model.get_formatted_cell_value(sheet, row, column)?,
        "cell_type": cell_type, "extended_style": style,
        "array_structure": model.get_cell_array_structure(sheet, row, column)? }))
}

fn dispatch_upstream(
    api: &mut crate::upstream_api::Model,
    request: &Value,
) -> Option<Result<Value, String>> {
    if request.get("op").and_then(Value::as_str) == Some("updateRangeStyle") {
        if let Err(error) = crate::style_validation::validate(&api.model, request) {
            return Some(Err(error));
        }
    }
    crate::upstream_dispatch::dispatch(api, request)
}

pub fn dispatch(api: &mut crate::upstream_api::Model, request: &Value) -> Result<Value, String> {
    if let Some(result) = dispatch_upstream(api, request) {
        return result;
    }
    if let Some(canonical) = crate::legacy::request(api, request)? {
        if let Some(result) = dispatch_upstream(api, &canonical) {
            return result;
        }
    }
    let model = &mut api.model;
    let op = text(request, "op")?;
    let view = model.get_selected_view();
    let sheet = optional::<u32>(request, "sheet")?.unwrap_or(view.sheet);
    let row = optional::<i32>(request, "row")?.unwrap_or(view.row);
    let column = optional::<i32>(request, "column")?.unwrap_or(view.column);
    match op {
        "info" => {
            return Ok(json!({ "abi": 1, "upstream": "297553dc3a37ec6b0f4821055220e3d26c4ac6a1" }))
        }
        "state" => {
            return Ok(json!({
                "name": model.get_name(), "view": view,
                "can_undo": model.can_undo(), "can_redo": model.can_redo(),
                "worksheets": model.get_worksheets_properties(),
                "merged_cells": model.get_merged_cells(sheet)?,
                "frozen_rows": model.get_frozen_rows_count(sheet)?,
                "frozen_columns": model.get_frozen_columns_count(sheet)?,
                "show_grid_lines": model.get_show_grid_lines(sheet)?,
                "locale": model.get_locale(), "timezone": model.get_timezone(), "language": model.get_language(),
                "cell": cell(model, sheet, view.row, view.column)?
            }))
        }
        "cell" => return cell(model, sheet, row, column),
        "raw_style" => return value(model.get_cell_style(sheet, row, column)?),
        "get_cell_value" => {
            return cell_value(
                model
                    .get_model()
                    .get_cell_value_by_index(sheet, row, column)?,
            )
        }
        "get_cell_value_by_ref" => {
            return cell_value(
                model
                    .get_model()
                    .get_cell_value_by_ref(text(request, "cell_ref")?)?,
            )
        }
        "get_cell_formula" => {
            return value(model.get_model().get_cell_formula(sheet, row, column)?)
        }
        "sheet_dimensions" => {
            let d = model.get_model().workbook.worksheet(sheet)?.dimension();
            return Ok(json!([d.min_row, d.max_row, d.min_column, d.max_column]));
        }
        "cells" => {
            let rows: Vec<i32> = arg(request, "rows")?;
            let columns: Vec<i32> = arg(request, "columns")?;
            let mut cells = Vec::new();
            for r in rows {
                for c in &columns {
                    cells.push(cell(model, sheet, r, *c)?);
                }
            }
            return Ok(json!(cells));
        }
        "dimensions" => {
            let rows: Vec<i32> = arg(request, "rows")?;
            let columns: Vec<i32> = arg(request, "columns")?;
            return Ok(json!({
                "rows": rows.iter().map(|r| model.get_row_height(sheet, *r)).collect::<Result<Vec<_>, _>>()?,
                "columns": columns.iter().map(|c| model.get_column_width(sheet, *c)).collect::<Result<Vec<_>, _>>()?
            }));
        }
        "set_viewport" => {
            model.set_window_width(arg(request, "width")?);
            model.set_window_height(arg(request, "height")?);
        }
        "copy_styles" => {
            let area: Area = arg(request, "area")?;
            let mut styles = Vec::new();
            for r in area.row..area.row + area.height {
                let mut row_styles = Vec::new();
                for c in area.column..area.column + area.width {
                    row_styles.push(model.get_extended_cell_style(area.sheet, r, c)?.style);
                }
                styles.push(row_styles);
            }
            return value(styles);
        }
        "reset_history" => {
            let language = model.get_language();
            *model = UserModel::from_bytes(&model.to_bytes(), "en")?;
            model.set_language(&language)?;
        }
        "builtin_styles" => return value(model.get_builtin_named_styles()),
        "get_named_style" => {
            return Ok(
                json!({ "style": model.get_named_style(text(request, "name")?)?, "includes": model.get_named_style_includes(text(request, "name")?)? }),
            )
        }
        "themes" => return value(ironcalc::base::themes::builtin_themes()),
        "locales" => return value(ironcalc::base::get_supported_locales()),
        "timezones" => return value(ironcalc::base::get_all_timezones()),
        "tokens" => {
            return value(ironcalc::base::expressions::lexer::util::get_tokens(text(
                request, "formula",
            )?))
        }
        _ => return Err(format!("Unknown operation '{op}'")),
    }
    Ok(Value::Null)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn module_reference_helpers_follow_pinned_engine() {
        for (number, name) in [(1, "A"), (26, "Z"), (27, "AA"), (16384, "XFD")] {
            assert_eq!(
                dispatch_utility(&json!({"op":"column_name_from_number","column":number})).unwrap(),
                name
            );
            assert_eq!(
                dispatch_utility(&json!({"op":"column_number_from_name","column":name})).unwrap(),
                number
            );
        }
        for number in [0, -1, 16385] {
            assert_eq!(
                dispatch_utility(&json!({"op":"column_name_from_number","column":number}))
                    .unwrap_err(),
                "Invalid column number"
            );
        }
        for (name, error) in [
            ("", "Invalid column name"),
            ("a", "Invalid column name"),
            ("A1", "Invalid column name"),
            ("É", "Invalid column name"),
            ("XFE", "Invalid column name"),
        ] {
            assert_eq!(
                dispatch_utility(&json!({"op":"column_number_from_name","column":name}))
                    .unwrap_err(),
                error
            );
        }
        for (name, quoted) in [
            ("", ""),
            ("Sheet1", "Sheet1"),
            ("My Sheet", "'My Sheet'"),
            ("O'Brien", "'O''Brien'"),
            ("A1", "'A1'"),
            ("R1C1", "'R1C1'"),
            ("B1048577", "B1048577"),
        ] {
            assert_eq!(
                dispatch_utility(&json!({"op":"quote_name","name":name})).unwrap(),
                quoted
            );
        }
    }
    #[test]
    fn hyperlink_requests_keep_upstream_shapes_and_history() {
        let mut m = crate::upstream_api::Model::new("links", "en", "UTC", "en").unwrap();
        let link = json!({"type":"External","target":"https://example.com","tooltip":null});
        let get = json!({"op":"get_cell_link","sheet":0,"row":2,"column":2});
        assert_eq!(dispatch(&mut m, &get).unwrap(), Value::Null);
        dispatch(&mut m, &json!({"op":"set_cell_link","sheet":0,"row":2,"column":2,"link":link,"label":"Example"})).unwrap();
        assert_eq!(dispatch(&mut m, &get).unwrap(), link);
        assert_eq!(
            dispatch(&mut m, &json!({"op":"get_links","sheet":0})).unwrap(),
            json!([{"row":2,"column":2,"dynamic":false,"type":"External","target":"https://example.com","tooltip":null}])
        );
        assert_eq!(
            dispatch(
                &mut m,
                &json!({"op":"get_cell_content","sheet":0,"row":2,"column":2})
            )
            .unwrap(),
            "Example"
        );
        dispatch(&mut m, &json!({"op":"undo"})).unwrap();
        assert_eq!(dispatch(&mut m, &get).unwrap(), Value::Null);
        assert_eq!(
            dispatch(
                &mut m,
                &json!({"op":"get_cell_content","sheet":0,"row":2,"column":2})
            )
            .unwrap(),
            ""
        );
        assert!(dispatch(
            &mut m,
            &json!({"op":"set_cell_link","sheet":0,"row":0,"column":2,"link":link})
        )
        .is_err());
    }
    #[test]
    fn edit_undo_roundtrip_and_invalid_request() {
        let mut m = crate::upstream_api::Model::new("test", "en", "UTC", "en").unwrap();
        dispatch(
            &mut m,
            &json!({"op":"set_user_input","row":1,"column":1,"input":"12"}),
        )
        .unwrap();
        dispatch(
            &mut m,
            &json!({"op":"set_user_input","row":1,"column":2,"input":"=A1*3"}),
        )
        .unwrap();
        assert_eq!(cell(&m.model, 0, 1, 2).unwrap()["display"], "36");
        dispatch(
            &mut m,
            &json!({"op":"set_user_input","row":1,"column":1,"input":"20"}),
        )
        .unwrap();
        assert_eq!(cell(&m.model, 0, 1, 2).unwrap()["display"], "60");
        dispatch(&mut m, &json!({"op":"undo"})).unwrap();
        assert_eq!(cell(&m.model, 0, 1, 2).unwrap()["display"], "36");
        let bytes = dispatch(&mut m, &json!({"op":"serialize"})).unwrap();
        dispatch(&mut m, &json!({"op":"deserialize","data":bytes})).unwrap();
        assert!(!m.can_undo());
        assert_eq!(cell(&m.model, 0, 1, 2).unwrap()["display"], "36");
        assert!(dispatch(
            &mut m,
            &json!({"op":"set_user_input","row":0,"input":"bad"})
        )
        .is_err());
        assert!(dispatch(&mut m, &json!({"op":"unknown"})).is_err());
    }

    #[test]
    fn previous_engine_ic_rejects_without_mutation_and_xlsx_migrates() {
        let bytes = include_bytes!("../../../../tests/fixtures/ironcalc-8f8c0f8f.ic");
        let xlsx = include_bytes!("../../../../tests/fixtures/ironcalc-8f8c0f8f.xlsx");
        let expected: Value =
            serde_json::from_str(include_str!("../../../../tests/fixtures/ironcalc-8f8c0f8f.json"))
                .unwrap();
        let mut model = crate::upstream_api::Model::new("upgrade", "en", "UTC", "en").unwrap();
        dispatch(
            &mut model,
            &json!({"op":"set_user_input","row":1,"column":1,"input":"existing document"}),
        )
        .unwrap();
        let before = model.to_bytes();
        let error = dispatch(
            &mut model,
            &json!({"op":"deserialize","data":STANDARD.encode(bytes)}),
        )
        .unwrap_err();
        assert!(error.starts_with("Error parsing workbook:"));
        assert_eq!(model.to_bytes(), before);
        assert!(model.can_undo());
        dispatch(
            &mut model,
            &json!({"op":"import_xlsx","name":"previous engine.xlsx","data":STANDARD.encode(xlsx)}),
        )
        .unwrap();
        for original in expected["cells"].as_array().unwrap() {
            let actual = cell(
                &model.model,
                original["sheet"].as_i64().unwrap() as u32,
                original["row"].as_i64().unwrap() as i32,
                original["column"].as_i64().unwrap() as i32,
            )
            .unwrap();
            assert_eq!(actual["content"], original["content"]);
            assert_eq!(actual["display"], original["display"]);
        }
        assert!(!model.can_undo());
        dispatch(
            &mut model,
            &json!({"op":"set_user_input","sheet":0,"row":1,"column":1,"input":"7"}),
        )
        .unwrap();
        assert_eq!(cell(&model.model, 0, 1, 2).unwrap()["display"], "10");
        assert_eq!(cell(&model.model, 0, 1, 3).unwrap()["display"], "20");
        dispatch(&mut model, &json!({"op":"undo"})).unwrap();
        assert_eq!(cell(&model.model, 0, 1, 2).unwrap()["display"], "5");
    }
}
