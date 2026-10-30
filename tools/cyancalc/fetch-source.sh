#!/usr/bin/env bash
set -euo pipefail

ironcalc_lab_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ironcalc_source_dir="$(cd -- "$ironcalc_lab_dir/../.." && pwd)"
ironcalc_revision="297553dc3a37ec6b0f4821055220e3d26c4ac6a1"
ironcalc_repository="https://github.com/ironcalc/IronCalc.git"

if ! git -C "$ironcalc_source_dir" cat-file -e "$ironcalc_revision^{commit}" 2>/dev/null; then
  git -C "$ironcalc_source_dir" fetch --no-tags --depth 1 "$ironcalc_repository" "$ironcalc_revision"
fi

python3 "$ironcalc_lab_dir/verify-upstream.py" --source "$ironcalc_source_dir" --revision "$ironcalc_revision"
git -C "$ironcalc_source_dir" update-ref refs/remotes/upstream/cyancalc-base "$ironcalc_revision"

printf 'IronCalc source: %s\nRevision: %s\n' "$ironcalc_source_dir" "$ironcalc_revision"
