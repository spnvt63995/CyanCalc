# Upstream web and CyanCalc implementation map

The baseline is IronCalc v0.9.0 (`297553dc3a37ec6b0f4821055220e3d26c4ac6a1`).
Upstream sources retain their original directories; the .NET binding and Avalonia implementation use separate directories for each responsibility.

## Using upstream references

Start with the feature's user documentation to establish expected behavior, then read its tests, Storybook examples, and implementation together.
When changing behavior, compare the pinned upstream web app with the actual native app using the same input, document, and theme.
Local upstream references use the revision above.
Online documentation may describe another version; check versions before interpreting a difference.

| Area | Documentation to read first | Upstream examples and implementation |
| --- | --- | --- |
| Cell/formula input, keyboard, and pointer | [Shortcuts](../docs.ironcalc.com/src/features/keyboard-shortcuts.md), [input modes](../../webapp/IronCalc/src/components/README.md) | [Editor story](../../webapp/IronCalc/src/components/Editor/Editor.stories.tsx), [reference-context tests](../../webapp/IronCalc/tests/isInReferenceMode.test.ts), [Workbook story](../../webapp/IronCalc/src/components/Workbook/Workbook.stories.tsx) |
| Named ranges | [Name Manager](../docs.ironcalc.com/src/features/name-manager.md) | [NamedRanges](../../webapp/IronCalc/src/components/RightDrawer/NamedRanges), Workbook story |
| Styles and conditional formatting | [Cell styles](../docs.ironcalc.com/src/features/cell-styles.md), [conditional formatting](../docs.ironcalc.com/src/features/conditional-formatting.md) | [Engine conditional-formatting tests](../../base/src/test/conditional_formatting), [RightDrawer](../../webapp/IronCalc/src/components/RightDrawer) |
| Component appearance and themes | [Themes](../docs.ironcalc.com/src/features/themes.md), [theme implementation](../../webapp/IronCalc/src/theme/README.md) | [Component stories](../../webapp/IronCalc/src/components), [Storybook configuration](../../webapp/IronCalc/.storybook/main.ts) |
| Cell rendering | [Visual regression guide](../../webapp/IronCalc/tests/worksheetCanvas/README.md) | [Rendering cases](../../webapp/IronCalc/tests/worksheetCanvas/renderSheet.test.ts), [reference images](../../webapp/IronCalc/tests/worksheetCanvas/screenshots) |
| Engine and bindings | [Programming guide](../docs.ironcalc.com/src/programming/about.md), [API documentation structure](../ironcalc.dev/README.md) | [Documentation generator](../ironcalc.dev/build-docs.sh), [CyanCalc API compatibility](../../bindings/dotnet/API-COMPATIBILITY.md) |

Commands and local URLs are in the development guide's [web comparison](README.md#web-comparison) section.
Use the Editor story for isolated input behavior and the Workbook story for sheet, selection, and reference transitions.
Use the full app for document switching and the left drawer.
Storybook wrappers can differ from the full app, so also inspect the relevant components' event wiring.

The upstream input-mode description is brief, not a complete interaction specification.
Visual regression tests compare the cell canvas; they exclude DOM layers such as editors, selection borders, and tooltips.
Do not require macOS rendering to reproduce font antialiasing in Linux reference images exactly.
Distinguish state/utility checks from actual focus, IME, and pointer verification.
See [editor architecture](EDITOR-ARCHITECTURE.md) for native input responsibilities.

## Implementation map

