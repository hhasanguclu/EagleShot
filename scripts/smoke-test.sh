#!/usr/bin/env bash
# Starts EagleShot on an X server (e.g. under xvfb-run), takes a full-screen capture through the
# command line of a second instance, and checks that the PNG and the history entry were written.
set -euo pipefail
EXE="${1:-target/release/eagleshot}"
WORK="$(mktemp -d)"
export EAGLESHOT_CONFIG_DIR="$WORK/config"
export EAGLESHOT_PORT=47555
export HOME="$WORK/home"
export XDG_PICTURES_DIR="$WORK/pictures"
mkdir -p "$HOME" "$XDG_PICTURES_DIR" "$EAGLESHOT_CONFIG_DIR"
printf '{ "SaveFolder": "%s", "ShowSplash": false, "AfterCapture": { "CopyToClipboard": false, "SaveToFile": true, "ShowNotification": true } }' "$WORK/shots" > "$EAGLESHOT_CONFIG_DIR/settings.json"

"$EXE" --version
"$EXE" --minimized > "$WORK/log.txt" 2>&1 &
PID=$!
cleanup() { kill "$PID" 2>/dev/null || true; }
trap cleanup EXIT
sleep 4
if ! kill -0 "$PID" 2>/dev/null; then echo "EagleShot exited early:"; cat "$WORK/log.txt"; exit 1; fi

"$EXE" --fullscreen
for _ in $(seq 1 30); do
  if ls "$WORK/shots"/*.png >/dev/null 2>&1 && [ -s "$EAGLESHOT_CONFIG_DIR/history.json" ]; then break; fi
  sleep 0.5
done
ls -la "$WORK/shots"
FILE="$(ls "$WORK/shots"/*.png | head -1)"
[ -s "$FILE" ] || { echo "no screenshot was saved"; cat "$WORK/log.txt"; exit 1; }
grep -q '"Kind":"Full screen"' "$EAGLESHOT_CONFIG_DIR/history.json" || { echo "history entry missing"; cat "$EAGLESHOT_CONFIG_DIR/history.json"; exit 1; }

# the second instance forwarded its command instead of starting another EagleShot
"$EXE" --quit
for _ in $(seq 1 20); do kill -0 "$PID" 2>/dev/null || break; sleep 0.5; done
if kill -0 "$PID" 2>/dev/null; then echo "EagleShot did not quit"; cat "$WORK/log.txt"; exit 1; fi
echo "smoke test passed: $FILE"
