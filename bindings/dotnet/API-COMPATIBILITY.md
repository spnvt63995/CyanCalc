# CyanCalc .NET API conventions

The binding contract is the pinned **v0.9.0 WASM `Model`**, not an independently designed
desktop API. [The original wrapper](../../bindings/wasm/src/lib.rs) is the source for all
127 model exports (including its constructor and factories) and 8 module utilities.
`python3 tools/cyancalc/sync-wasm-binding.py` generates the native wrapper, JSON dispatch and C#
PascalCase methods. `--check` verifies their exact generated contents. Update the pin and
rerun the generator instead of editing generated files or copying new engine logic into
the desktop protocol. Function bodies, parameter order and engine errors stay upstream.

- `sheet` is a **zero-based index**, not the worksheet's persistent `sheet_id`.
- `row` and `column` are **one-based**. A1 is `(0, 1, 1)` on the first sheet.
- Range methods take `CellArea(sheet, row, column, width, height)` wherever WASM takes `Area`.
  `UpdateRangeStyle(new(0, 1, 1, 1, 2), ...)` affects A1:A2. `RangeClear*` and
  `SetSelectedRange` retain the WASM inclusive start/end parameters. Existing Python-style
  overloads in `UserModel.Api.cs` only convert endpoints to an area and forward the call.
- `UserModel` automatically recalculates changes and owns Rust Undo/Redo history.
  `PauseEvaluation`, `ResumeEvaluation`, and `Evaluate` retain upstream behavior.
- Binary collaboration diffs remain available through `FlushSendQueue` / `ApplyExternalDiffs`.
  The local Desktop host discards its own outgoing queue because it has no collaboration transport.
  The reusable control does not consume a host's queue.
  A host attaching that transport to a `WorkbookView` must call `view.CancelEdit()` **before**
  `model.ApplyExternalDiffs(diffs)`. One diff can delete and recreate a worksheet with the same
  `sheet_id`; its final `Changed` snapshot cannot identify the deleted editing owner.
  To replace an entire document, load it into a new `UserModel` and then assign `view.Session`.
  Do not call `LoadBytes` on a model while its attached view has an active edit. The host owns
  both sessions and disposes the old one after it is no longer in use.
- Complex rule/style payloads use `JsonElement` with upstream JSON field names. They can retain
  theme colors and fields absent from the renderer's convenience DTOs.
- `GetCellStyle` returns the WASM **extended** object (`style`, conditional icon/data bar/rating),
  retaining raw theme color references. Access `.GetProperty("style")` for the inner style.
  `GetExtendedCellStyle` is a separate Avalonia convenience which resolves colors for painting.
- Native engine errors become `IronCalcException`; .NET I/O errors retain their standard types.
- Dispose each `UserModel` after its document closes. Detaching a `WorkbookView` does not dispose it.

```csharp
using CyanCalc;

using var model = new UserModel("my-workbook");
model.SetUserInput(0, 1, 1, "150");
model.SetUserInput(0, 2, 1, "=A1*2");
Console.WriteLine(model.GetFormattedCellValue(0, 2, 1)); // 300
model.UpdateRangeStyle(new(0, 1, 1, 1, 2), "font.b", "true");
model.InsertRows(0, 1, 1);
Console.WriteLine(model.GetCellContent(0, 3, 1)); // =A2*2
model.Undo();
```

`SaveToICalc` and `SaveToXlsx` refuse to overwrite existing files, matching the upstream file
helpers. The Desktop application's Save command separately offers an explicit overwrite workflow.

This exposes the complete pinned WASM function surface as `UserModel` and `ReferenceUtils`.
TypeScript payload interfaces and enum constants use their original JSON fields/values;
this is not a separate CLR type declaration for every TypeScript type.
It does not claim to implement Python's separate raw `Model` or a published NuGet package.
`Snapshot`, `Cell`, `Cells`, `Dimensions`, and the JSON command transport are additional host/view
helpers, not upstream API names. `native/src/protocol.rs` aggregates view data;
`native/src/legacy.rs` translates old view command names to the generated WASM dispatch.
The native engine is pinned to v0.9.0 commit `297553dc3a37ec6b0f4821055220e3d26c4ac6a1`.
`from_icalc` / `save_to_icalc` use the explicit C# spellings `FromICalc` / `SaveToICalc`.
For the additional JSON transport, use `Execute` for mutations so revision and change events
are raised; `Query` is a low-level escape hatch and does not notify attached views.