| Feature | Upstream IronCalc | CyanCalc |
| --- | --- | --- |
| Spreadsheet component | [Workbook.tsx](../../webapp/IronCalc/src/components/Workbook/Workbook.tsx) | [WorkbookView.cs](../../ui/avalonia/Workbook/WorkbookView.cs) |
| Full app, file bar, and menus | [App.tsx](../../webapp/app.ironcalc.com/frontend/src/App.tsx), [FileBar.tsx](../../webapp/app.ironcalc.com/frontend/src/components/FileBar.tsx) | [WorkbookShell](../../ui/avalonia/Shell/WorkbookShell.cs), [WorkbookWindow](../../apps/desktop/WorkbookWindow.cs) |
| Left document drawer | [LeftDrawer](../../webapp/app.ironcalc.com/frontend/src/components/LeftDrawer) | [WorkbookShell.Drawer.cs](../../ui/avalonia/Shell/WorkbookShell.Drawer.cs) |
| Library and autosave | [storage.ts](../../webapp/app.ironcalc.com/frontend/src/components/storage.ts) | [WorkbookLibrary.cs](../../apps/desktop/Storage/WorkbookLibrary.cs) |
| Welcome, templates, import, and deletion confirmation | [WelcomeDialog](../../webapp/app.ironcalc.com/frontend/src/components/WelcomeDialog) | [WorkbookShell.Dialogs.cs](../../ui/avalonia/Shell/WorkbookShell.Dialogs.cs) |
| Grid rendering | [worksheetCanvas.ts](../../webapp/IronCalc/src/components/WorksheetCanvas/worksheetCanvas.ts) | [WorksheetCanvas.cs](../../ui/avalonia/WorksheetCanvas/WorksheetCanvas.cs) |
| Toolbar | [Toolbar.tsx](../../webapp/IronCalc/src/components/Toolbar/Toolbar.tsx) | [WorkbookToolbar.cs](../../ui/avalonia/Toolbar/WorkbookToolbar.cs) |
| App and component translations | [App locales](../../webapp/app.ironcalc.com/frontend/src/locale), [component locales](../../webapp/IronCalc/src/locale) | [WorkbookText.cs](../../ui/avalonia/Localization/WorkbookText.cs), [native-only strings](../../ui/avalonia/Localization/Locales) |
| Formula input | [FormulaBar.tsx](../../webapp/IronCalc/src/components/FormulaBar/FormulaBar.tsx) | [CellEditor.cs](../../ui/avalonia/Editor/CellEditor.cs), [WorkbookEditing.cs](../../ui/avalonia/Editor/WorkbookEditing.cs) |
| Keyboard | [useKeyboardNavigation.ts](../../webapp/IronCalc/src/components/Workbook/useKeyboardNavigation.ts) | [WorkbookEditing.cs](../../ui/avalonia/Editor/WorkbookEditing.cs) |
| Sheet tabs | [SheetTabBar.tsx](../../webapp/IronCalc/src/components/SheetTabBar/SheetTabBar.tsx) | [SheetTabBar.cs](../../ui/avalonia/Workbook/SheetTabBar.cs) |
| Copy, cut, and paste | [Workbook.tsx](../../webapp/IronCalc/src/components/Workbook/Workbook.tsx) | [WorkbookClipboard.cs](../../ui/avalonia/Workbook/WorkbookClipboard.cs) |
| Number formatting | [FormatMenu.tsx](../../webapp/IronCalc/src/components/FormatMenu/FormatMenu.tsx) | [WorkbookToolbar.cs](../../ui/avalonia/Toolbar/WorkbookToolbar.cs), [NumberFormatPicker.cs](../../ui/avalonia/Toolbar/NumberFormatPicker.cs) |
| Right panels | [RightDrawer](../../webapp/IronCalc/src/components/RightDrawer) | [WorkbookPanels.cs](../../ui/avalonia/RightDrawer/WorkbookPanels.cs), [DrawerResizeHandle.cs](../../ui/avalonia/RightDrawer/DrawerResizeHandle.cs) |
| Name Manager | [NamedRanges](../../webapp/IronCalc/src/components/RightDrawer/NamedRanges) | [NamedRanges.cs](../../ui/avalonia/RightDrawer/NamedRanges.cs) |
| Conditional formatting | [ConditionalFormatting](../../webapp/IronCalc/src/components/RightDrawer/ConditionalFormatting) | [ConditionalFormatting.cs](../../ui/avalonia/RightDrawer/ConditionalFormatting.cs), [ConditionalRuleEditor.cs](../../ui/avalonia/RightDrawer/ConditionalRuleEditor.cs), [ConditionalRulePresentation.cs](../../ui/avalonia/RightDrawer/ConditionalRulePresentation.cs) |
| Function completion and help | [FormulaHelper.tsx](../../webapp/IronCalc/src/components/FormulaHelper/FormulaHelper.tsx) | [FormulaHelper.cs](../../ui/avalonia/Editor/FormulaHelper.cs) |
| Error messages | [Alert.tsx](../../webapp/IronCalc/src/components/Modal/Alert.tsx), [Workbook.tsx](../../webapp/IronCalc/src/components/Workbook/Workbook.tsx) | [WorkbookAlerts.cs](../../ui/avalonia/Workbook/WorkbookAlerts.cs) |
| Cell styles | [NamedStyles](../../webapp/IronCalc/src/components/RightDrawer/NamedStyles) | [NamedStyles.cs](../../ui/avalonia/RightDrawer/NamedStyles.cs), [StyleEditor.cs](../../ui/avalonia/RightDrawer/StyleEditor.cs) |
| Theme editing and preview | [Themes](../../webapp/IronCalc/src/components/RightDrawer/Themes) | [WorkbookPanels.cs](../../ui/avalonia/RightDrawer/WorkbookPanels.cs), [ThemePreviewTile.cs](../../ui/avalonia/RightDrawer/ThemePreviewTile.cs) |
| Regional settings | [RegionalSettings.tsx](../../webapp/IronCalc/src/components/RightDrawer/RegionalSettings/RegionalSettings.tsx) | [WorkbookPanels.cs](../../ui/avalonia/RightDrawer/WorkbookPanels.cs), [WorkbookLocale.cs](../../ui/avalonia/Localization/WorkbookLocale.cs) |
| Link editing | [LinkDialog.tsx](../../webapp/IronCalc/src/components/LinkDialog/LinkDialog.tsx) | [LinkEditorWindow.cs](../../ui/avalonia/Workbook/LinkEditorWindow.cs) |
| Link, format, deletion, and import dialog appearance | [Modal](../../webapp/IronCalc/src/components/Modal), [UploadFileDialog](../../webapp/app.ironcalc.com/frontend/src/components/UploadFileDialog) | [WorkbookDialog.cs](../../ui/avalonia/Workbook/WorkbookDialog.cs), [WorkbookDialogStyles.axaml](../../ui/avalonia/Workbook/WorkbookDialogStyles.axaml) |
| Engine calls and file loading | [WASM binding](../../bindings/wasm/src/lib.rs) | [Generated native counterpart](../../bindings/dotnet/native/src/upstream_api.rs), [UserModel.Wasm.g.cs](../../bindings/dotnet/UserModel.Wasm.g.cs) |
| Calculation, Undo, and workbook state | [base](../../base), [xlsx](../../xlsx) | [Pinned engine dependency](../../bindings/dotnet/native/Cargo.toml) |

