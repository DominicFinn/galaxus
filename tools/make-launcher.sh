#!/usr/bin/env bash
# Builds ~/Applications/Galaxus.app: a small launcher with the Galaxus name and icon that builds
# the latest code and runs the game. Double-click it, or drag it to the Dock.
# Until the game is exported as a proper app, macOS's menu bar still says "Godot" while it runs.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$HOME/Applications/Galaxus.app"
GODOT="${GODOT:-$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot}"
DOTNET="$(command -v dotnet || echo /opt/homebrew/bin/dotnet)"
DOTNET_ROOT_DIR="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$ROOT/docs/branding/Galaxus.icns" "$APP/Contents/Resources/Galaxus.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Galaxus</string>
  <key>CFBundleDisplayName</key><string>Galaxus</string>
  <key>CFBundleIdentifier</key><string>com.galaxus.launcher</string>
  <key>CFBundleExecutable</key><string>Galaxus</string>
  <key>CFBundleIconFile</key><string>Galaxus</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>0.1</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSArchitecturePriority</key><array><string>arm64</string><string>x86_64</string></array>
  <key>LSRequiresNativeExecution</key><true/>
</dict>
</plist>
PLIST

cat > "$APP/Contents/MacOS/Galaxus" <<SCRIPT
#!/bin/bash
# Launches Galaxus from $ROOT. Output goes to ~/Library/Logs/Galaxus.log.
export DOTNET_ROOT="$DOTNET_ROOT_DIR"
export PATH="\$(dirname "$DOTNET"):\$PATH"
LOG="\$HOME/Library/Logs/Galaxus.log"
"$DOTNET" build "$ROOT/game/Galaxus.csproj" -nologo -v quiet > "\$LOG" 2>&1
# Run natively on Apple silicon; never under Rosetta.
if [ "\$(uname -m)" = "arm64" ] || [ "\$(sysctl -n hw.optional.arm64 2>/dev/null)" = "1" ]; then
  exec arch -arm64 "$GODOT" --path "$ROOT/game" >> "\$LOG" 2>&1
fi
exec "$GODOT" --path "$ROOT/game" >> "\$LOG" 2>&1
SCRIPT
chmod +x "$APP/Contents/MacOS/Galaxus"

# Tell Finder and the Dock about the new app so the icon shows straight away.
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$APP" >/dev/null 2>&1 || true
touch "$APP"
echo "Made $APP"
