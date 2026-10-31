use super::*;
use crate::{protocol::dispatch, upstream_api::Model};
use serde_json::json;

fn area(row: i32, column: i32, width: i32, height: i32) -> Area {
    Area {
        sheet: 0,
        row,
        column,
        width,
        height,
    }
}

fn set(model: &mut Model, area: &Area, size: i32) {
    model
        .model
        .update_range_style(area, "font.size", &size.to_string())
        .unwrap();
}

const CASES: &[&str] = &[
    "last-cell",
    "first-cell",
    "column-cells",
    "row-cells",
    "column-styles",
    "row-styles",
    "inherited-row",
    "inherited-column",
    "column-revisit",
    "row-revisit",
    "height-only-row",
    "row-style-last",
];

fn fixture(case: &str, floor: i32) -> (Model, Area) {
    let mut m = Model::new("font sizes", "en", "UTC", "en").unwrap();
    let cell = area(1, 1, 1, 1);
    let column = area(1, 1, 1, LAST_ROW);
    let row = area(1, 1, LAST_COLUMN, 1);
    let range = match case {
        "last-cell" | "first-cell" | "row-cells" => {
            set(
                &mut m,
                &cell,
                if case == "first-cell" {
                    floor
                } else {
                    floor + 1
                },
            );
            set(
                &mut m,
                &area(1, 2, 1, 1),
                if case == "first-cell" {
                    floor + 1
                } else {
                    floor
                },
            );
            if case == "row-cells" {
                row
            } else {
                area(1, 1, 2, 1)
            }
        }
        "column-cells" => {
            set(&mut m, &cell, floor + 1);
            set(&mut m, &area(2, 1, 1, 1), floor);
            column
        }
        "column-styles" => {
            set(&mut m, &column, floor + 1);
            set(&mut m, &area(1, 2, 1, LAST_ROW), floor);
            area(1, 1, 2, LAST_ROW)
        }
        "row-styles" => {
            set(&mut m, &row, floor + 1);
            set(&mut m, &area(2, 1, LAST_COLUMN, 1), floor);
            area(1, 1, LAST_COLUMN, 2)
        }
        "inherited-row" => {
            set(&mut m, &row, floor);
            column
        }
        "inherited-column" => {
            set(&mut m, &column, floor);
            row
        }
        "column-revisit" => {
            set(&mut m, &row, floor + 1);
            m.model.set_user_input(0, 1, 1, "A").unwrap();
            column
        }
        "row-revisit" => {
            set(&mut m, &column, floor + 1);
            m.model.set_user_input(0, 1, 1, "A").unwrap();
            row
        }
        "height-only-row" => {
            set(&mut m, &column, floor + 1);
            m.model.set_rows_height(0, 1, 1, 30.0).unwrap();
            column
        }
        "row-style-last" => {
            set(&mut m, &row, floor);
            set(&mut m, &cell, floor + 1);
            row
        }
        _ => panic!("Unknown fixture"),
    };
    (m, range)
}

#[test]
fn rejected_deltas_preserve_the_workbook_redo_and_send_queue() {
    for case in CASES {
        let (mut m, range) = fixture(case, 1);
        // Keep a real Redo branch, independently of the style being rejected.
        m.model.set_user_input(0, 7, 7, "redo sentinel").unwrap();
        let with_sentinel = m.model.to_bytes();
        m.model.undo().unwrap();
        let before = m.model.to_bytes();
        m.model.flush_send_queue();
        let empty_queue = m.model.flush_send_queue();
        let request = json!({"op":"updateRangeStyle","range":range,"style_path":"font.size_delta","value":"-1"});
        let error = dispatch(&mut m, &request).unwrap_err();
        assert_eq!(error, "Invalid value for font size: '0'.", "{case}");
        assert_eq!(m.model.to_bytes(), before, "{case}: partial mutation");
        assert!(m.model.can_redo(), "{case}: lost redo");
        assert_eq!(
            m.model.flush_send_queue(),
            empty_queue,
            "{case}: sent a diff"
        );
        m.model.redo().unwrap();
        assert_eq!(m.model.to_bytes(), with_sentinel, "{case}: wrong redo");
        m.model.undo().unwrap();
        assert_eq!(m.model.to_bytes(), before, "{case}: wrong undo");
    }
}

