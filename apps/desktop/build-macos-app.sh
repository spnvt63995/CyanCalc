#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# Reuse one development app identity so rebuilds do not accumulate Dock entries.
output_dir="$script_dir/artifacts/macos"
app_dir="$output_dir/CyanCalc.app"
while IFS= read -r command; do
  if [[ "$command" == "$app_dir/Contents/MacOS/CyanCalc.Desktop" || "$command" == "$app_dir/Contents/MacOS/CyanCalc.Desktop "* ]]; then
    printf 'Close the running CyanCalc window before rebuilding: %s\n' "$app_dir" >&2
    exit 1
  fi
done < <(ps -axo command=)
mkdir -p "$output_dir"
bundle_stage="$(mktemp -d "$output_dir/.publish.XXXXXX")"
trap 'rm -rf -- "$bundle_stage"' EXIT
staged_app="$bundle_stage/CyanCalc.app"
mkdir -p "$staged_app/Contents/MacOS"
# Publish the current project's payload; old build outputs must not enter the bundle.
dotnet publish "$script_dir/CyanCalc.Desktop.csproj" --configuration Debug --no-self-contained \
  -p:RestoreLockedMode=true --output "$staged_app/Contents/MacOS" --nologo
cat > "$staged_app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>CyanCalc</string>
<key>CFBundleDisplayName</key><string>CyanCalc</string>
<key>CFBundleGetInfoString</key><string>CyanCalc is an unofficial .NET wrapper based on IronCalc.</string>
<key>CFBundleIdentifier</key><string>local.cyancalc.lab</string>
<key>CFBundleExecutable</key><string>CyanCalc.Desktop</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleVersion</key><string>1</string>
<key>CFBundleShortVersionString</key><string>0.1.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>LSMinimumSystemVersion</key><string>12.0</string>
</dict></plist>
PLIST
rm -rf -- "$app_dir"
mv "$staged_app" "$app_dir"
printf '%s\n' "$app_dir"