## Platform integration

- C ABI, UTF-8 JSON, SafeHandle, and call locking replace the WASM/JavaScript transport.
- Web canvas and text-editing elements map to Avalonia DrawingContext, TextBox, and IME.
- String offsets are converted at the .NET Rune/UTF-16 boundary.
- Browser events and clipboard access map to Avalonia events, the dispatcher, and the OS clipboard.
- File dialogs, document windows, and save confirmation belong to the `apps/desktop` host.
- Local `.ic`, metadata, and settings files replace IndexedDB/localStorage; C ABI engine calls replace server-side XLSX conversion.

## Application UI scope

The document drawer follows the upstream creation-time groups Pinned/Today/Last 30 Days/Older, search, and Shift range selection.
Duplicating a document retains the current selection and increments duplicate names with `(1)`.
Deleting the selected document switches to the newest document or an empty one.
Language and theme choices are library settings.

The app and spreadsheet component embed the pinned upstream locale JSON directly.
Six language choices—US/GB English, German, Spanish, French, and Italian—share one translation context across toolbars, menus, right panels, and detached native dialogs.
Only titles and labels in open editors change, preserving unfinished formatting, link, and conditional-formatting input.
Strings absent upstream, such as local-storage notices and native accessibility descriptions, use `native.*` keys.
Document, sheet, and custom-style names, formulas, and format codes are not translated.
Upstream function descriptions and default style names remain in their original English.
OS file pickers use the system language.

