# Editor state and input boundaries

CyanCalc displays one `WorkbookEditSession` through two native input controls.
The baseline is IronCalc v0.9.0 (`297553dc3a37ec6b0f4821055220e3d26c4ac6a1`).

## Upstream behavior and native adaptation

The implementation follows upstream features, state semantics, and editing flows, adapted to Avalonia and OS input and presentation.
The edit session owns the draft and references.
Native adapters handle focus, IME, platform shortcuts, and pointer capture.

## Mapping to upstream

Upstream pointer, keyboard, and formula-bar handling is also split across components.
`EditingCell` in `webapp/IronCalc/src/components/workbookState.ts` shares editing state with
`Editor/Editor.tsx`, `Editor/useKeyDown.ts`, `FormulaBar/FormulaBar.tsx`, and related components.
The native implementation receives events at the relevant controls but keeps a single source of draft and reference-selection state.

| Responsibility | Owner |
| --- | --- |
| Start and end the active edit; expose `Editing`/`References` | `ui/avalonia/WorkbookState.cs` |
| Draft text, accept/edit mode, active input surface, and caret recorded at the last text input | `ui/avalonia/Editor/WorkbookEditSession.cs` |
| Reference prefix/suffix, sheet, anchor and range endpoints; shared pointer/arrow reference updates; reference context during sheet switching | `WorkbookEditSession` |
| Edited cell/sheet identity, deletion detection, and measured editing size | `EditingCell` |
| Receive Avalonia keys, check IME and active input, prioritize native history, references, and completion | `ui/avalonia/Input/WorkbookInput.cs` |
| Classify keys, modifiers, and editing context as app actions, TextBox delegation, or consumed input | `ui/avalonia/Input/WorkbookKeyBindings.cs` |
| Execute classified actions; connect sheet navigation, edit commit/cancel, formatting, and engine history | `ui/avalonia/Input/WorkbookCommands.cs` |
| Edit lifetime, actual focus, TextBox synchronization and history, engine commit, and selection restoration | `ui/avalonia/Editor/WorkbookEditing.cs` |
| Actual caret, text selection, IME, and text presentation | `ui/avalonia/Editor/CellEditor.cs` and Avalonia `TextBox` |
| Visible-cell, merge, and overflow caches; palette, scrolling, and Avalonia event integration | `ui/avalonia/WorksheetCanvas/WorksheetCanvas.cs` |
| Click, double-click, selection-drag, resize, and fill gesture state | `ui/avalonia/WorksheetCanvas/WorksheetPointerController.cs` |
| Read-only rendering of cells, grid, selection, references, and headers | `WorksheetRenderState` and `WorksheetRenderer` in `ui/avalonia/WorksheetCanvas/WorksheetRenderer.cs` |
| Row/column coordinates, frozen regions, cell/range rectangles, and hit testing | `ui/avalonia/Worksheet/SheetLayout.cs` |
| Editor layout/presentation and execution of pointer-requested commands | `WorkbookView` and the edit session |

## Worksheet rendering and pointer handling

`WorksheetCanvas.Refresh` reads visible cells, merges, and overflow data from the engine, then updates `SheetLayout` and caches using their existing keys.
`Render` packages this data, the current selection, references, editing state, pointer-selection state, and resize guide into `WorksheetRenderState`.
This state contains values and read-only cache views for synchronous UI-thread rendering; it does not own separate document state.

`WorksheetRenderer` uses only `DrawingContext` and the supplied display data.
It receives no engine, `WorkbookView`, or input controls, so rendering cannot change the document, focus, selection, or Undo history.
It owns drawing order and pane clipping for cells, conditional formatting, merges, overflowing text, selection, references, and headers.

`WorksheetCanvas` also owns `WorksheetRenderResources`.
It reuses cell/header `TextLayout` objects and grid pens.
A layout is reused only when text, font, color, wrapping mode, and wrapping width match; position, alignment, and clipping are calculated on every draw.
The least-recently-used layout cache holds at most 1,024 entries and 65,536 characters across text, font-name, and color strings.
An entry exceeding 4,096 characters is disposed immediately after drawing; evicted layouts are also disposed immediately.
Resources are cleared on document replacement or removal, sheet/theme/display-scale changes, and canvas detachment.
Updating the rendering resource cache does not write document, selection, or editing state.

`WorksheetPointerController` tracks the captured pointer, the document and sheet where the gesture began, and the cell associated with a completed click.
Move and release events apply only to that pointer and owning document/sheet.
Document replacement, read-only transitions, capture loss, and detachment cancel gesture state, fill previews, and reference drags and release capture.
Cancellation does not move focus, and a delayed blur from before detachment cannot commit the draft of a reattached view.
Hit testing uses the canvas's shared `SheetLayout`; selection, reference updates, resizing, and filling call the existing `WorkbookView` methods.
Fill-handle drawing and hit testing use the same canvas rectangle.
A normal release cleans up capture and selection state while retaining the completed click needed to start double-click editing.

