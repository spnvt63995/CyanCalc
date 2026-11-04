#![allow(clippy::unwrap_used)]

use crate::{test::user_model::util::new_empty_user_model, UserModel};

fn snapshot(model: &UserModel) -> Vec<(String, String)> {
    let mut cells = Vec::new();
    for sheet in 0..2 {
        for row in 1..=10 {
            for column in 1..=10 {
                cells.push((
                    model.get_cell_content(sheet, row, column).unwrap(),
                    model.get_formatted_cell_value(sheet, row, column).unwrap(),
                ));
            }
        }
    }
    cells
}

fn populated() -> UserModel<'static> {
    let mut model = new_empty_user_model();
    model.new_sheet().unwrap();
    for row in 1..=4 {
        for column in 1..=4 {
            model
                .set_user_input(0, row, column, &(row * 10 + column).to_string())
                .unwrap();
        }
    }
    for (row, column, formula) in [
        (1, 6, "=SUM(B1:B3)"),
        (2, 6, "=SUM(A1:D4)"),
        (3, 6, "=$B$2+B3+$C4+D$1"),
        (4, 6, "=SUM(B2:C3)"),
        (5, 6, "=B2+#REF!"),
        (6, 6, "=#REF!"),
        (7, 6, "=F1*2"),
        (8, 6, "=SUM(A:A)"),
        (9, 6, "=SUM(2:3)"),
    ] {
        model.set_user_input(0, row, column, formula).unwrap();
    }
    for (row, formula) in [
        (1, "=SUM(Sheet1!B1:B3)"),
        (2, "=Sheet1!$B$2"),
        (3, "=SUM(Sheet1!B2:C3)"),
        (4, "=Sheet1!A1+#REF!"),
    ] {
        model.set_user_input(1, row, 1, formula).unwrap();
    }
    // A load clears setup history and gives the receiver the same formula table.
    UserModel::from_bytes(&model.to_bytes(), "en").unwrap()
}

#[test]
fn rows_and_columns_restore_formulas_and_replay_diffs() {
    for rows in [false, true] {
        for count in [1, 2] {
            let mut model = populated();
            let mut peer = UserModel::from_bytes(&model.to_bytes(), "en").unwrap();
            let before = snapshot(&model);
            assert!(!model.can_undo());
            if rows {
                model.delete_rows(0, 2, count).unwrap();
            } else {
                model.delete_columns(0, 2, count).unwrap();
            }
            let deleted = snapshot(&model);
            peer.apply_external_diffs(&model.flush_send_queue())
                .unwrap();
            assert_eq!(snapshot(&peer), deleted);
            for _ in 0..3 {
                model.undo().unwrap();
                assert_eq!(snapshot(&model), before, "undo rows={rows} count={count}");
                assert!(!model.can_undo(), "delete must be one history step");
                peer.apply_external_diffs(&model.flush_send_queue())
                    .unwrap();
                assert_eq!(snapshot(&peer), before, "remote undo");
                let loaded = UserModel::from_bytes(&model.to_bytes(), "en").unwrap();
                assert_eq!(snapshot(&loaded), before, "saved workbook");
                model.redo().unwrap();
                assert_eq!(snapshot(&model), deleted, "redo");
                peer.apply_external_diffs(&model.flush_send_queue())
                    .unwrap();
                assert_eq!(snapshot(&peer), deleted, "remote redo");
            }
        }
    }
}

#[test]
fn deletion_history_survives_interleaved_edits_and_sheet_moves() {
    let mut model = populated();
    let before = snapshot(&model);
    model.delete_columns(0, 2, 2).unwrap();
    model.set_user_input(0, 10, 1, "=123").unwrap();
    model.move_sheet(1, 0).unwrap();
    model.undo().unwrap();
    model.undo().unwrap();
    model.undo().unwrap();
    assert_eq!(snapshot(&model), before);
}

#[test]
fn deletion_history_preserves_dynamic_and_cse_arrays() {
    for rows in [false, true] {
        let mut model = populated();
        model.set_user_input(1, 6, 1, "=Sheet1!B2:C3").unwrap();
        model
            .set_user_array_formula(1, 6, 5, 2, 2, "=Sheet1!B2:C3")
            .unwrap();
        let before = snapshot(&model);
        if rows {
            model.delete_rows(0, 2, 1).unwrap();
        } else {
            model.delete_columns(0, 2, 1).unwrap();
        }
        for _ in 0..2 {
            model.undo().unwrap();
            assert_eq!(snapshot(&model), before);
            assert!(matches!(
                model.model.get_cell_structure(1, 6, 5).unwrap(),
                crate::model::CellStructure::ArrayFormula { range: (2, 2) }
            ));
            model.redo().unwrap();
        }
    }
}

#[test]
fn failed_deletion_preserves_formula_history() {
    let mut model = populated();
    let before = snapshot(&model);
    assert!(model.delete_rows(0, 0, 1).is_err());
    assert!(model.delete_columns(0, 2, 0).is_err());
    assert!(!model.can_undo());
    assert_eq!(snapshot(&model), before);
}
