#!/bin/bash
# FreeRDP's SDL3 client for MOAVI Desk.app, built with FreeRDP's own macOS bundle script (universal arm64 + x86_64,
# macOS 12+, its libraries built from source: nothing from Homebrew ends up in the app).
# Output: $OUT/MacOS/sdl-freerdp (FreeRDP's SDL3 client) and $OUT/Frameworks/*.dylib (rpath @loader_path/../Frameworks).
#   installer/macos/build-freerdp.sh [out-dir]       FREERDP_VERSION=3.33.1 by default
set -euo pipefail
FREERDP_VERSION="${FREERDP_VERSION:-3.33.1}"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
OUT="${1:-$ROOT/artifacts/freerdp-macos}"
SRC="$ROOT/artifacts/freerdp-src"

if [ -x "$OUT/MacOS/sdl-freerdp" ]; then echo "FreeRDP already in $OUT"; exit 0; fi

if [ ! -d "$SRC/.git" ]; then
  git clone --depth 1 -b "$FREERDP_VERSION" https://github.com/FreeRDP/FreeRDP.git "$SRC"
fi
"$SRC/scripts/bundle-mac-os.sh" --arch 'arm64 x86_64' --target 12

APP="$SRC/build-macos/install/MacFreeRDP.app/Contents"
rm -rf "$OUT"
mkdir -p "$OUT/MacOS" "$OUT/Frameworks"
cp "$(ls "$APP"/MacOS/sdl3-freerdp "$APP"/MacOS/sdl-freerdp 2>/dev/null | head -1)" "$OUT/MacOS/sdl-freerdp"
cp -P "$APP"/Frameworks/*.dylib "$OUT/Frameworks/"
cp "$SRC/LICENSE" "$OUT/FreeRDP-LICENSE.txt"
echo "$FREERDP_VERSION" > "$OUT/VERSION"
lipo -info "$OUT/MacOS/sdl-freerdp"
otool -L "$OUT/MacOS/sdl-freerdp"
