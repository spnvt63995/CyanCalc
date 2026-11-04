use bitcode::{Decode, Encode};

use crate::{expressions::types::Area, types::Cell, UserModel};

/// A surviving formula before a structural delete. Formula indices remain valid
/// because the shared formula table is append-only during editing.
#[derive(Clone, Encode, Decode)]
pub(crate) struct FormulaSnapshot {
    sheet: u32,
    row: i32,
    column: i32,
    cell: Cell,
}

impl<'a> UserModel<'a> {
    pub(super) fn formulas_outside_deleted_area(&self, area: &Area) -> Vec<FormulaSnapshot> {
        let mut formulas = Vec::new();
        for (sheet, worksheet) in self.model.workbook.worksheets.iter().enumerate() {
            if worksheet.shared_formulas.is_empty() {
                continue;
            }
            for (row, column, cell) in worksheet.sheet_data.cells() {
                let deleted = sheet as u32 == area.sheet
                    && row >= area.row
                    && row < area.row + area.height
                    && column >= area.column
                    && column < area.column + area.width;
                if !deleted && cell.get_formula().is_some() {
                    formulas.push(FormulaSnapshot {
                        sheet: sheet as u32,
                        row,
                        column,
                        cell: cell.into_owned(),
                    });
                }
            }
        }
        formulas
    }

    pub(super) fn retain_rewritten_formulas(
        &self,
        formulas: &mut Vec<FormulaSnapshot>,
        area: &Area,
        rows: bool,
    ) {
        formulas.retain(|old| {
            let mut row = old.row;
            let mut column = old.column;
            if old.sheet == area.sheet {
                if rows && row >= area.row + area.height {
                    row -= area.height;
                }
                if !rows && column >= area.column + area.width {
                    column -= area.width;
                }
            }
            let current = self.model.workbook.worksheets[old.sheet as usize].cell(row, column);
            match (current.as_deref(), &old.cell) {
                (Some(Cell::CellFormula { f, .. }), Cell::CellFormula { f: old_f, .. }) => {
                    f != old_f
                }
                (
                    Some(Cell::ArrayFormula { f, r, kind, .. }),
                    Cell::ArrayFormula {
                        f: old_f,
                        r: old_r,
                        kind: old_kind,
                        ..
                    },
                ) => f != old_f || r != old_r || kind != old_kind,
                _ => true,
            }
        });
    }

    pub(super) fn restore_deleted_formulas(
        &mut self,
        formulas: &[FormulaSnapshot],
    ) -> Result<(), String> {
        // Clear the current dynamic spills before restoring their original anchors.
        // Fixed-size CSE anchors keep their original kind and dimensions.
        let mut sheets = std::collections::HashSet::new();
        for formula in formulas {
            if sheets.insert(formula.sheet) {
                self.model.reset_dynamic_array_spills(formula.sheet)?;
            }
        }
        for formula in formulas {
            self.model
                .workbook
                .worksheet_mut(formula.sheet)?
                .update_cell(formula.row, formula.column, formula.cell.clone())?;
        }
        Ok(())
    }
}
