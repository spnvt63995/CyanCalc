#!/usr/bin/env python3
"""Verify the checked-in IronCalc source against its upstream Git blobs."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

PIN = "297553dc3a37ec6b0f4821055220e3d26c4ac6a1"
# CyanCalc keeps its own entry-point documentation and combined ignore rules.
ADAPTED = {"README.md", ".gitignore"}


def git(source, *args, input=None):
    return subprocess.check_output(
        ["git", "-C", str(source), *args], input=input, stderr=subprocess.PIPE
    )


def verify(source, revision):
    source = source.resolve()
    if git(source, "rev-parse", "--show-toplevel").decode().strip() != str(source):
        raise ValueError("Source must be a repository root")
    git(source, "cat-file", "-e", revision + "^{commit}")
    integrated = (source / "tools/cyancalc/verify-upstream.py").is_file()
    if not integrated:
        if git(source, "rev-parse", "HEAD").decode().strip() != revision:
            raise ValueError("External source checkout must be at the requested revision")
        if git(source, "status", "--porcelain", "--untracked-files=no").strip():
            raise ValueError("External source checkout has tracked changes")
    expected = {}
    symlinks = {}
    for record in git(source, "ls-tree", "-r", "-z", revision).split(b"\0"):
        if not record:
            continue
        metadata, raw_path = record.split(b"\t", 1)
        mode, kind, object_id = metadata.decode().split()
        path = raw_path.decode()
        if integrated and path in ADAPTED:
            continue
        if kind != "blob":
            raise ValueError("Unsupported upstream entry: " + path)
        if mode == "120000":
            symlinks[path] = object_id
        else:
            expected[path] = object_id
    missing = [path for path in expected if not (source / path).is_file()]
    if missing:
        raise ValueError("Missing original source files: " + ", ".join(missing[:10]))
    if integrated:
        manifest = json.loads((source / "tools/cyancalc/engine-patches.json").read_text())
        if manifest["baseline"] != revision:
            raise ValueError("Engine patch baseline differs from requested revision")
        for path, patch in manifest["files"].items():
            if expected.get(path) != patch["upstream_blob"]:
                raise ValueError("Engine patch does not match its upstream blob: " + path)
            expected[path] = patch["patched_blob"]
    paths = sorted(expected)
    if any("\n" in path for path in paths):
        raise ValueError("Unsupported newline in an upstream path")
    hashes = git(source, "hash-object", "--no-filters", "--stdin-paths",
                 input=("\n".join(paths) + "\n").encode()).decode().splitlines()
    if len(hashes) != len(paths):
        raise ValueError("Incomplete original source hash result")
    changed = [path for path, actual in zip(paths, hashes) if actual != expected[path]]
    for path, object_id in symlinks.items():
        file = source / path
        if not file.is_symlink():
            changed.append(path)
            continue
        actual = git(source, "hash-object", "--stdin", input=os.readlink(file).encode()).decode().strip()
        if actual != object_id:
            changed.append(path)
    if changed:
        raise ValueError("Source differs from upstream " + revision + ": " + ", ".join(changed[:20]))
    return len(expected) + len(symlinks)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--revision", default=PIN)
    parser.add_argument("--print-revision", action="store_true")
    args = parser.parse_args()
    try:
        count = verify(args.source, args.revision)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print("UPSTREAM_SOURCE_DIFF: " + str(error), file=sys.stderr)
        print("If the baseline Git object is missing, run bash tools/cyancalc/fetch-source.sh.", file=sys.stderr)
        return 1
    print(args.revision if args.print_revision else
          f"UPSTREAM_SOURCE_PASS ({count} files; {args.revision}; declared engine patches verified)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