The transport differences are C ABI/SafeHandle lifetime management, serialized calls,
UTF-8 JSON instead of JS values, base64 inside JSON for Rust byte vectors, and native
panic containment. Public binary methods return `byte[]`; optional values map to null,
JS maps to JSON objects, and `[sheet,row,column]` to a named CLR tuple. These mappings do
not add workbook rules. `Identity`, `Revision`, and `Changed` support view ownership and
notifications; Rust continues to own workbook contents, evaluation and Undo/Redo.

The binding adds no cell-count or payload quota. Worksheet bounds and validation
come from the engine. Native pointer/null checks and Rust's `isize::MAX` slice-size
requirement protect the C ABI's memory boundary.

`GetCellValue(sheet, row, column)` and `GetCellValueByRef(cellRef)` return
`null`, `string`, `double`, or `bool` from the pinned engine. An error cell
returns its localized error string. `GetCellFormula(sheet, row, column)` returns
the localized formula with the leading `=`, or `null` when there is no formula.
The reference overload uses the engine's parser (for example, `Sheet1!A1`);
invalid references raise `IronCalcException`. These three calls only read model
state; they do not change `Revision`, raise `Changed`, or add Undo history.
Non-finite floating point numbers cannot be represented by the JSON C ABI and
raise an engine error at that boundary.

```csharp
model.SetUserInput(0, 1, 1, "12.5");
model.SetUserInput(0, 2, 1, "=A1*2");
double number = (double)model.GetCellValueByRef("Sheet1!A2")!; // 25
string? formula = model.GetCellFormula(0, 2, 1);              // =A1*2
```

The pinned `UserModel` hyperlink methods are `GetCellLink`, `SetCellLink`,
`DeleteCellLink`, and `GetLinks`. Link payloads use `JsonElement` and the upstream
tagged shape: `{"type":"External","target":"...","tooltip":null}` or
`{"type":"Internal","location":"...","tooltip":null}`. The optional
`SetCellLink` label becomes cell content; it is not part of the link metadata.
`GetCellLink` returns `null` when there is no editable worksheet link. `GetLinks`
returns sorted, flattened entries with `row`, `column`, and `dynamic`; formula
`HYPERLINK` entries are dynamic and only appear in this list. A worksheet link
at the same cell takes precedence. Deleting a worksheet link keeps content and
style. The Rust `UserModel` groups a link plus label edit into one Undo step;
an absent delete or identical set adds no native history entry. Reads do not
change .NET `Revision`, emit `Changed`, or mutate workbook bytes.

```csharp
using System.Text.Json;

var link = JsonSerializer.SerializeToElement(new
{
    type = "External", target = "https://example.com", tooltip = (string?)null
});
model.SetCellLink(0, 2, 2, link, "Example");
JsonElement? editable = model.GetCellLink(0, 2, 2);
JsonElement[] all = model.GetLinks(0);
model.DeleteCellLink(0, 2, 2);
```

The pinned WASM module-level reference helpers are exposed as static .NET
`ReferenceUtils.ColumnNameFromNumber(int)`, `ColumnNumberFromName(string)`, and
`QuoteName(string)`. They call the same pinned Rust engine functions through a
model-independent C ABI. Columns are one-based and limited to A through XFD.
`ColumnNumberFromName` requires uppercase ASCII letters only; invalid input
raises `IronCalcException` with the WASM message `Invalid column name`. `QuoteName` leaves
ordinary names alone, quotes names with spaces or names that look like cell
references, and doubles apostrophes inside quoted names. It formats a name for a
formula reference; it does not validate worksheet naming rules. These helpers
neither require a `UserModel` nor alter any workbook revision, event, or history.
The same class also exposes `GetTokens`, `GetSupportedLocales`, `GetAllTimezones`,
`GetThemeList`, and `HexWithTintToRgb`. Supported-locale order is unspecified upstream
(the engine collects HashMap keys); the wrapper preserves its returned order.

`CellAddress.ColumnName` is an address-format helper with a different
out-of-range exception (`ArgumentOutOfRangeException`). `CellAddress.TryParse`
parses a **full cell address**, accepts lowercase letters, and returns `false`
for invalid text; it is not the upstream column-name parser.