The desktop app manages local documents and provides no sharing server, Share action, or shared-document links.
It displays local storage and backup guidance in place of upstream server-data deletion warnings.
Exporting an `.ic` copy is an additional native-host menu item.
Welcome, template, import, and deletion-confirmation UI uses separate native modals in place of browser overlays.
Template cards use upstream PNG assets.

## Behavior baseline

The binding is generated from 127 upstream WASM model APIs and 8 utilities.
It uses engine bounds checks without adding cell-count limits and preserves cached calculation results when loading files.
Toolbars, menus, icons, keyboard handling, and formatting follow upstream code; UI colors use the CyanCalc palette.

CyanCalc intentionally omits a workbook history entry when an ordinary cell edit finishes with its original input text.
The pinned web editor submits unchanged input on blur, which adds an Undo step and clears an existing Redo branch.
The native editor preserves that branch so entering and leaving an editor does not discard a redoable change.
Changed input and explicit array-formula commands still reach the engine.
See [unchanged edits and workbook history](EDITOR-ARCHITECTURE.md#unchanged-edits-and-workbook-history) for the scope and commit boundaries.

Font-size commits follow the pinned toolbar: Enter or loss of focus submits a formatting command for the full selected range, even when its active cell already matches.
An already uniform range still receives an Undo step and loses an existing Redo branch, including when the font-size field is left without changing its value.
Escape and invalid input submit no formatting command.
See [formatting commits](EDITOR-ARCHITECTURE.md#formatting-commits) for the distinction from unchanged cell edits.

Relative font-size changes have one native safety difference: if a selected cell
would receive an invalid size, CyanCalc rejects the complete command before any
write. The pinned engine can partially change the range and then return an error
without recording Undo history. The native transport preflight preserves existing
sizes and Undo/Redo on rejection while successful commands still use the original
engine. The generated bindings and pinned engine sources remain unchanged.
See [binding compatibility](../../bindings/dotnet/API-COMPATIBILITY.md) for the guard's scope.

Font-size Tab navigation intentionally retains native toolbar traversal.
The pinned web Workbook normally refocuses itself after the render caused by a font-size commit.
CyanCalc keeps Tab on the next toolbar control and Shift+Tab on the previous one so users can continue formatting with the keyboard.
Enter and Escape in the font-size field return focus to the active sheet cell without moving the selection.
See [font-size focus and keyboard traversal](EDITOR-ARCHITECTURE.md#font-size-focus-and-keyboard-traversal) for returning to the sheet and the scope of Undo/Redo shortcuts.

Conditional formatting follows the upstream four tabs, defaults, presets, range validation, and filtering by full containment of the selected range.
Enter/Tab in completion inserts the selected function without committing the cell or moving selection.
Merge, paste, and editing errors use the same translation keys in separate dialogs when upstream presents an error.
Cell styles follow upstream categories and property-inclusion flags.
When number formatting is excluded, an empty custom format does not prevent saving.
A custom theme remains selectable after switching to a default theme within the panel.
Regional settings expose locale and timezone without a separate formula-language selector.
Link editing has external, internal-document, and email tabs, with errors shown at the same input stage as upstream.

The Name Manager follows upstream absolute references for the selected range, capturing the current selection, empty-list/no-results messages,
300 ms input validation, field-specific errors, disabled Apply while errors exist, and trimming names on save.
Scope is tracked by sheet ID so renaming a sheet preserves the range.

Detached link, custom-number-format, row/column-size, and sheet-deletion windows follow the invoking component's theme.
Link tooltips and border previews also use the current UI palette.
Sheet deletion initially focuses the confirmation button; Escape cancels and Cmd/Ctrl+Enter confirms.
Row/column sizes accept numbers in px.
Negative or non-finite input closes the dialog without applying a size.

The import picker accepts multiple files; drag-and-drop processes only the first file, as upstream does.
Progress and failure messages stay inside the import dialog, which closes on success.
Conversion uses the local engine instead of a server.

Link, custom-number-format, sheet/document-deletion, and import modals map upstream dimensions to Avalonia DIP:
44 px headers, 12 px inner padding, 8 px field spacing, 32 px inputs/buttons, and 8 px corner radii.
They close through the header X without a separate OS title bar, duplicate labels, or a bottom Close button.
Content-based height avoids extra empty space when tabs, errors, or translations change.
Existing-link Delete is on the left and Save on the right.
The detached sheet-selection list follows upstream checkmarks, 32 px rows, 4 px inner padding, and palette.
Avalonia owns input-control IME, accessibility, and focus.

For size comparisons, record browser zoom and native DPI and map CSS px to Avalonia DIP.
Distinguish Retina screenshot pixels from window dimensions in DIP.

Body text embeds upstream Inter regular/600.
The import drop area retains Arial 12 px, matching the font actually applied upstream.
WOFF2 fonts are converted to TTF for the Avalonia loader, preserving source/output hashes and OFL notices.
Browser and Skia text measurement and window pixel rounding can differ by fractional amounts.
DOM overlays map to owned native modals; file browsing uses OS file pickers.
Document deletion omits the shared-link warning because the app has no sharing server, so that dialog is shorter than upstream.

`WorkbookMenus` / `WorkbookMenuStyles.axaml` reproduce upstream Menu dimensions:
32 px rows, 4 px outer padding, 8 px item padding/spacing/corner radii, and 16 px icons/checkmarks, with the corresponding separator and deletion-item palette.
They do not use the default Fluent menu's extra spacing, width, or separate checkmark column.
Shared context menus and menu flyouts activate a focused, enabled command once when unmodified Space is released, matching upstream menu buttons.
Activation uses Avalonia's Enter handling, including check/radio state, commands, menu closing, and focus restoration.
Key repeat does not add activations; focus loss, another key, or menu closure cancels a pending Space press.
Modified Space and submenu headers keep their existing behavior; use Enter or Right to open a submenu.
The adapter only handles focused menu items, so text fields and worksheet shortcuts retain their own input handling.
The names list follows FormulaBar's 182 px minimum width, 12 px icons, and -4 px left offset.
Color controls use 16 px swatches in the upstream ten-column arrangement, with Recent and + always visible.
The advanced color picker follows the 240 px body and Hex/Cancel/Add color arrangement.
Borders use two rows of five 28 px buttons and upstream line-preview patterns.
Both chevron and right-click sheet menus anchor 24 px inside the tab's right edge.
Sheet lists and sheet-color popups also appear above the bottom tab bar.

The theme list uses upstream overlapping six-color previews and row layout.
A 32 px height difference caused by the presence of Custom reflects document theme state.
Measure popups separately from the main window, including submenus and color pickers.
Use relevant regressions to check menu commands, color application, sheet dropdowns, read-only behavior, and translation updates.
OS popup shadows replace CSS shadows.
Measure browser/Avalonia text and pixel-rounding differences separately; do not change palette policy to compensate for document theme values or OS screenshot color profiles.

PNG export sizes the output from the full selection coordinates and copies the current canvas.
It does not render offscreen cells again; those areas can remain transparent, as upstream.
Wheel handling converts Avalonia deltas to pixels, then calculates scroll position using actual row/column sizes.
When a worksheet selection, reference, or fill drag moves beyond the viewport, `WorksheetPointerController` moves the viewport.
`WorkbookEditorScrolling` handles text selection in the cell editor.
See [dragging and auto-scroll](NATIVE-PORT.md#dragging-and-auto-scroll) for their lifetimes and stopping conditions.
In read-only mode, right-click neither opens a context menu nor changes selection.

For cross-document Cut, source coordinates are interpreted in the receiving model.
The clipboard payload does not carry the other document's merge geometry or ownership.
It is not a complete cross-document move operation.

```sh
python3 tools/cyancalc/verify-upstream.py
python3 tools/cyancalc/sync-wasm-binding.py --check
node tools/cyancalc/sync-ui-assets.mjs --check
python3 tools/cyancalc/sync-ui-fonts.py --check
```

These commands verify source/generated-resource consistency.
They do not establish equivalence of actual native key input, detached popups, or rendered screens.
