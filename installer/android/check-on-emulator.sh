#!/bin/bash
# The APK against a real RDP server, on the Android emulator (CI: rdp-check.yml). The host's xrdp is 10.0.2.2:3389
# from the emulator. Passes when FreeRDP's session screen connected (GlobalApp: OnConnectionSuccess) - that is,
# MOAVI Desk set up FreeRDP (AndroidRdp.Prepare) and opened its screen with MOAVI Desk's bar.
#   installer/android/check-on-emulator.sh <apk> <out-dir>
set -uo pipefail
APK="$1"
OUT="$2"
mkdir -p "$OUT"
adb install -r "$APK"
adb logcat -c
adb shell am start -n com.makussystems.moavidesk/com.makussystems.moavidesk.MainActivity \
  --es moavi.test.rdp "10.0.2.2:3389|abc|abc"
ok=1
for i in $(seq 1 60); do
  sleep 2
  if adb logcat -d | grep -q "OnConnectionSuccess"; then ok=0; break; fi
done
sleep 8
adb exec-out screencap -p > "$OUT/android-rdp.png"
adb logcat -d > "$OUT/android-logcat.txt"
grep -E "MoaviDesk|GlobalApp|FreeRDP|AndroidRuntime|SessionActivity" "$OUT/android-logcat.txt" | tail -60
if [ $ok -eq 0 ]; then echo "PASS: FreeRDP's session screen connected"; else echo "FAIL: no connection"; fi
exit $ok
