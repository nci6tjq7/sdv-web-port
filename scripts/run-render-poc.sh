#!/usr/bin/env bash
# Run the KNI WebGL rendering PoC in headless Chrome.
# Captures console.log (looking for "FPS:" lines) to validate the PoC.
#
# Exit codes:
#   0 = PASS (≥1 FPS reading captured, ideally ≥15)
#   1 = FAIL (no FPS captured — rendering didn't work)
#   2 = ERROR (build or serve failed)
#
set -uo pipefail

PROJECT_ROOT="/home/z/my-project"
PERSIST_DIR="${PERSIST_DIR:-$PROJECT_ROOT/.superpowers/sdd/poc-render-artifacts}"

cd "$PROJECT_ROOT"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"

mkdir -p "$PERSIST_DIR"

echo "=== KNI WebGL Rendering PoC ==="
echo "Started: $(date -Iseconds)"
echo ""

# ── Step 1: Build ─────────────────────────────────────────────────────────
echo "[1/5] Building project..."
if ! dotnet build src/SdvWebPort.PoC.Render/SdvWebPort.PoC.Render.csproj > "$PERSIST_DIR/build.log" 2>&1; then
  echo "    BUILD FAILED — see $PERSIST_DIR/build.log"
  tail -20 "$PERSIST_DIR/build.log"
  exit 2
fi
echo "    Build OK ($(tail -1 "$PERSIST_DIR/build.log"))"

# ── Step 2: Construct serve directory ─────────────────────────────────────
echo "[2/5] Constructing serve directory..."
SERVE_DIR="$PERSIST_DIR/serve"
rm -rf "$SERVE_DIR"
mkdir -p "$SERVE_DIR"

APPBUNDLE="$PROJECT_ROOT/src/SdvWebPort.PoC.Render/bin/Debug/net10.0/browser-wasm/AppBundle"
cp -r "$APPBUNDLE"/* "$SERVE_DIR/"
cp "$PROJECT_ROOT/src/SdvWebPort.PoC.Render/wwwroot/index.html" "$SERVE_DIR/index.html"
mkdir -p "$SERVE_DIR/Content"
cp "$PROJECT_ROOT/src/SdvWebPort.PoC.Render/Content/test_sprite.png" "$SERVE_DIR/Content/"

# Verify dotnet.js exists
if [ ! -f "$SERVE_DIR/_framework/dotnet.js" ]; then
  echo "    ERROR: _framework/dotnet.js not found in serve dir"
  ls "$SERVE_DIR/_framework/" | head -10
  exit 2
fi
echo "    Serve dir ready: $SERVE_DIR ($(du -sh "$SERVE_DIR" | cut -f1))"

# ── Step 3: Start HTTP server ─────────────────────────────────────────────
echo "[3/5] Starting HTTP server on :8765..."
PORT=8765
python3 -m http.server "$PORT" --directory "$SERVE_DIR" > "$PERSIST_DIR/http.log" 2>&1 &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null || true; pkill -f 'http.server $PORT' 2>/dev/null || true" EXIT

# Wait for server to be ready
for i in {1..15}; do
  if curl -s "http://localhost:$PORT/" > /dev/null 2>&1; then
    echo "    Server ready at http://localhost:$PORT/"
    break
  fi
  sleep 0.5
done

# ── Step 4: Run headless Chrome ───────────────────────────────────────────
echo "[4/5] Running headless Chrome (45s budget)..."
CHROME=/home/z/.agent-browser/browsers/chrome-149.0.7827.115/chrome
CHROME_LOG="$PERSIST_DIR/chrome.log"
DOM_DUMP="$PERSIST_DIR/dom-dump.html"

# WebGL2 requires these flags in headless mode
# --virtual-time-budget: how long virtual time advances (the page runs to completion)
# --timeout: real wall-clock timeout (default 15s; raise to be safe)
"$CHROME" \
  --headless \
  --no-sandbox \
  --use-gl=angle \
  --use-angle=swiftshader \
  --enable-webgl \
  --ignore-gpu-blocklist \
  --enable-unsafe-swiftshader \
  --enable-logging=stderr \
  --v=1 \
  --virtual-time-budget=45000 \
  --timeout=90000 \
  --dump-dom \
  "http://localhost:$PORT/" \
  > "$DOM_DUMP" 2> "$CHROME_LOG" &
CHROME_PID=$!

# Wait for Chrome (max 120s — virtual-time-budget is 45s, plus startup overhead)
for i in {1..120}; do
  if ! kill -0 $CHROME_PID 2>/dev/null; then
    break
  fi
  sleep 1
done
kill $CHROME_PID 2>/dev/null || true
wait $CHROME_PID 2>/dev/null || true
echo "    Chrome finished"

# ── Step 5: Analyze results ───────────────────────────────────────────────
echo "[5/5] Analyzing results..."

# Combine all log sources for FPS search
ALL_LOG="$PERSIST_DIR/all.log"
cat "$CHROME_LOG" > "$ALL_LOG"
echo "" >> "$ALL_LOG"
echo "=== DOM DUMP ===" >> "$ALL_LOG"
cat "$DOM_DUMP" >> "$ALL_LOG"

# Look for FPS readings
FPS_LINES=$(grep -oE "FPS: [0-9]+" "$ALL_LOG" || true)
FPS_COUNT=$(echo "$FPS_LINES" | grep -c "FPS:" || true)

if [ "$FPS_COUNT" -gt 0 ]; then
  echo ""
  echo "[+] Captured $FPS_COUNT FPS readings:"
  echo "$FPS_LINES"
  echo ""

  AVG_FPS=$(echo "$FPS_LINES" | grep -oE "[0-9]+" | awk '{sum+=$1; n++} END {if(n>0) print int(sum/n); else print 0}')
  MAX_FPS=$(echo "$FPS_LINES" | grep -oE "[0-9]+" | sort -n | tail -1)
  echo "[+] Average FPS: $AVG_FPS  |  Max FPS: $MAX_FPS"
  echo ""

  THRESHOLD=15
  if [ "$AVG_FPS" -ge "$THRESHOLD" ]; then
    echo "[PASS] Rendering PoC: Average FPS $AVG_FPS ≥ threshold $THRESHOLD"
    exit 0
  elif [ "$AVG_FPS" -ge 1 ]; then
    echo "[PASS-WITH-CONCERNS] Rendering PoC: Average FPS $AVG_FPS < threshold $THRESHOLD"
    echo "    R1 confirmed (per spec §10.1). Phase 1 must do render optimization."
    exit 1
  fi
fi

# No FPS captured — check what went wrong
echo ""
echo "[FAIL] No FPS readings captured. PoC did not render successfully."
echo ""
echo "=== Console log (last 50 lines) ==="
tail -50 "$CHROME_LOG" | grep -v "^$" | head -50
echo ""
echo "=== DOM dump (text content) ==="
grep -oE '<div id="log">[^<]*</div>' "$DOM_DUMP" | head -3 || cat "$DOM_DUMP" | head -30
echo ""
echo "=== Looking for errors ==="
grep -iE "error|exception|fail|fatal" "$CHROME_LOG" | head -10
echo ""
echo "Full logs at: $PERSIST_DIR/"
exit 1