When attaching a document, `WorkbookView` sends the already-arranged worksheet size to the new model.
Initial layout and subsequent resizing use the same path, excluding row and column headers.
An attachment without a size waits for layout.
Viewport-size synchronization does not create document history entries.

The canvas and workbook adapters manage scroll position, extent, accumulated pixel offsets, and merged-cell editor layout.
The rendering boundary receives no scroll commands or model-writing callbacks.

## Input and edit lifetime

1. `BeginEdit` identifies the edited cell, and `WorkbookState.BeginEdit` creates the edit session.
2. Actual `TextProperty` changes go to `InputText`. `RenderEditText` displays the session draft in both input controls without treating that refresh as new input.
3. Pointer and arrow reference selection use the same `TryStartReference`/`UpdateReference` path in the session. The adapter applies the resulting text and caret position and returns focus.
4. Commit reads the session's `Text`, not the active TextBox string. Commit, cancel, and document replacement use `EndEditSession` to release session subscriptions, editing presentation, and completion popups. `RefreshFormulaTextColors` then updates the current cell's formula colors.
5. Commit closes editing state before writing to the engine. Cancel restores the edited cell's selection according to the existing rules. The host retains its policy for committing before document switches.

### Unchanged edits and workbook history

An ordinary cell commit compares the final session draft with the original input captured when editing began.
If the text is identical, editing still ends and the requested navigation or panel transition still occurs.
The commit submits no cell-input write: workbook Undo/Redo, the model revision, and document-change notifications remain unchanged.
This also applies when the user changes the draft and then restores the original text before committing.

| Final draft or command | Workbook history behavior |
| --- | --- |
| Original text, including an edited-then-restored draft | End editing without adding an Undo entry or clearing Redo |
| Different text, even if it calculates to the same value | Submit the input to the engine as an actual edit |
| Explicit array-formula commit | Apply the requested array range, even if the formula text is unchanged |
| Cancel, a read-only transition, or replacement of the view's session | Discard the unfinished edit without a text commit |

Equality is exact and case-sensitive; whitespace, formula spelling, and other input differences are not normalized by this policy.
For example, changing `=1+1` to `=2` is an edit even though both calculate to 2.
Enter, Tab, their reverse-navigation variants, blur, and panel entry all use the same ordinary commit path.
Completion acceptance and IME handling retain their existing input boundaries.

The policy belongs to the editor adapter. Direct binding calls, formatting, clipboard operations, and structural commands retain the engine's history semantics.
A host that wants to save an active edit before replacing the document calls `CommitEdit` first.

### Text history while editing

During editing, the adapter advances both TextBox native Undo/Redo histories together.
Outside an edit, the engine handles Undo/Redo.
Display synchronization pauses while history moves; the active input's result is then applied to the session once.
An Undo result is not recorded as a new text change in the other input, so switching inputs cannot restore already-undone text.

Avalonia also records programmatic `Text` assignments in Undo history.
Both histories are cleared when refreshing the idle display, loading existing content for a new edit, and ending an edit.
If typing the first character starts an edit, its native Undo entry is retained, and the other input receives the same baseline.
Displaying the same draft again updates only inputs whose values differ.
A focus change alone does not clear the active edit's history.

On macOS, `CellEditor` and `MacTextComposition` finish composition at the input-control boundary.
Avalonia.Native 11.3.20 leaves the input method's `Reset` empty, so `NSView.unmarkText` on the current window ends the AppKit conversion session before focus is lost.
When committing, the captured preedit is delivered once through the original input's `TextInput`, using TextBox insertion, selection, and Undo handling.
`CommitEdit` reads the draft after this completes.
For cancellation or document replacement, `EndEditSession` discards composition before clearing editing state.
The native view is obtained from the current TopLevel on each operation and is not retained.
This handling is limited to actual macOS input controls; other platforms keep the standard TextBox IME path.

For macOS `Option+Return`, the adapter verifies the actual current window and key event, then sends it to the current AppKit input context before TextBox newline handling.
If the input method consumes it, the event is not sent again; otherwise it continues through the standard TextBox path.
If an AppKit command callback re-enters KeyDown, forwarding is suppressed and TextBox handles that nested event.
Input-source names and Hanja candidates do not belong in the workbook command table.

