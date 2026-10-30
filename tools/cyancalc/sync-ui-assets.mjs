// Import the actual IronCalc UI assets; do not redraw their paths in the desktop UI.
// Run after `npm ci` in webapp/IronCalc. Normal .NET builds use the committed JSON.
import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import { fileURLToPath, pathToFileURL } from "node:url";
import path from "node:path";

const root = fileURLToPath(new URL("../../", import.meta.url));
const upstream = "297553dc3a37ec6b0f4821055220e3d26c4ac6a1";
const packagePath = "webapp/IronCalc/node_modules/lucide-react";
const output = "ui/avalonia/Resources/workbook-ui-assets.json";
const read = (name) => readFile(path.join(root, name));
const hash = (bytes) => createHash("sha256").update(bytes).digest("hex");
const lock = JSON.parse(await read("webapp/IronCalc/package-lock.json"));
const dependency = lock.packages["node_modules/lucide-react"];
const installed = JSON.parse(await read(`${packagePath}/package.json`));
if (dependency.version !== "1.49.0" || installed.version !== dependency.version)
  throw new Error("Expected IronCalc v0.9.0's locked lucide-react 1.49.0");
if (!(await read(`${packagePath}/LICENSE`)).equals(await read("docs/cyancalc/licenses/Lucide.ISC-and-Feather.MIT.txt")))
  throw new Error("Update the distributed Lucide/Feather notices before importing assets");

// Export names used by the original toolbar, sheet bar and drawers, including aliases.
const names = {
  Undo2: "undo-2", Redo2: "redo-2", PaintRoller: "paint-roller", RemoveFormatting: "remove-formatting",
  DollarSign: "dollar-sign", Euro: "euro", PoundSterling: "pound-sterling", Percent: "percent",
  DecimalsArrowLeft: "decimals-arrow-left", DecimalsArrowRight: "decimals-arrow-right",
  Minus: "minus", Plus: "plus", Bold: "bold", Italic: "italic", Underline: "underline",
  Strikethrough: "strikethrough", Type: "type", PaintBucket: "paint-bucket", Grid2X2: "grid-2x2",
  AlignLeft: "align-left", AlignCenter: "align-center", AlignRight: "align-right",
  ArrowUpToLine: "arrow-up-to-line", ArrowDownToLine: "arrow-down-to-line", WrapText: "wrap-text",
  TableCellsMerge: "table-cells-merge", Link2: "link-2", Link2Off: "link-2-off", ChevronUp: "chevron-up",
  ChevronLeft: "chevron-left", ChevronRight: "chevron-right", ChevronDown: "chevron-down",
  Menu: "menu", Settings: "settings", Settings2: "settings-2", Brush: "brush", SwatchBook: "swatch-book", Layers: "layers",
  ImageDown: "image-down", Grid2x2Check: "grid-2x2-check", Grid2x2X: "grid-2x2-x",
  AlignCenterHorizontal: "align-center-horizontal", Rows3: "rows-3", Columns3: "columns-3", TableCellsSplit: "table-cells-split",
  Scissors: "scissors", Copy: "copy", Clipboard: "clipboard", Trash2: "trash-2",
  ArrowUp: "arrow-up", ArrowDown: "arrow-down", ArrowLeft: "arrow-left", ArrowRight: "arrow-right",
  MoveVertical: "move-vertical", MoveHorizontal: "move-horizontal", Eye: "eye", EyeOff: "eye-off",
  Snowflake: "snowflake", PencilLine: "pencil-line", LineStyle: "line-style", Tag: "tag", TextCursorInput: "text-cursor-input",
  PanelLeftOpen: "panel-left-open", PanelLeftClose: "panel-left-close", Search: "search", X: "x", Check: "check",
  Table2: "table-2", Pin: "pin", PinOff: "pin-off", EllipsisVertical: "ellipsis-vertical",
  FileDown: "file-down", FileUp: "file-up", BookOpen: "book-open", Info: "info",
  Keyboard: "keyboard", Globe: "globe", Sun: "sun", CloudOff: "cloud-off", LayoutTemplate: "layout-template",
  PackageOpen: "package-open", SearchX: "search-x", SquareMousePointer: "square-mouse-pointer",
};
const sources = {};
async function source(name) {
  const bytes = await read(name);
  sources[name] = hash(bytes);
  return bytes.toString();
}
async function lucide(name) {
  const file = `${packagePath}/dist/esm/icons/${name}.mjs`;
  const text = await source(file);
  const data = (await import(pathToFileURL(path.join(root, file)).href)).__iconData;
  if (data) return data;
  const alias = text.match(/export \{ default \} from '\.\/([\w-]+)\.mjs'/);
  if (!alias) throw new Error(`Unsupported Lucide export: ${file}`);
  return lucide(alias[1]);
}
const attributesFile = `${packagePath}/dist/esm/shared/src/build/defaultAttributes.mjs`;
await source(attributesFile);
const { default: attributes } = await import(pathToFileURL(path.join(root, attributesFile)).href);
if (attributes.fill !== "none" || attributes.stroke !== "currentColor" || attributes["stroke-linecap"] !== "round" || attributes["stroke-linejoin"] !== "round")
  throw new Error("Lucide drawing attributes changed; review the Avalonia renderer");
