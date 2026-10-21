#!/usr/bin/env bash
# Installs the Unity test APK on the emulator, waits until the game logs that it runs and takes screenshots.
set -u
APK="$1"; OUT="$2"
PKG=com.elyasomar.unitytest
mkdir -p "$OUT"
adb wait-for-device
adb install -r -g "$APK" || { echo "::error::install failed"; exit 1; }
adb logcat -c
adb logcat -v time > "$OUT/logcat.txt" 2>/dev/null &
LOGCAT_PID=$!
START=$(date +%s)
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1
shot() { adb exec-out screencap -p > "$OUT/$1.png"; echo "screenshot $1 at $(( $(date +%s) - START ))s"; }

for i in $(seq 1 36); do
  sleep 5
  adb shell pidof $PKG >/dev/null || { echo "::error::game process died"; break; }
  grep -q "\[UnityTest\] first-frame" "$OUT/logcat.txt" && break
done
sleep 5; shot 01-start
sleep 10; shot 02-running

kill $LOGCAT_PID 2>/dev/null || true
echo "================= game log ================="
grep -E "UnityTest|Unity   |CRASH|AndroidRuntime|F/libc" "$OUT/logcat.txt" | tail -80 || true
echo "============================================"
adb shell pidof $PKG >/dev/null || { echo "::error::the game is not running anymore"; exit 1; }
grep -q "\[UnityTest\] first-frame" "$OUT/logcat.txt" || { echo "::error::the game never logged its first frame"; exit 1; }
echo "game running - OK"
