#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
bash "$script_dir/build-macos-app.sh"
# Replace this process so the caller can monitor the application's lifetime.
exec "$script_dir/artifacts/macos/CyanCalc.app/Contents/MacOS/CyanCalc.Desktop" "$@"
