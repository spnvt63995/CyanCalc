# CyanCalc development guide

CyanCalc consists of a .NET binding, an Avalonia control library, and a desktop app.
The Rust bridge uses this repository's `xlsx/` and `base/` sources and maintains its own Cargo workspace and lockfile.
To embed the control, reference `ui/avalonia/CyanCalc.Avalonia.csproj`.
The window, file-dialog, and document-management code in `apps/desktop` is an optional reference implementation for hosts.

Run all commands from the repository root. You need .NET 10, Rust/Cargo, and a native linker for your platform.
Normal .NET builds use committed resources and do not require Node.js.
The default NuGet cache is `.cache/nuget/packages`; `NUGET_PACKAGES` and `RestorePackagesPath` overrides are also respected.

```sh
dotnet build CyanCalc.slnx -p:RestoreLockedMode=true
dotnet run --project apps/desktop --no-build
```

The root solution contains four projects: the binding, Avalonia controls, desktop app, and core checks.
`.ic` is the native format of the pinned engine. To exchange files from an older engine revision, export them to XLSX using that engine.
The desktop app provides the file bar and left document drawer from the full upstream web app.
It supports new documents, search, pinning, duplication, multiple selection and deletion, renaming, templates, import, and export.
Changed documents are saved to the local library every second; switching or closing documents commits the current edit before saving.
Imported `.ic`/`.xlsx` files become library copies without overwriting their source files.
`File → Download (.xlsx)` and `Save a copy (.ic)…` export separate files.

The default library is .NET's `LocalApplicationData/CyanCalc/Workbooks`.
Each library has a process lock to prevent simultaneous access by another host.
During development, you can select a separate library:

```sh
dotnet run --project apps/desktop --no-build -- --library /absolute/path/to/test-library
```

The `CYANCALC_LIBRARY_PATH` environment variable is also supported.
Documents, pinned items, selection, language, and theme settings are stored outside build output.
Template previews are pinned upstream resources; selecting a template downloads its document from the upstream public `/models/*.ic` route.
This download requires an internet connection. The app does not connect to a sharing server or provide Share uploads.

The macOS development bundle reuses `apps/desktop/artifacts/macos/CyanCalc.app` and a stable application ID.
The build does not overwrite a running app. Close its window before rebuilding so the current edit is saved.

```sh
bash apps/desktop/build-macos-app.sh
# Build and run in the same process; --library arguments are forwarded.
bash apps/desktop/run-macos-app.sh
```

Closing the window commits the current edit and saves the document.

## UI colors and workbook themes

The app's Light/Dark/System setting selects the CyanCalc UI palette.
`ui/avalonia/Resources/cyancalc-ui-palette.json` defines the teal accent `#009688`,
light gray surfaces for light mode, and dark gray surfaces for dark mode.
The palette applies to the grid, toolbar, panels, popups, and text selection.

`SheetPalette` reads the pinned IronCalc `workbook-ui-assets.json` and overrides only CyanCalc UI colors.
Generated assets remain unchanged, preserving upstream fonts, dimensions, and layout.
Workbook themes and the RGB/theme color slots stored in cells are separate document data.
Changing the app theme does not change cell formatting or Undo history.
Add UI colors through the CyanCalc palette and theme resources; do not replace document colors with UI accents.

## Web comparison

Use [upstream references](UPSTREAM-COMPARISON.md#using-upstream-references) to find the documentation, tests, and Storybook entry points for each feature.
The upstream component UI is available through Storybook in `webapp/IronCalc`.
The file bar, left drawer, and welcome screen belong to the separate `webapp/app.ironcalc.com/frontend` app.

```sh
# Pinned upstream user documentation. Install dependencies before the first run.
npm ci --prefix docs/docs.ironcalc.com
npm --prefix docs/docs.ironcalc.com run dev -- --host 127.0.0.1 --port 5175 --strictPort

# Once the upstream WASM and npm dependencies are ready:
npm --prefix webapp/IronCalc run storybook -- --host 127.0.0.1 --ci --disable-telemetry

# Full upstream app. Local document lists and menus work without an /api server.
npm --prefix webapp/IronCalc run build
npm ci --prefix webapp/app.ironcalc.com/frontend
npm --prefix webapp/app.ironcalc.com/frontend run dev -- --host 127.0.0.1 --port 5174
```

The Workbook story is at <http://127.0.0.1:6006/?path=/story/components-workbook--primary>.
User documentation is at <http://127.0.0.1:5175>, and the standalone Editor story is at
<http://127.0.0.1:6006/?path=/story/components-editor--default>.

## Public checks and resource generators

```sh
dotnet run --project tests/CyanCalc.CoreChecks --no-build
cargo test --locked --manifest-path bindings/dotnet/native/Cargo.toml --lib
python3 tools/cyancalc/verify-upstream.py
python3 tools/cyancalc/sync-wasm-binding.py --check
node tools/cyancalc/sync-ui-assets.mjs --check
```

The core checks exercise the engine, reference utilities, scalar readers, and links through the actual C ABI.
CyanCalc CI runs Rust regressions, all core .NET checks, and the desktop build using a public checkout.
See [public checks](../../tests/README.md) for coverage and fixture provenance.
These checks do not exercise native GUI focus, IME, clipboard, or platform dialogs.
Asset generation and comparison require the locked upstream web dependencies.
Normal builds use the generated JSON already in the repository.

See also [API compatibility](../../bindings/dotnet/API-COMPATIBILITY.md), [host integration](NATIVE-PORT.md),
[editor state and input boundaries](EDITOR-ARCHITECTURE.md), the [upstream implementation map](UPSTREAM-COMPARISON.md),
and [third-party notices](THIRD_PARTY.md).
