#!/usr/bin/env bash
# Installs the game on the running emulator, plays it with simulated touch input and saves screenshots.
set -u
APK="$1"; OUT="$2"
PKG=com.elyasomar.arenarush
mkdir -p "$OUT"
adb wait-for-device
adb shell settings put system accelerometer_rotation 0 || true
adb install -r -g "$APK" || { echo "::error::install failed"; exit 1; }
adb logcat -c
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1
shot() { sleep "${2:-0}"; adb exec-out screencap -p > "$OUT/$1.png"; echo "screenshot $1 ($(stat -c%s "$OUT/$1.png") bytes)"; }

# Software Vulkan in the emulator is slow: give shaders/assets time to load.
for i in $(seq 1 30); do
  sleep 5
  adb shell pidof $PKG >/dev/null || { echo "::error::game process died"; break; }
done
shot 01-start
SIZE=$(adb shell wm size | grep -o '[0-9]*x[0-9]*' | tail -1); W=${SIZE%x*}; H=${SIZE#*x}
# landscape: the long side is X
if [ "$W" -lt "$H" ]; then T=$W; W=$H; H=$T; fi
echo "screen ${W}x${H}"
JX=$((W*15/100)); JY=$((H*70/100))
# walk forward with the left joystick
adb shell input motionevent DOWN $JX $JY
for s in 1 2 3 4 5 6; do adb shell input motionevent MOVE $JX $((JY - s*25)); done
shot 02-running 6
# turn the camera with a drag on the right side
adb shell input swipe $((W*70/100)) $((H*40/100)) $((W*50/100)) $((H*42/100)) 1500
shot 03-turned 4
# jump
adb shell input tap $((W*90/100)) $((H*78/100))
shot 04-jump 0.6
adb shell input motionevent UP $JX $((JY - 150))
shot 05-after 8
adb shell input swipe $((W*70/100)) $((H*40/100)) $((W*85/100)) $((H*30/100)) 1500
shot 06-look 6

adb logcat -d > "$OUT/logcat.txt" 2>/dev/null || true
grep -iE "FATAL|AndroidRuntime|Unhandled|Exception" "$OUT/logcat.txt" | head -40 || true
if ! adb shell pidof $PKG >/dev/null; then
  echo "::error::the game is not running anymore (see logcat.txt)"
  exit 1
fi
echo "game still running - OK"
