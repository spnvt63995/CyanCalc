# Library and host boundaries

```text
Host → ui/avalonia → bindings/dotnet → bindings/dotnet/native → IronCalc
```

The Rust engine owns calculation, committed workbook state, selection, scrolling, and Undo/Redo.
Avalonia's `WorkbookState` handles uncommitted input, references, and drag presentation.
C# does not maintain a separate calculation model or workbook Undo stack.

The Rust C ABI carries commands and responses from the upstream WASM API as UTF-8 JSON.
.NET `SafeHandle` and a per-model lock manage lifetime and serialize calls; Rust errors become `IronCalcException`.
File and diff byte arrays are transported as Base64.
See [API compatibility](../../bindings/dotnet/API-COMPATIBILITY.md) for the public contract.

## Editing surfaces and formula references

`WorkbookEditSession` owns the draft, the cell being edited, the active input surface, and the reference insertion position.
Clicking the cell editor or formula bar updates both the editing mode and the input surface; actual focus changes also update the session.
`WorkbookEditing` applies that state to TextBox text, focus, and caret position.
Completing a grid reference selection preserves the active input surface.
Only a selection made outside an edit returns focus to the cell.

Like the upstream Editor's `onChange`, reference insertion uses the selection recorded at the last text change.
Subsequent caret movement or text selection does not change that position.
Pointer reference eligibility is evaluated at the recorded selection end; the reference text is inserted at the selection start while preserving the existing suffix.
Replacing a selection by typing records the new text and position.
The actual TextBox continues to own caret movement, text selection, and IME input.

## Dragging and auto-scroll

`WorksheetPointerController` owns the lifetime of captured selection, reference, and fill gestures.
While the pointer remains outside the canvas, it moves the viewport every 50 ms and resolves the target again through `SheetLayout.HitTestDrag`.
Each axis requests 8–40 DIP of movement according to the distance beyond the edge.
The cell-based viewport and the engine's selection visibility adjustment can make actual movement differ from the requested pixel distance.
The axes move independently; row headers scroll vertically and column headers horizontally.
A single Shift-click extension and row/column resizing do not start the timer.

Auto-scroll stops when the pointer returns inside, is released, or loses capture, and when the document or sheet changes, the view becomes read-only, or the view detaches.
Frozen regions stay fixed. An axis stops when the last cell is fully visible, but movement in the opposite direction can start again.
Reference selection retains the edited cell and draft.
A fill preview does not write values; a normal release commits the fill once.
Even when the selection anchor is offscreen, the transparent underlying TextBox receives keyboard input, including Undo after a fill.

If a host callback replaces the document, the `WorksheetCanvas` viewport adapter discards the old movement request and synchronizes the new document's position.
Auto-scroll for text selection inside an editor is handled separately by `CellEditor` and `WorkbookEditorScrolling`.

## Embedding in an Avalonia host

Reference `ui/avalonia/CyanCalc.Avalonia.csproj`. The runtime is .NET 10 / Avalonia 11.3.20.
Create a model and a `WorkbookView` for each document.

```csharp
using CyanCalc;
using CyanCalc.Controls;

var model = new UserModel("Workbook");
var view = new WorkbookView { Session = model };
view.UiLanguage = "fr-FR"; // Updates open menus, panels, and detached dialogs immediately.
view.DocumentChanged += (_, _) => MarkDocumentDirty();
view.Error += (_, message) => ShowDocumentError(message);
// Place view in the host's Content.
// Save: after view.CommitEdit() succeeds, persist model.ToBytes().
// Close the document: view.Session = null; model.Dispose();
```

The callbacks above are examples for the host to implement.
The host owns file paths, titles, saving, and close confirmation.
It can handle `ExportPngRequested` and save the output of `ExportSelectionToPng(Stream)`.
The control does not depend on a particular Application, a global document, or the host's file-dialog service.
Layout uses the control's Bounds instead of the window size.

Detaching and reattaching the control does not close the document.
The host owns document lifetime and is responsible for disposing the model.
Apply model changes to the UI on the UI thread; the native call lock does not replace the UI dispatcher.
`WorkbookView.UiLanguage` is a convenience property for `WorkbookView.Text.Language`.
`WorkbookShell.Text` uses the same instance.
Change the language on the UI thread. Only displayed strings are refreshed, preserving controls, unfinished input, and selection.
Changing the display language alone does not change the engine's locale or formula language.
The example host's language menu changes the display language and model locale/formula language together, as the upstream app does.