Native focus and Dispatcher operations depend on controls and remain outside the session.
Line/document boundary navigation uses platform shortcut settings and TextBox line measurement.
The shared `CellEditor` mapping connects macOS `Cmd+↑/↓` and `Shift+Cmd+↑/↓` to document-start/end movement and selection.
This applies to ordinary text editing.
The existing adapter first handles accept-mode arrow commit/reference navigation and worksheet navigation outside an edit.
Before a boundary movement that does not extend selection, `CellEditor` collapses the selection at the visible caret.
This prevents Avalonia from returning to an old selection anchor when the movement leaves the caret unchanged.
Shift selection and input during IME composition bypass this adjustment.

A delayed blur verifies that its session and editing-mode owner are still current.
Completion updates also run only if the session that scheduled them remains current.
Callbacks from an old document cannot commit the next edit or reopen its completion popup.

When text selection in a long cell is dragged beyond the sheet edge, `WorkbookEditorScrolling` moves the sheet while `CellEditor` retains pointer capture.
After movement, TextBox character/word selection is recalculated at the same pointer position without duplicating the draft or selection anchor.
The timer is cleared on button release, capture loss, focus loss, and edit completion.
Editor position and size remain valid even when the edited cell leaves the visible row/column cache.
The formula bar uses its own TextBox scrolling.

## Shortcut responsibilities and baseline

The upstream [shortcut documentation](../docs.ironcalc.com/src/features/keyboard-shortcuts.md) describes worksheet navigation, selection, and formatting.
Its `Cmd/Ctrl+Arrow` shortcuts mean **navigation to worksheet data boundaries**.
It is not a complete binding table for character, word, line, and document movement inside text.
Always distinguish focus and editing mode.

| Responsibility | Upstream IronCalc | CyanCalc |
| --- | --- | --- |
| Worksheet navigation, range selection, formatting, and engine Undo/Redo outside editing | Modifier/key branches in `Workbook/useKeyboardNavigation.ts` and Workbook callbacks | Non-editing classification in `WorkbookKeyBindings` and `WorkbookCommands` |
| Commit, cancel, F4, and accept-mode arrow reference/commit handling | `Editor/useKeyDown.ts` and shared editing state | `WorkbookInput` → `WorkbookKeyBindings` → `WorkbookCommands`, `WorkbookEditSession` |
| Arrows, Enter, Tab, and Escape in function completion | `handleHelperKeyDown` in `Editor.tsx` | `FormulaHelper.HandleKey` |
| Ordinary text movement, selection, deletion, clipboard, and IME | HTML `textarea` in the shared `Editor.tsx`, with browser/OS editing | Shared `CellEditor`, Avalonia `TextBox`, and `PlatformSettings.HotkeyConfiguration` |
| Undo/Redo while editing | Browser editing history for the textarea | `WorkbookEditing` advances both TextBox native histories together |

The upstream cell editor and formula bar share `Editor`; the two native inputs share `CellEditor`.
In upstream edit mode, arrow keys reach the default textarea handler.
macOS `Cmd+↑/↓` document navigation therefore comes from browser editing behavior, not a separate upstream shortcut implementation.
Compare Home/End mode changes, PageUp/PageDown, completion, and reference input separately because the upstream hook handles them before ordinary text movement.

The native implementation does not overwrite application-wide shortcut settings.
TextBox handles default editing; `CellEditor` only connects the required macOS document-navigation keys to existing platform start/end gestures.
Keys already handled by the outer adapter and IME composition bypass this mapping.

Verify groups of behavior rather than isolated key names:

- Movement in both directions by character, word, line, and document, with Shift selection, existing anchors, selection collapse, and repeated movement.
- Boundaries between cell/formula-bar input, edit/accept/non-editing modes, completion, reference input, and IME.
- Separation of text Undo/Redo, deletion, and clipboard operations from worksheet engine commands.

Tests that execute the upstream hook compare application command branches but do not exercise the browser's default editing.
Text-control checks must also inspect the actual platform shortcut settings.
Do not pre-register a shortcut under test in a way that hides a missing mapping.
Compare default editing through actual key delivery, caret position, and selection ranges in the upstream browser and native app.

## Key dispatch order and execution boundaries

`WorkbookInput.EditorKeyDown` is the common entry point for both inputs.
It normalizes Cmd/Ctrl modifiers to the workbook's `Primary` modifier, but identifies text Undo/Redo using the actual platform gesture settings.

| Order | Condition and handling |
| --- | --- |
| 1 | During IME composition, skip workbook handling. |
| 2 | Set the current input as the active surface of the existing edit session. |
| 3 | During editing, advance both TextBox histories for a platform Undo/Redo gesture. |
| 4 | If an accept-mode arrow can select a reference, update the reference. |
| 5 | If the open function-completion popup handles the key, do not continue to commit/navigation policy. |
| 6 | The pure classifier chooses an app action, default TextBox handling, or consumption for the editing/non-editing context. |
| 7 | Execute app actions and mark handled events. Forward the rest to TextBox. |

