# CyanCalc

CyanCalc extends [IronCalc](https://github.com/ironcalc/IronCalc) with a .NET binding,
a reusable Avalonia spreadsheet control, and a desktop editor. It is an independent,
unofficial project.

IronCalc owns calculation, workbook state, file formats, and workbook undo/redo.
The .NET binding calls that engine through a Rust C ABI. The Avalonia library
provides the spreadsheet UI; the desktop app owns windows and local document storage.

## Repository layout

```text
base/                       IronCalc calculation engine
xlsx/                       IronCalc workbook and Excel file support
bindings/
  wasm/, python/, nodejs/    Original IronCalc bindings
  dotnet/                   CyanCalc .NET API
    native/                 Rust C ABI with its own Cargo workspace
webapp/                     Original IronCalc web applications
ui/avalonia/                Reusable CyanCalc spreadsheet controls
apps/desktop/               CyanCalc desktop editor
tests/CyanCalc.CoreChecks/  Public regression checks
tests/fixtures/             Synthetic file compatibility fixtures
tools/cyancalc/             Upstream verification and resource generators
docs/cyancalc/              Architecture, integration, and development guides
CyanCalc.slnx               .NET libraries, desktop app, and regression checks
```

The native bridge uses the `xlsx/` and `base/` sources in this checkout.
Its Cargo workspace and lockfile remain separate from the upstream workspace.
The UI library can be embedded without using the desktop app.

## Build and run

Install the .NET SDK selected by `global.json`, Rust/Cargo, and the native linker
for your platform. Run these commands from the repository root:

```sh
dotnet build CyanCalc.slnx -p:RestoreLockedMode=true
dotnet run --project apps/desktop --no-build
```

Normal .NET builds use committed resources and do not need Node.js.
The development build targets the current operating system and architecture;
it is not a signed installer or a prebuilt NuGet distribution.

## Checks

```sh
dotnet run --project tests/CyanCalc.CoreChecks --no-build
cargo test --locked --manifest-path bindings/dotnet/native/Cargo.toml --lib
bash tools/cyancalc/fetch-source.sh
python3 tools/cyancalc/sync-wasm-binding.py --check
```

See [public checks](tests/README.md) for coverage and fixture provenance.

## Upstream baseline

The original source is based on **IronCalc v0.9.0**,
commit `297553dc3a37ec6b0f4821055220e3d26c4ac6a1`.
Original engine, bindings, web applications, names, and license files are retained.
The root README and ignore rules are adapted for this fork.
`fetch-source.sh` retrieves the baseline Git object when needed and verifies
the original files; it does not replace the working tree.

CyanCalc adds its own light and dark UI colors. Workbook themes and explicitly
formatted cell colors remain document data.

- [Development and desktop usage](docs/cyancalc/README.md)
- [Embedding the Avalonia control](docs/cyancalc/NATIVE-PORT.md)
- [Editor architecture](docs/cyancalc/EDITOR-ARCHITECTURE.md)
- [Upstream implementation map](docs/cyancalc/UPSTREAM-COMPARISON.md)
- [.NET API compatibility](bindings/dotnet/API-COMPATIBILITY.md)
- [Third-party notices](docs/cyancalc/THIRD_PARTY.md)