```csharp
ReferenceUtils.ColumnNameFromNumber(27);   // AA
ReferenceUtils.ColumnNumberFromName("XFD"); // 16384
ReferenceUtils.QuoteName("O'Brien");       // 'O''Brien'
```

References:
- [Pinned WASM binding implementation](../../bindings/wasm/src/lib.rs)
- [Generated native counterpart](native/src/upstream_api.rs)
- [Generated C# counterpart](UserModel.Wasm.g.cs)
- [Pinned Python API](https://github.com/ironcalc/IronCalc/blob/297553dc3a37ec6b0f4821055220e3d26c4ac6a1/bindings/python/python/ironcalc/_ironcalc.pyi)
- [Pinned Python binding implementation](https://github.com/ironcalc/IronCalc/blob/297553dc3a37ec6b0f4821055220e3d26c4ac6a1/bindings/python/src/user_model.rs)
- [Pinned module helper implementation](https://github.com/ironcalc/IronCalc/blob/297553dc3a37ec6b0f4821055220e3d26c4ac6a1/bindings/python/src/lib.rs)
- [Pinned reference utility rules](https://github.com/ironcalc/IronCalc/blob/297553dc3a37ec6b0f4821055220e3d26c4ac6a1/base/src/expressions/utils/mod.rs)

Merged-cell methods follow the pinned WASM: `MergeCells`, `MergeCellsCenter`,
`MergeCellsAcross`, `MergeCellsDown`, `UnmergeCells`, `GetMergedCells`.
Mutations take `CellArea`, with inclusive endpoint overloads retained for existing callers.
`GetMergedCells` returns
typed `MergedCell` rectangles. Conflicting contents/merges/array formulas are
rejected by the original engine; each successful merge command is one Undo step.
For example, `model.MergeCellsCenter(0, 1, 1, 2, 3)` merges A1:C2.

`PasteFromClipboard` keeps upstream semantics: merge geometry comes from the
current source sheet, so its raw payload is not a self-contained cross-document
merge clipboard. The control passes the upstream payload without a separate stale/ownership policy.

`FromBytes`/`LoadBytes`/`FromICalc` read the pinned engine's original schema directly,
and all writes use that schema. The binding has no automatic migration layer for older `.ic` schemas.
`.ic` files and binary diffs are not version-independent interchange formats;
diff peers must share a pin.
See the [merge implementation](https://github.com/ironcalc/IronCalc/blob/297553dc3a37ec6b0f4821055220e3d26c4ac6a1/base/src/user_model/merged_cells.rs).

`FromBytes` and the byte-based `FromXlsx(bytes, name, locale, timezone, languageId)`
follow the WASM factories directly. `LoadBytes` and `LoadXlsx` replace the handle's model
through those same factories. No load path adds `Evaluate()` or changes the language after
loading. Cached formula results survive opening exactly as upstream; call `Evaluate()`
explicitly when desired. Failed imports leave the previous model intact.

Run the public source-generation and native interop checks from the repository root:

```sh
python3 tools/cyancalc/sync-wasm-binding.py --check
dotnet build CyanCalc.slnx -p:RestoreLockedMode=true --nologo
dotnet run --project tests/CyanCalc.CoreChecks
cargo test --locked --manifest-path bindings/dotnet/native/Cargo.toml
```

The native timezone list comes from chrono-tz and the browser list from Intl; the wrapper does not filter either list.
The source-generation and interop checks do not exercise native GUI input or platform dialogs.

File compatibility follows the pinned IronCalc engine and is not lossless:

- XLSX `patternType="none"` with indexed foreground/background metadata can import
  as a black fill ([upstream #1500](https://github.com/ironcalc/IronCalc/issues/1500)).
  That fill survives `.ic` serialization but can disappear after XLSX export/reopen.
- Unknown OOXML parts, including unreferenced `customXml` parts, are not preserved.
  Keep the original file when those parts matter.
- XLSX export omits sheet tab colors; `.ic` preserves them.
- Neither `.ic` nor XLSX persists Undo history.
- `CUBEVALUE` is unsupported.

The wrapper uses IronCalc for calculation and file conversion.
