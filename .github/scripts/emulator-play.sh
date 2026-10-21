#!/usr/bin/env bash
# Installs the game on the running emulator, goes through the menus, plays with simulated
# touch input (move, aim, shoot, jump) and saves a screenshot after every step.
set -u
APK="$1"; OUT="$2"
PKG=com.elyasomar.arenarush
mkdir -p "$OUT"
adb wait-for-device
adb shell settings put system accelerometer_rotation 0 || true
adb install -r -g "$APK" || { echo "::error::install failed"; exit 1; }
adb logcat -c
adb logcat -v time > "$OUT/logcat.txt" 2>/dev/null &
LOGCAT_PID=$!
START=$(date +%s)
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1
shot() { sleep "${2:-0}"; adb exec-out screencap -p > "$OUT/$1.png"; echo "screenshot $1 at $(( $(date +%s) - START ))s ($(stat -c%s "$OUT/$1.png") bytes)"; }
alive() { adb shell pidof $PKG >/dev/null; }

SIZE=$(adb shell wm size | grep -o '[0-9]*x[0-9]*' | tail -1); W=${SIZE%x*}; H=${SIZE#*x}
if [ "$W" -lt "$H" ]; then T=$W; W=$H; H=$T; fi
echo "screen ${W}x${H}"
# overlay virtual space: height 720, width = 720 * W / H
vx() { echo $(( $1 * H / 720 )); }    # virtual x/y -> pixels (same scale on both axes)
nx() { echo $(( W * $1 / 1000 )); }   # normalized (per mille) -> pixels
ny() { echo $(( H * $1 / 1000 )); }

# Software Vulkan in the emulator is slow: wait for the first frames.
sleep 25; alive || echo "::error::game process died during start"
shot 01-splash
sleep 20
shot 02-main-menu

# SETTINGS (main menu button at virtual x 100..490, y 370..440)
adb shell input tap $(vx 300) $(vx 405); shot 03-settings 6
# BACK (centre of the screen, virtual y 570..634)
adb shell input tap $(( W / 2 )) $(vx 602); sleep 4
# PLAY (virtual y 280..350)
adb shell input tap $(vx 300) $(vx 315); shot 04-playing 10

# walk forward with the left stick
JX=$(nx 150); JY=$(ny 720)
adb shell input motionevent DOWN $JX $JY
for s in 1 2 3 4 5 6; do adb shell input motionevent MOVE $JX $((JY - s*25)); done
shot 05-running 5
adb shell input motionevent UP $JX $((JY - 150))

# hold FIRE (normalized 0.86, 0.70) and drag a little to aim
FX=$(nx 860); FY=$(ny 700)
adb shell input motionevent DOWN $FX $FY
adb shell input motionevent MOVE $((FX - 40)) $FY
shot 06-firing 2
adb shell input motionevent MOVE $((FX - 120)) $((FY - 10))
shot 07-firing-turned 3
adb shell input motionevent UP $((FX - 120)) $((FY - 10))

# swap weapon (0.93, 0.20) and fire again
adb shell input tap $(nx 930) $(ny 200); sleep 2
adb shell input motionevent DOWN $FX $FY
shot 08-m4-firing 2
adb shell input motionevent UP $FX $FY

# pause menu (0.5, 0.07)
adb shell input tap $(nx 500) $(ny 70); shot 09-paused 4

kill $LOGCAT_PID 2>/dev/null || true
echo "================= game log ================="
APP_PIDS=$(grep -oE "Start proc ([0-9]+):com.elyasomar.arenarush" "$OUT/logcat.txt" | grep -oE "[0-9]+" | sort -u | tr '\n' '|' | sed 's/|$//')
echo "app pids: $APP_PIDS"
grep -E "\(\s*(${APP_PIDS:-none})\)|ArenaRush|arenarush|DEBUG   |F/libc|monodroid|mono-rt|DOTNET|Stride|AndroidRuntime" "$OUT/logcat.txt" \
  | grep -vE "ModernMediaScanner|Telephony|Conscrypt" | tail -200 || true
echo "============================================"
if ! alive; then
  echo "::error::the game is not running anymore (see logcat.txt)"
  exit 1
fi
echo "game still running - OK"
