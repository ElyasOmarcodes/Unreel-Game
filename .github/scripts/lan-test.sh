#!/usr/bin/env bash
# Runs two copies of the desktop build on the runner: one hosts a group (with bots), the other finds it
# through LAN discovery, joins and plays. Both save screenshots of every screen into $2.
set -u
BUILD="$(realpath "$1")"; OUT="$(realpath -m "$2")"
mkdir -p "$OUT"
if [ ! -x "$BUILD/AgentWarfare.x86_64" ]; then echo "::warning::no desktop build, skipping the LAN test"; exit 0; fi
sudo apt-get install -y -q xvfb libgl1-mesa-dri libglu1-mesa mesa-utils >/dev/null 2>&1 || true
run() { # role log
  xvfb-run -a -s "-screen 0 1280x720x24" "$BUILD/AgentWarfare.x86_64" -force-glcore -screen-fullscreen 0 \
    -screen-width 1280 -screen-height 720 -autotest "$1" -shots "$OUT" -logFile "$OUT/$1.log" &
}
run host
HOST=$!
sleep 15
run client
CLIENT=$!
# wait at most 21 minutes
for i in $(seq 1 252); do
  sleep 5
  kill -0 $HOST 2>/dev/null || kill -0 $CLIENT 2>/dev/null || break
done
kill $HOST $CLIENT 2>/dev/null || true
sleep 2
for role in host client; do
  echo "================= $role ================="
  grep -E "\[UA\]|Exception|Error" "$OUT/$role.log" | grep -v "^$" | head -120 || true
done
ls -la "$OUT"
ok=1
grep -q "\[UA\] client-joined" "$OUT/host.log" || { echo "::error::the client never joined the host"; ok=0; }
grep -q "\[UA\] joined id=" "$OUT/client.log" || { echo "::error::the client did not get a player id"; ok=0; }
grep -q "\[UA\] phase=Playing" "$OUT/client.log" || { echo "::error::the client never saw the match start"; ok=0; }
grep -q "\[UA\] autotest-done host" "$OUT/host.log" || { echo "::warning::the host test did not finish"; }
[ $ok = 1 ] && echo "LAN test OK" || exit 1
