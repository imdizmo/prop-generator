#!/usr/bin/env bash
# Soak test: runs the real app under a private Xvfb for 30 minutes, driven through Claude's connection, and checks
# memory and leftover processes. See tools/soak.py for the drives and the pass rule.
# Usage: tools/soak.sh [SECONDS] [floor|engine|phase3] (default 1800 and floor; engine works easy-props' kinds on the
# engine; phase3 adds 0.4.5's features, exports at picture size 2048 and drives the cards with xdotool).
# SOAK_PORT picks Claude's connection port (default 47199) when another app already listens there; XVFB names the Xvfb
# to run (default: Xvfb on PATH). No memory cap on the app (the worker still has its own 2G cap, set by the app). The app runs with a private
# HOME under the scratch folder, so nothing it writes (sets, pieces, caches) lands in the user's own folders.
# Scratch lives in ~/.cache/prop-generator/scratch/soak/ and is removed at the end; only soak-samples.csv and
# soak-summary.txt (engine-soak-* for the engine drive, phase3-soak-* for the phase3 drive) are kept, in tools/soak-out/. Ctrl+C, SIGTERM, a failed start or a crashed driver all end in
# cleanup(), which stops everything this script started, by PID.
set -u
SECONDS_TOTAL="${1:-1800}"
DRIVE="${2:-floor}"
case "$DRIVE" in floor) NAME="soak" ;; engine) NAME="engine-soak" ;; phase3) NAME="phase3-soak" ;; *) echo "soak: drive is floor, engine or phase3" >&2; exit 2 ;; esac
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/src/PropGenerator.Desktop/bin/Release/net10.0"
SCR="$HOME/.cache/prop-generator/scratch/soak"
KEEP="$ROOT/tools/soak-out"
PORT="${SOAK_PORT:-47199}"
TOKEN="soak$(head -c 12 /dev/urandom | od -An -tx1 | tr -d ' \n')"
DISP=":97"
XVFB_PID=""
APP_PID=""
DRV_PID=""
mkdir -p "$SCR/map/models" "$SCR/home" "$SCR/work/sets" "$KEEP"
: > "$SCR/pids.txt"

# True when the pid is alive and is one of ours by name (a reused pid would not match).
ours() {
  [ -r "/proc/$1/cmdline" ] || return 1
  tr '\0' ' ' < "/proc/$1/cmdline" | grep -qE 'PropGenerator|worker\.py|multiprocessing|Xvfb|soak\.py|systemd-run'
}

descendants() {
  local c
  for c in $(pgrep -P "$1" 2>/dev/null); do echo "$c"; descendants "$c"; done
}

# Every pid this run started: the recorded ones, the app's descendants now, the app, the driver and Xvfb.
all_pids() {
  { cat "$SCR/pids.txt" 2>/dev/null
    [ -n "$APP_PID" ] && { echo "$APP_PID"; descendants "$APP_PID"; }
    [ -n "$DRV_PID" ] && echo "$DRV_PID"
    [ -n "$XVFB_PID" ] && echo "$XVFB_PID"
  } | sort -un
}

# Stops what is still running: SIGTERM, a moment to end, then SIGKILL. Prints the pids that needed stopping.
reap() {
  local alive="" p
  for p in $(all_pids); do ours "$p" && alive="$alive $p"; done
  [ -z "$alive" ] && return 0
  for p in $alive; do kill -TERM "$p" 2>/dev/null; done
  sleep 3
  for p in $alive; do
    if ours "$p"; then kill -KILL "$p" 2>/dev/null; fi
  done
  echo "$alive"
}

cleanup() {
  trap - EXIT INT TERM
  reap >/dev/null
  rm -rf "$SCR"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

printf '{"name": "Soak", "catalog": "catalog.json", "models": "models"}\n' > "$SCR/map/map.json"
printf '{}\n' > "$SCR/map/catalog.json"
if [ "$DRIVE" = phase3 ]; then
  printf '{"claudeEnabled": true, "claudePort": %s, "claudeToken": "%s", "exportPictureSize": 2048, "propsSort": "name", "saveFolder": "%s", "setFolder": "%s"}\n' \
    "$PORT" "$TOKEN" "$SCR/work" "$SCR/work/sets" > "$SCR/prefs.json"
else
  printf '{"claudeEnabled": true, "claudePort": %s, "claudeToken": "%s"}\n' "$PORT" "$TOKEN" > "$SCR/prefs.json"
fi
chmod 600 "$SCR/prefs.json"

# A free display number: :97 unless taken.
while [ -e "/tmp/.X11-unix/X${DISP#:}" ]; do DISP=":$(( ${DISP#:} + 1 ))"; done
"${XVFB:-Xvfb}" "$DISP" -screen 0 1600x1000x24 -nolisten tcp >/dev/null 2>&1 &
XVFB_PID=$!
sleep 2

HOME="$SCR/home" DISPLAY="$DISP" PROPGEN_BRIDGE_ROOT="$SCR/bridge" \
  "$OUT/PropGenerator" --prefs "$SCR/prefs.json" --library "$SCR/library.json" >"$SCR/app.log" 2>&1 &
APP_PID=$!

up=0
for _ in $(seq 1 60); do
  if curl -s -o /dev/null -m 2 -H "Authorization: Bearer $TOKEN" "http://127.0.0.1:$PORT/mcp"; then up=1; break; fi
  kill -0 "$APP_PID" 2>/dev/null || break
  sleep 1
done
if [ "$up" != 1 ]; then
  echo "soak: the app did not start listening on port $PORT" >&2
  tail -5 "$SCR/app.log" >&2
  exit 2
fi
sleep 5

# The driver runs in the background and is waited for, so Ctrl+C and SIGTERM reach the trap at once.
DISPLAY="$DISP" python3 -I "$ROOT/tools/soak.py" "$PORT" "$TOKEN" "$APP_PID" "$KEEP/$NAME-samples.csv" "$SCR/map" \
  "$SCR/work/soak-piece.propgen" "$SCR/pids.txt" "$SECONDS_TOTAL" "$DRIVE" >"$SCR/drive.log" 2>&1 &
DRV_PID=$!
wait "$DRV_PID"
drv_rc=$?
DRV_PID=""
cat "$SCR/drive.log"
summary="$(grep '^SUMMARY' "$SCR/drive.log")"

# Close the app the way a user would (SIGTERM), then check that everything is gone.
kill -TERM "$APP_PID" 2>/dev/null
for _ in $(seq 1 20); do kill -0 "$APP_PID" 2>/dev/null || break; sleep 1; done
sleep 3
left=""
for p in $(all_pids); do
  [ "$p" = "$XVFB_PID" ] && continue
  ours "$p" && left="$left $p"
done
# Xvfb is this script's own: stopped by PID, and it must be gone too.
kill "$XVFB_PID" 2>/dev/null
sleep 1
ours "$XVFB_PID" && left="$left $XVFB_PID"
if [ -n "$left" ]; then
  reap >/dev/null
  lefttxt="LEFTOVER:$left (killed)"
else
  lefttxt="leftover=none"
fi

fail=0
[ "$drv_rc" = 0 ] || fail=1
case "$summary" in *verdict=PASS*) ;; *) fail=1 ;; esac
[ -z "$left" ] || fail=1
[ -n "$summary" ] || summary="SUMMARY verdict=FAIL (the driver ended without a summary, exit $drv_rc)"
echo "$summary $lefttxt" | tee "$KEEP/$NAME-summary.txt"
[ "$fail" = 0 ] || exit 1
