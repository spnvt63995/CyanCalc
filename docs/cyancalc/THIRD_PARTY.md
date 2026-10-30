# Third-party software

- IronCalc: <https://github.com/ironcalc/IronCalc>, MIT OR Apache-2.0.
  The native C ABI links the original Rust engine at the exact revision below.
  `bindings/dotnet/native/src/upstream_api.rs` is mechanically derived from that revision's
  `bindings/wasm/src/lib.rs`, with only native transport substitutions.
  `tools/cyancalc/sync-wasm-binding.py` records its source hash and also generates the dispatch
  and C# API. These derived bindings retain IronCalc's MIT/Apache-2.0 notices below.
  The Avalonia UI ports the upstream component behavior, number-format helpers,
  and embeds both the app and workbook component locale JSON catalogs directly
  from the pinned source tree. Native-only wording lives separately under
  `ui/avalonia/Localization/Locales` and is not presented as an upstream translation.
  It also ports the
  function catalogue (`ui/avalonia/Resources/functions.json`) and conditional icon path
  catalogue (`ui/avalonia/Resources/icons.json`) and the 36 conditional-format presets
  (`ui/avalonia/Resources/conditional-presets.json`, from the pinned `ColorScaleRule.tsx`
  and `IconSetsRule.tsx`). These derived resources retain the
  upstream license notices distributed alongside the native library.
  The native engine and source tree use release `v0.9.0`, commit
  `297553dc3a37ec6b0f4821055220e3d26c4ac6a1`. The bridge links this checkout's
  `xlsx/` and `base/` crates through a Cargo path dependency.
  The upstream license texts are preserved
  in `licenses/IronCalc.MIT.txt` and `licenses/IronCalc.Apache-2.0.txt`.
- Popup typography embeds the pinned IronCalc Inter regular/600 Latin fonts under
  SIL Open Font License 1.1. `licenses/Inter.OFL.txt` is distributed with the app.
  `tools/cyancalc/sync-ui-fonts.py` unwraps the original WOFF2 files into TTF for
  Avalonia; it preserves their names and outlines. Source and output hashes are
  recorded in `ui/avalonia/Resources/Fonts/provenance.json`. FontTools is needed
  only for regeneration, never for normal builds or the running component.
- The native icon paths also derive from Lucide/Feather. Their complete ISC/MIT
  notices are copied to `licenses/Lucide.ISC-and-Feather.MIT.txt` and included in
  .NET build/publish output. Avalonia and its dependencies are recorded in the
  native projects' `packages.lock.json`; the engine dependency tree is in `bindings/dotnet/native/Cargo.lock`.
- `WorkbookAdvancedColorPicker` ports the saturation/hue controls, HEX input behavior
  and HSV/RGB conversion order from the pinned web dependency react-colorful.
  Its MIT notice is preserved in `licenses/ReactColorful.MIT.txt` and included by
  the same .NET build/publish license glob.
- Native toolbar, menus and sheet-tab icons use the same `lucide-react@1.49.0` SVG nodes
  as the IronCalc 0.9.0 web UI. `ui/avalonia/Resources/workbook-ui-assets.json` records
  the package integrity and each source file's SHA-256. `ui/avalonia/Toolbar/WorkbookIcon.cs`
  is the Avalonia drawing adapter: it retains the original viewBox, shapes,
  stroke width, round caps/joins, foreground fills and inherited foreground color. No icon paths
  are redrawn locally. `ArrowMiddleFromLine` and the nine border SVGs come from
  IronCalc's own `webapp/IronCalc/src/icons/` under its MIT option. Border SVGs
  retain their separate gray guide strokes and foreground strokes.
  This import covers functional UI icons; IronCalc logos are not CyanCalc branding.
  The generated resource also includes the original light/dark CSS theme tokens
  from `webapp/IronCalc/src/theme/theme.ts`, with its source SHA-256.
- The optional application shell ports `webapp/app.ironcalc.com/frontend`'s
  FileBar, LeftDrawer, workbook catalog behavior and dialogs under IronCalc's MIT option.
  Its locale JSON and template preview PNG files are embedded directly from that
  pinned source tree. Functional shell icons use the same generated Lucide resource.
  Template workbooks are fetched on selection from the upstream public models route;
  they are separate from the version-pinned preview assets.

The original IronCalc source tree is kept at the repository root; CyanCalc additions
are under `bindings/dotnet/`, `ui/avalonia/`, and `apps/desktop/`. Installed dependencies and build outputs are ignored by Git.
The root README and combined ignore rules are local adaptations. The original
baseline is release v0.9.0, commit `297553dc3a37ec6b0f4821055220e3d26c4ac6a1`.
The desktop host, C ABI transport,
and Avalonia controls are local port code. The generated binding bodies and the
calculation engine remain IronCalc's implementation.

To refresh the native UI icon data after installing the locked upstream web
dependencies, run `node tools/cyancalc/sync-ui-assets.mjs` from the repository root.
Use `node tools/cyancalc/sync-ui-assets.mjs --check` to compare the committed data with
those sources and verify the Lucide/Feather license text. Normal .NET builds do
not need Node or `node_modules`. The full IronCalc MIT and Lucide ISC/Feather MIT
copyright and permission notices are copied to build and publish output by
`bindings/dotnet/CyanCalc.csproj`; preserve that `licenses/` folder in distributed apps.
These licenses permit commercial use, modification and redistribution with the
notices; they do not require an attribution banner on each toolbar icon.

License sources: [Lucide and Feather](https://lucide.dev/license),
[IronCalc 0.9.0 licensing choice](https://github.com/ironcalc/IronCalc/blob/v0.9.0/README.md#license),
[IronCalc MIT](https://github.com/ironcalc/IronCalc/blob/v0.9.0/LICENSE-MIT).
The software license is not permission to present CyanCalc as an official
IronCalc product; Apache-2.0 explicitly excludes trademark permission in §6.