const supported = { path: ["d"], line: ["x1", "y1", "x2", "y2"], circle: ["cx", "cy", "r"], rect: ["x", "y", "width", "height", "rx", "ry"] };
const icons = {};
for (const [name, module] of Object.entries(names)) {
  const data = await lucide(module);
  const nodes = data.node.map(([type, props]) => {
    const { key, ...values } = props;
    if (!supported[type] || Object.keys(values).some((key) => !supported[type].includes(key) && !(key === "fill" && values.fill === "currentColor")))
      throw new Error(`Unsupported SVG attributes in ${name}: ${type}`);
    return { type, ...values };
  });
  icons[name] = { size: data.size, strokeWidth: attributes["stroke-width"], nodes };
}

// Preserve each original path and its foreground/gray stroke. These 16px SVGs
// use one-unit strokes; unexpected SVG features require a renderer review.
const custom = { ArrowMiddleFromLine: "arrow-middle-from-line", BorderInner: "border-inner",
  BorderCenterH: "border-center-h", BorderCenterV: "border-center-v", BorderOuter: "border-outer",
  BorderNone: "border-none", BorderTop: "border-top", BorderRight: "border-right",
  BorderBottom: "border-bottom", BorderLeft: "border-left" };
for (const [name, file] of Object.entries(custom)) {
  const svg = await source(`webapp/IronCalc/src/icons/${file}.svg`);
  const nodes = [...svg.matchAll(/<path\b([^>]*)\/>/g)].map((match) => {
    const attrs = Object.fromEntries([...match[1].matchAll(/([\w-]+)="([^"]*)"/g)].map((attr) => [attr[1], attr[2]]));
    if (Object.keys(attrs).some((key) => !["id", "d", "stroke", "stroke-linecap", "stroke-linejoin"].includes(key)) ||
        !["currentColor", "#B2B2B2"].includes(attrs.stroke) || attrs["stroke-linecap"] !== "round" || attrs["stroke-linejoin"] !== "round")
      throw new Error(`Review the changed ${name} SVG attributes`);
    return { type: "path", d: attrs.d, ...(attrs.stroke === "currentColor" ? {} : { stroke: attrs.stroke }) };
  });
  if (!svg.includes('viewBox="0 0 16 16"') || svg.includes("transform=") || nodes.length === 0 ||
      [...svg.replace(/<defs>\s*<clipPath id="clip0_107_4135">\s*<rect width="16" height="16" fill="white"\/>\s*<\/clipPath>\s*<\/defs>/, "").matchAll(/<(?!\/)(\w+)\b/g)].some((match) => !["svg", "g", "path"].includes(match[1])))
    throw new Error(`Review the changed ${name} SVG structure`);
  icons[name] = { size: 16, strokeWidth: 1, nodes };
}
// The original formula-bar glyph uses a 12x14 viewBox and one filled path.
const fx = await source("webapp/IronCalc/src/icons/fx.svg");
const fxPath = fx.match(/<path[^>]* d="([^"]+)"[^>]* fill="#828282"/);
if (!fx.includes('viewBox="0 0 12 14"') || !fxPath) throw new Error("Review the original Fx SVG");
icons.Fx = { size: 14, width: 12, strokeWidth: 0, nodes: [{ type: "path", d: fxPath[1], fill: "currentColor" }] };
await source(`${packagePath}/LICENSE`);
await source("webapp/IronCalc/src/components/Toolbar/Toolbar.tsx");
await source("webapp/IronCalc/src/components/SheetTabBar/SheetTabBar.tsx");
await source("webapp/app.ironcalc.com/frontend/src/components/FileBar.tsx");
await source("webapp/app.ironcalc.com/frontend/src/components/LeftDrawer/WorkbookMenu.tsx");
const themeSource = await source("webapp/IronCalc/src/theme/theme.ts");
const themes = {};
for (const [name, exported] of [["light", "defaultThemeVariables"], ["dark", "darkThemeVariables"]]) {
  const body = themeSource.match(new RegExp(`export const ${exported}: IronCalcThemeVariables = \\{([\\s\\S]*?)\\n\\};`))?.[1];
  if (!body) throw new Error(`Cannot locate ${exported}`);
  themes[name] = Object.fromEntries([...body.matchAll(/"(--[^"]+)":\s*(?:"([^"]*)"|'([^']*)')/g)]
    .map(([, key, doubleQuoted, singleQuoted]) => [key, doubleQuoted ?? singleQuoted]));
  if (!themes[name]["--palette-sheet-grid-color"]) throw new Error(`Incomplete ${exported}`);
}

const content = JSON.stringify({
  provenance: {
    upstream, lucideVersion: installed.version, lucideIntegrity: dependency.integrity,
    licenses: ["Lucide.ISC-and-Feather.MIT.txt", "IronCalc.MIT.txt"],
    generatedBy: "tools/cyancalc/sync-ui-assets.mjs", sources,
  }, icons, themes,
}, null, 2) + "\n";
if (process.argv.includes("--check")) {
  if ((await read(output)).toString() !== content) throw new Error("UI assets differ from their pinned source; run node tools/cyancalc/sync-ui-assets.mjs");
  console.log(`UI_ASSETS_MATCH: ${Object.keys(icons).length} original icons; licenses match`);
} else {
  await writeFile(path.join(root, output), content);
  console.log(`Imported ${Object.keys(icons).length} original icons into ${output}`);
}
