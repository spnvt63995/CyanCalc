//! Read-only preflight for relative font sizes. The pinned engine writes each
//! cell before validating later cells, and only records history after success.
use std::collections::HashMap;

use ironcalc::base::{expressions::types::Area, types::Style, UserModel};
use serde_json::Value;

use crate::protocol::{arg, text};

// The pinned engine's worksheet limits are not exported by its Rust API.
const LAST_COLUMN: i32 = 16_384;
const LAST_ROW: i32 = 1_048_576;

pub(crate) fn validate(model: &UserModel, request: &Value) -> Result<(), String> {
    if request.get("style_path").and_then(Value::as_str) != Some("font.size_delta") {
        return Ok(());
    }
    let area: Area = arg(request, "range")?;
    if area.width <= 0 || area.height <= 0 {
        return Ok(());
    }
    let input = text(request, "value")?;
    let delta: i32 = input
        .parse()
        .map_err(|_| format!("Invalid value for font size: '{input}'."))?;
    let next = |size: i32| -> Result<i32, String> {
        let size = i64::from(size) + i64::from(delta);
        if !(1..=i64::from(i32::MAX)).contains(&size) {
            return Err(format!("Invalid value for font size: '{size}'."));
        }
        Ok(size as i32)
    };
    let row_end = i64::from(area.row) + i64::from(area.height);
    let column_end = i64::from(area.column) + i64::from(area.width);
    if area.row < 1
        || area.column < 1
        || row_end > i64::from(LAST_ROW) + 1
        || column_end > i64::from(LAST_COLUMN) + 1
    {
        return Err("Invalid font size range".into());
    }
    let engine = model.get_model();
    let sheet = engine.workbook.worksheet(area.sheet)?;
    let size = |row, column| -> Result<i32, String> {
        Ok(engine.get_style_for_cell(area.sheet, row, column)?.font.sz)
    };
    let default_size = Style::default().font.sz;

    // Follow the pinned engine's sparse row/column traversal, including cells
    // visited twice. Only provisional sizes for one row or column are stored;
    // neither the workbook nor its history is copied or modified.
    if area.row == 1 && area.height == LAST_ROW {
        let data_rows = sheet.sheet_data.rows();
        for column in area.column..column_end as i32 {
            let column_size = next(
                engine
                    .get_column_style(area.sheet, column)?
                    .map_or(default_size, |s| s.font.sz),
            )?;
            let mut sizes = HashMap::new();
            for row in &sheet.rows {
                // The engine changes the column before visiting its cells.
                // An empty row with only a height inherits that new size.
                let current = if sheet.cell(row.r, column).is_some() || row.custom_format {
                    size(row.r, column)?
                } else {
                    column_size
                };
                sizes.insert(row.r, next(current)?);
            }
            for &row in &data_rows {
                if let Some(&current) = sizes.get(&row) {
                    sizes.insert(row, next(current)?);
                } else if sheet.cell(row, column).is_some() {
                    next(size(row, column)?)?;
                }
            }
        }
    } else if area.column == 1 && area.width == LAST_COLUMN {
        for row in area.row..row_end as i32 {
            let mut sizes = HashMap::new();
            for column in sheet.sheet_data.columns_in_row(row) {
                sizes.insert(column, next(size(row, column)?)?);
            }
            for column_style in &sheet.cols {
                for column in column_style.min..=column_style.max {
                    let current = match sizes.get(&column) {
                        Some(&current) => current,
                        None => size(row, column)?,
                    };
                    sizes.insert(column, next(current)?);
                }
            }
            next(
                engine
                    .get_row_style(area.sheet, row)?
                    .map_or(default_size, |s| s.font.sz),
            )?;
        }
    } else {
        for row in area.row..row_end as i32 {
            for column in area.column..column_end as i32 {
                next(size(row, column)?)?;
            }
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests;
