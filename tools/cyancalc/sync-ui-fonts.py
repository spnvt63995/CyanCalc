#!/usr/bin/env python3
"""Unwrap pinned upstream WOFF2 into SFNT for Avalonia's font loader.

Regeneration only: python -m pip install 'fonttools[woff]==4.67.0'.
Normal builds use the checked-in TTF files; outlines and names are unchanged.
"""
import hashlib
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[2]
output = root / "ui/avalonia/Resources/Fonts"
if "--check" in sys.argv:
    manifest = json.loads((output / "provenance.json").read_text())
    for record in manifest["fonts"]:
        for kind in ("source", "output"):
            actual = hashlib.sha256((root / record[kind]).read_bytes()).hexdigest()
            if actual != record[kind + "_sha256"]:
                raise SystemExit(f"Font hash mismatch: {record[kind]}")
    print("UI_FONTS_MATCH: pinned Inter sources and converted font hashes")
    raise SystemExit(0)

from fontTools.ttLib import TTFont

output.mkdir(parents=True, exist_ok=True)
records = []
for weight, name in (("regular", "Inter-Regular"), ("600", "Inter-SemiBold")):
    source = root / f"webapp/IronCalc/fonts/inter-v13-latin-{weight}.woff2"
    target = output / f"{name}.ttf"
    font = TTFont(source, recalcTimestamp=False)
    font.flavor = None
    font.save(target)
    records.append({"source": str(source.relative_to(root)), "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
                    "output": str(target.relative_to(root)), "output_sha256": hashlib.sha256(target.read_bytes()).hexdigest()})
(output / "provenance.json").write_text(json.dumps({"upstream": "297553dc3a37ec6b0f4821055220e3d26c4ac6a1", "conversion": "fonttools 4.67.0; WOFF2 to SFNT without outline/name changes", "fonts": records}, indent=2) + "\n")
