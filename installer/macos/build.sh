#!/bin/bash
# MOAVI Desk.app and its .dmg for one architecture.
#   installer/macos/build.sh osx-arm64|osx-x64 [freerdp-dir]
# freerdp-dir: what build-freerdp.sh made (default artifacts/freerdp-macos); without it the app has no RDP.
# Signing: with MACOS_SIGN_IDENTITY ("Developer ID Application: …", in the keychain) the app is signed with the
# hardened runtime; with APPLE_ID, APPLE_TEAM_ID and APPLE_APP_PASSWORD as well the .dmg is notarized and stapled.
# Without them the app is signed ad hoc (it runs; Gatekeeper asks once: right-click → 열기).
set -euo pipefail
RID="${1:?osx-arm64 or osx-x64}"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
FREERDP="${2:-$ROOT/artifacts/freerdp-macos}"
ARCH="${RID#osx-}"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props")"
OUT="$ROOT/artifacts/macos/$RID"
APP="$OUT/MOAVI Desk.app"

rm -rf "$OUT"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$APP/Contents/Frameworks"

dotnet publish "$ROOT/src/MoaviDesk.Mac/MoaviDesk.Mac.csproj" -c Release -r "$RID" --self-contained \
  -p:PublishSingleFile=false -o "$APP/Contents/MacOS"

if [ -x "$FREERDP/MacOS/sdl3-freerdp" ]; then
  cp "$FREERDP/MacOS/sdl3-freerdp" "$APP/Contents/MacOS/"
  cp -P "$FREERDP"/Frameworks/*.dylib "$APP/Contents/Frameworks/"
  cp "$FREERDP/FreeRDP-LICENSE.txt" "$APP/Contents/Resources/"
else
  echo "warning: no FreeRDP in $FREERDP - RDP needs 'brew install freerdp' on the Mac"
fi
cp "$ROOT/3rd-party-licenses.md" "$ROOT/LICENSE.md" "$APP/Contents/Resources/"

# the icon from docs/moavi.png
ICONSET="$OUT/moavi.iconset"
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s "$ROOT/docs/moavi.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2)); sips -z $d $d "$ROOT/docs/moavi.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/moavi.icns"
rm -rf "$ICONSET"

sed "s/@VERSION@/$VERSION/g" "$HERE/Info.plist" > "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"

# signing: every dylib and executable inside first, then the app
if [ -n "${MACOS_SIGN_IDENTITY:-}" ]; then
  SIGN=(codesign --force --timestamp --options runtime --entitlements "$HERE/entitlements.plist" -s "$MACOS_SIGN_IDENTITY")
else
  SIGN=(codesign --force -s -)
fi
find "$APP/Contents/Frameworks" "$APP/Contents/MacOS" -type f \( -name "*.dylib" -o -perm -111 \) -print0 |
  while IFS= read -r -d '' f; do "${SIGN[@]}" "$f"; done
"${SIGN[@]}" "$APP"
codesign --verify --deep --strict "$APP"

# the .dmg: the app and a link to Applications
DMG="$ROOT/artifacts/macos/moavi-desk-$VERSION-macos-$ARCH.dmg"
STAGE="$OUT/dmg"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
rm -f "$DMG"
hdiutil create -volname "MOAVI Desk" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
rm -rf "$STAGE"

if [ -n "${MACOS_SIGN_IDENTITY:-}" ]; then
  codesign --force --timestamp -s "$MACOS_SIGN_IDENTITY" "$DMG"
  if [ -n "${APPLE_ID:-}" ] && [ -n "${APPLE_TEAM_ID:-}" ] && [ -n "${APPLE_APP_PASSWORD:-}" ]; then
    xcrun notarytool submit "$DMG" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD" --wait
    xcrun stapler staple "$DMG"
  fi
fi
echo "$DMG"
