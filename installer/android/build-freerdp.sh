#!/bin/bash
# FreeRDP's Android session screen (client/Android/Studio/freeRDPCore) as an .aar for MoaviDesk.Android:
# FreeRDP's own Gradle + CMake build, which fetches and builds OpenSSL and the rest for each ABI.
#   installer/android/build-freerdp.sh [out-dir]      FREERDP_VERSION=3.33.1 by default
# Needs ANDROID_HOME (or ANDROID_SDK_ROOT) and a JDK 17+; installs the NDK / CMake / platform it asks for.
# Output: $OUT/freeRDPCore-release.aar
set -euo pipefail
FREERDP_VERSION="${FREERDP_VERSION:-3.33.1}"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
OUT="${1:-$ROOT/artifacts/freerdp-android}"
SRC="$ROOT/artifacts/freerdp-src-android"
SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:?ANDROID_HOME}}"

if [ -f "$OUT/freeRDPCore-release.aar" ]; then echo "FreeRDP already in $OUT"; exit 0; fi

if [ ! -d "$SRC/.git" ]; then
  git clone --depth 1 -b "$FREERDP_VERSION" https://github.com/FreeRDP/FreeRDP.git "$SRC"
fi
STUDIO="$SRC/client/Android/Studio"

# phones and tablets (arm64) and the emulator (x86_64); OpenSSL and cJSON only (FreeRDP's own qa set: no H.264 decoder yet)
cat > "$STUDIO/release.properties" <<'PROPS'
ABI_FILTERS=arm64-v8a;x86_64
SPLIT_ARCHITECTURES=arm64-v8a;x86_64
SPLIT_ENABLED=false
CMAKE_ARGUMENTS=-DWITH_FFMPEG=OFF;-DWITH_OPENH264=OFF;-DWITH_OPUS=OFF;-DWITH_WEBP=OFF;-DWITH_JPEG=OFF;-DWITH_PNG=OFF;-DWITH_CJSON=ON;-DWITH_OPENSSL=ON
PROPS

# what its build.gradle asks for (API 37 and its build tools come from the SDK's beta channel for now)
NDK=$(sed -n "s/.*'NDK_VERSION', '\([^']*\)'.*/\1/p" "$STUDIO/build.gradle")
CMAKE=$(sed -n "s/.*'CMAKE_VERSION', '\([^']*\)'.*/\1/p" "$STUDIO/build.gradle")
API=$(sed -n "s/.*'COMPILE_API', \([0-9]*\).*/\1/p" "$STUDIO/build.gradle")
TOOLS=$(sed -n "s/.*'TOOLS_VERSION', '\([^']*\)'.*/\1/p" "$STUDIO/build.gradle")
SDKMANAGER="$(ls "$SDK"/cmdline-tools/*/bin/sdkmanager | sort | tail -1)"
yes | "$SDKMANAGER" --licenses >/dev/null || true
# the platform is "android-37.0" in the SDK repository (minor versions since API 36)
PLATFORM=$("$SDKMANAGER" --channel=1 --list 2>/dev/null | grep -oE "platforms;android-$API(\.0)?( |$)" | head -1 | tr -d ' ')
"$SDKMANAGER" --channel=1 "ndk;$NDK" "cmake;$CMAKE" "${PLATFORM:-platforms;android-$API}" "build-tools;$TOOLS"
ls "$SDK/platforms"

cd "$STUDIO"
chmod +x gradlew
./gradlew --no-daemon :freeRDPCore:assembleRelease

mkdir -p "$OUT"
cp freeRDPCore/build/outputs/aar/freeRDPCore-release.aar "$OUT/"
cp "$SRC/LICENSE" "$OUT/FreeRDP-LICENSE.txt"
echo "$FREERDP_VERSION" > "$OUT/VERSION"
unzip -l "$OUT/freeRDPCore-release.aar" | grep -E "\.so$|classes.jar"