The classifier does not reference Avalonia, TextBox, or the engine.
It reads only `WorkbookKey`, modifiers, and `WorkbookInputContext`, and returns `WorkbookKeyDecision`.
It does not implement ordinary text caret movement, selection, deletion, or clipboard operations.

`WorkbookCommands` connects to existing `Command`, `Undo`/`Redo`, `ToggleFont`, and edit-lifetime methods.
The toolbar uses the same action methods.
Clipboard commands classify copy/cut/paste intent; actual platform clipboard access and asynchronous work remain in `CopyAsync`/`PasteAsync`.
Focus, scrolling, and reference-text updates also remain in their adapters.

Non-editing classification preserves the upstream modifier-branch order.
This includes consuming Ctrl+A, the Ctrl+Shift+Z exception to the read-only command guard, and starting input after Shift+Space selects a row.
Movement after commit differs between Enter/Tab and accept-mode arrows.
Accept-mode arrows return to the draft's owning sheet; with Shift, they commit without cell navigation.

`tests/CyanCalc.CoreChecks/WorkbookInputChecks.cs` is the public entry point for classifier checks.
Run it with `--input-policy` without a platform UI.
It checks the boundary between app commands and native editing; actual control/engine comparisons and GUI verification are separate.

In accept mode, Home/End first runs `EnterTextEditMode`, then forwards the same key to TextBox.
Actual boundary movement, including Shift selection, follows platform rules.
Subsequent arrows move through text instead of committing the draft or selecting a reference.
In edit mode, the key reaches default editing without another state change.
The `NativeAfter` decision represents this sequence of updating app state before continuing native handling.

During editing, PageUp/PageDown are delegated to TextBox.
The draft, editing mode, and sheet selection remain unchanged, while the input control may scroll.
This native behavior differs from the upstream hook, which consumes page keys, and is separate from worksheet page navigation outside editing.

In function completion and argument help, Escape first closes only the help UI.
Caret movement in the same input does not reopen it.
A draft-text change resets dismissal for both inputs, and ending the edit session clears both dismissal states.
The selected completion row resets to the first item when text, caret position, or active input changes; a new edit never inherits the previous selection.
Completion updates apply only to the input with actual focus in the current session.
Focus changes also schedule an update, refreshing that input's state and popup position even when returning to the same caret position.

## Editing state semantics

The edit session keeps these concepts distinct:

- **Formula text colors and grid reference highlights.** Colors in the idle formula bar and in both editors immediately after F2 are calculated from the displayed formula. Refreshing those colors does not create an active session reference. Grid highlights change through input and reference selection. Read-only views do not display reference text colors.
- **The caret recorded at input time and the visible caret.** Upstream sheet-switch decisions use the caret from the last text input. Caret movement alone or F4 does not update it.
- **The selected reference and the original draft.** Sheet-switch context uses the prefix/suffix from before reference insertion, rather than reparsing the entire displayed formula.
- **Accept mode and edit mode.** Preserve the established transitions for double-click, F2, clicking inside an editor, and empty text.
- **The draft's owning sheet and the viewed sheet.** Selecting a reference on another sheet does not change the edited cell. The cell editor and its separate color layer appear only on the owning sheet.
- **The reference anchor and range boundaries.** A single reference uses the merged-cell anchor; dragging and Shift extension include every intersecting merged region. The anchor remains fixed even if the range expands beyond it. The next arrow endpoint depends on the anchor's relationship to both range endpoints. State retains drag direction while the formula displays an ordered range.
- **The reference sheet.** A tab switch changes only the viewed sheet. Arrows retain the reference's sheet, and the first keyboard reference starts on the draft's owning sheet. A new grid-pointer reference uses the viewed sheet. Merged-cell resolution uses that reference sheet.
- TextBox caret/selection offsets use UTF-16; the Rust reference API uses Unicode scalar offsets. `EditorTextOffsets` converts between them.

## Change and verification boundaries

Keep shortcut policy in `WorkbookKeyBindings` and action execution in `WorkbookCommands`.
Draft/reference semantics belong to `WorkbookEditSession`.
OS key input, IME, and focus belong to `WorkbookInput`, `WorkbookEditing`, and `CellEditor`.
Colors, borders, and layout belong to the presentation layer.
Do not duplicate draft variables or reference policy for individual UI events.
Ordinary worksheet selection, clipboard operations, and host document management retain their existing owners.

For structural changes, run regressions covering the affected sheet switching, arrows, edit lifetime, gestures, read-only behavior, toolbar behavior, and reference presentation.
Separately compare the actual upstream web app and macOS app for input-surface switching, sheet switching, typing again after cancellation, committing references to another sheet, and document replacement.
Headless checks do not replace actual IME or pointer input.