To update an error dialog while it remains open, pass the `LocalizedText` from `LocalizedError` to
`WorkbookShell.ShowLocalizedMessage` or `LocalizedText.Bind`.
The existing `Error` event continues to deliver a string rendered in the current language.
Before applying external diffs or replacing the contents of the same model, the host must call `CancelEdit()` to end the active draft.

`FlushSendQueue` and `ApplyExternalDiffs` expose the upstream APIs.
The reusable control does not consume the outgoing queue.
The desktop app drains and discards it; a host providing collaboration must implement its own transport.

`apps/desktop` is a reference implementation for windows, file dialogs, saving, and closing.
Library consumers do not need to reference or run that project.

## Optional application UI

`WorkbookShell` adds a file bar, left drawer, document title, and menus around `WorkbookView`.
Attach the model through `shell.Workbook.Session` and supply the document list through `SetLibrary`.
The host handles `Requested` events such as new/select/rename/pin/duplicate/delete/import/export.
The control does not choose the library path, HTTP sharing server, or autosave interval.
`ChooseTemplateAsync`, `ChooseImportAsync`, and `ConfirmDeleteAsync` are optional native dialogs.

The example host's `Storage/WorkbookLibrary.cs` uses `.ic` files and small metadata files in place of upstream IndexedDB storage.
It finishes writing a new data file before atomically replacing the metadata, then cleans up the old data.
A metadata parsing or save failure does not reset the library to an empty state.
The host does not connect to a sharing server.
Import and XLSX export use the same local Rust engine without a server round trip.

## Avalonia 11 button accessibility on macOS

CyanCalc uses Avalonia 11.3.20. Embedding hosts also need a compatible Avalonia version.
In Avalonia 11.3.20, the macOS AXPress path can invoke a disabled button and propagate
`ElementNotEnabledException` outside the UI loop.
The native enabled-state check from [Avalonia #21599](https://github.com/AvaloniaUI/Avalonia/pull/21599)
is included from 12.0.5 and is absent from the 11.3.22 source.

[WorkbookButton](../../ui/avalonia/Platform/WorkbookButton.cs) replaces only the accessibility peer of ordinary buttons created by CyanCalc.
Color, icon, preset, and preview buttons use the same peer while retaining their inheritance and style keys.
On macOS with Avalonia major version 11, `IInvokeProvider.Invoke` checks `IsEnabled()` and does nothing when disabled.
The check includes disabled ancestors and `Command.CanExecute`.
Enabled buttons call the standard `ButtonAutomationPeer.Invoke`, preserving Click/Command behavior and error propagation.
Names, roles, IDs, disabled-state presentation, themes, and ordinary pointer and keyboard behavior are unchanged.

This compatibility handling is limited to Invoke on ordinary Button controls.
It does not apply to Avalonia ToggleButton/CheckBox/ComboBox controls, controls inside templates, or controls created separately by the host.
It does not suppress exceptions globally, replace native methods, or modify framework binaries.

### Removal conditions and procedure

1. Upgrade Avalonia in CyanCalc and the embedding host to **12.0.5 or later**.
   Versions 12.0.0–12.0.4 predate this fix; major version 12 alone is insufficient.
2. Replace `new WorkbookButton` with `new Button`. Remove the `OnCreateAutomationPeer` overrides from
   `PanelColorButton`/`PanelIconButton`/`ConditionalPresetTile`/`NamedStylePreviewTile`/`ThemePreviewTile`,
   and remove `Platform/WorkbookButton.cs`. Preserve public API signatures.
3. In an actual macOS app, verify that AX calls on disabled Apply and Undo/Redo controls are ignored without saving.
   Check that enabled Apply, reopening the saved values, Undo/Redo, ordinary clicks, and Space activation still work.
   Compare disabled-button behavior with the upstream web app as well.
4. Exercise the native AX path. Calling the managed `ButtonAutomationPeer.Invoke` directly can still throw in Avalonia 12,
   so a headless peer check alone cannot establish that the native fix works.