#[test]
fn successful_deltas_and_their_history_match_the_unmodified_engine() {
    for case in CASES {
        for delta in ["-1", "1", "0"] {
            let (mut guarded, range) = fixture(case, 10);
            let mut upstream = Model::new("reference", "en", "UTC", "en").unwrap();
            upstream.model = UserModel::from_bytes(&guarded.model.to_bytes(), "en").unwrap();
            guarded.model.flush_send_queue();
            let observed_styles = |model: &UserModel| {
                [(1, 1), (1, 2), (2, 1), (LAST_ROW, 1), (1, LAST_COLUMN)].map(|(r, c)| {
                    serde_json::to_value(model.get_cell_style(0, r, c).unwrap()).unwrap()
                })
            };
            let before = observed_styles(&guarded.model);
            dispatch(&mut guarded, &json!({"op":"updateRangeStyle","range":range,"style_path":"font.size_delta","value":delta})).unwrap();
            upstream
                .model
                .update_range_style(&range, "font.size_delta", delta)
                .unwrap();
            assert_eq!(
                guarded.model.to_bytes(),
                upstream.model.to_bytes(),
                "{case}: {delta}"
            );
            assert_eq!(
                guarded.model.flush_send_queue(),
                upstream.model.flush_send_queue()
            );
            guarded.model.undo().unwrap();
            upstream.model.undo().unwrap();
            // Undo restores the styles, but the engine retains interned style
            // records. Full serialized bytes need not equal the pre-edit file.
            assert_eq!(observed_styles(&guarded.model), before, "{case}: undo");
            assert_eq!(guarded.model.to_bytes(), upstream.model.to_bytes());
            guarded.model.redo().unwrap();
            upstream.model.redo().unwrap();
            assert_eq!(
                guarded.model.to_bytes(),
                upstream.model.to_bytes(),
                "{case}: redo"
            );
        }
    }
}

#[test]
fn canonical_and_compatibility_names_share_the_guard() {
    for op in ["updateRangeStyle", "update_range_style", "style"] {
        let (mut m, range) = fixture("last-cell", 1);
        let before = m.model.to_bytes();
        let mut request =
            json!({"op":op,"range":range,"style_path":"font.size_delta","value":"-1"});
        if op == "style" {
            request = json!({"op":op,"area":range,"path":"font.size_delta","value":"-1"});
        }
        assert!(dispatch(&mut m, &request).is_err());
        assert_eq!(m.model.to_bytes(), before, "{op}");
    }
}

#[test]
fn invalid_delta_and_overflow_are_rejected_before_any_write() {
    for input in ["-3", "2147483647", "-2147483648", "1.5", "abc"] {
        let (mut m, range) = fixture("last-cell", 1);
        let before = m.model.to_bytes();
        assert!(dispatch(&mut m, &json!({"op":"updateRangeStyle","range":range,"style_path":"font.size_delta","value":input})).is_err());
        assert_eq!(m.model.to_bytes(), before, "{input}");
    }
}

#[test]
fn sparse_whole_columns_do_not_require_visiting_empty_cells() {
    let mut m = Model::new("sparse", "en", "UTC", "en").unwrap();
    let range = area(1, 1, 10, LAST_ROW);
    dispatch(
        &mut m,
        &json!({"op":"style","area":range,"path":"font.size_delta","value":"-1"}),
    )
    .unwrap();
    assert!(m
        .model
        .get_model()
        .workbook
        .worksheet(0)
        .unwrap()
        .sheet_data
        .rows()
        .is_empty());
    assert_eq!(m.model.get_cell_style(0, LAST_ROW, 10).unwrap().font.sz, 11);
    m.model.undo().unwrap();
    assert_eq!(m.model.get_cell_style(0, LAST_ROW, 10).unwrap().font.sz, 12);
}
