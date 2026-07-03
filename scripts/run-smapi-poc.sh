#!/usr/bin/env bash
# Run the SMAPI load PoC in headless Chrome, capture console output.
#
# Exit codes:
#   0 = PASS (SMAPI loaded, types enumerated)
#   1 = FAIL (load threw exception or types couldn't be enumerated)
#   2 = SETUP ERROR (SMAPI.dll missing, build failed, etc.)
#
set -uo pipefail

PROJECT_ROOT="/home/z/my-project"
PERSIST_DIR="${PERSIST_DIR:-$PROJECT_ROOT/.superpowers/sdd/poc-smapi-artifacts}"

cd "$PROJECT_ROOT"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"

mkdir -p "$PERSIST_DIR"

echo "=== SMAPI Assembly Load PoC ==="
echo "Started: $(date -Iseconds)"
echo ""

# ── Step 1: Verify SMAPI.dll present ───────────────────────────────────────
SMAPI_DLL="$PROJECT_ROOT/src/SdvWebPort.PoC.SmapiLoad/StardewModdingAPI.dll"
if [ ! -f "$SMAPI_DLL" ]; then
  echo "[FAIL] StardewModdingAPI.dll not found at:"
  echo "       $SMAPI_DLL"
  echo ""
  echo "       See src/SdvWebPort.PoC.SmapiLoad/README.md for setup instructions."
  exit 2
fi
echo "[+] SMAPI.dll present: $(stat -c '%s bytes' "$SMAPI_DLL")"

# ── Step 2: Build ──────────────────────────────────────────────────────────
echo ""
echo "[2/5] Building project..."
if ! dotnet build src/SdvWebPort.PoC.SmapiLoad/SdvWebPort.PoC.SmapiLoad.csproj > "$PERSIST_DIR/build.log" 2>&1; then
  echo "    BUILD FAILED — see $PERSIST_DIR/build.log"
  tail -20 "$PERSIST_DIR/build.log"
  exit 2
fi
echo "    Build OK"

# ── Step 3: Construct serve directory ──────────────────────────────────────
echo ""
echo "[3/5] Constructing serve directory..."
SERVE_DIR="$PERSIST_DIR/serve"
rm -rf "$SERVE_DIR"
mkdir -p "$SERVE_DIR"

APPBUNDLE="$PROJECT_ROOT/src/SdvWebPort.PoC.SmapiLoad/bin/Debug/net10.0/browser-wasm/AppBundle"
cp -r "$APPBUNDLE"/* "$SERVE_DIR/"
cp "$PROJECT_ROOT/src/SdvWebPort.PoC.SmapiLoad/wwwroot/index.html" "$SERVE_DIR/index.html"
# Copy SMAPI.dll to serve dir root so HttpClient can fetch it
cp "$SMAPI_DLL" "$SERVE_DIR/StardewModdingAPI.dll"
echo "    SMAPI.dll copied to serve dir ($(stat -c '%s bytes' "$SERVE_DIR/StardewModdingAPI.dll"))"

# Verify dotnet.js exists
if [ ! -f "$SERVE_DIR/_framework/dotnet.js" ]; then
  echo "    ERROR: _framework/dotnet.js not found in serve dir"
  ls "$SERVE_DIR/_framework/" | head -10
  exit 2
fi
echo "    Serve dir ready: $SERVE_DIR"

# ── Step 4: Start HTTP server + Run headless Chrome ────────────────────────
echo ""
echo "[4/5] Starting HTTP server on :8766..."
PORT=8766
python3 -m http.server "$PORT" --directory "$SERVE_DIR" > "$PERSIST_DIR/http.log" 2>&1 &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null || true; pkill -f 'http.server $PORT' 2>/dev/null || true" EXIT

for i in {1..15}; do
  if curl -s "http://localhost:$PORT/" > /dev/null 2>&1; then
    echo "    Server ready at http://localhost:$PORT/"
    break
  fi
  sleep 0.5
done

echo ""
echo "[5/5] Running headless Chrome (60s budget)..."
CHROME=/home/z/.agent-browser/browsers/chrome-149.0.7827.115/chrome
CHROME_LOG="$PERSIST_DIR/chrome.log"
DOM_DUMP="$PERSIST_DIR/dom-dump.html"

"$CHROME" \
  --headless \
  --no-sandbox \
  --enable-logging=stderr \
  --v=1 \
  --virtual-time-budget=60000 \
  --timeout=120000 \
  --dump-dom \
  "http://localhost:$PORT/" \
  > "$DOM_DUMP" 2> "$CHROME_LOG" &
CHROME_PID=$!

for i in {1..150}; do
  if ! kill -0 $CHROME_PID 2>/dev/null; then
    break
  fi
  sleep 1
done
kill $CHROME_PID 2>/dev/null || true
wait $CHROME_PID 2>/dev/null || true
echo "    Chrome finished"

# ── Step 5: Analyze results ────────────────────────────────────────────────
echo ""
echo "=== Analyzing results ==="

ALL_LOG="$PERSIST_DIR/all.log"
cat "$CHROME_LOG" > "$ALL_LOG"
echo "" >> "$ALL_LOG"
echo "=== DOM DUMP ===" >> "$ALL_LOG"
cat "$DOM_DUMP" >> "$ALL_LOG"

# Extract on-page log div content (we proxy console.log into it)
# Use Python to extract the div content reliably (handles multi-line)
ONPAGE_LOG=$(python3 -c "
import re, sys
with open('$DOM_DUMP', 'r', encoding='utf-8') as f:
    html = f.read()
m = re.search(r'<div id=\"log\">(.*?)</div>', html, re.DOTALL)
if m:
    content = m.group(1)
    # HTML-decode common entities
    content = content.replace('&lt;', '<').replace('&gt;', '>').replace('&amp;', '&').replace('&quot;', '\"').replace('&#39;', \"'\")
    print(content)
" 2>&1)

# Look for PASS or FAIL marker
if echo "$ONPAGE_LOG" | grep -q "\[PASS\] SMAPI loaded successfully"; then
  echo ""
  echo "[PASS] SMAPI load PoC succeeded"
  echo ""
  echo "=== Captured log (key lines) ==="
  echo "$ONPAGE_LOG" | grep -E "\[\+\]|\[PASS\]|\[FAIL\]|\[!\]" | head -30
  echo ""
  echo "=== Full log saved to: $ALL_LOG ==="
  exit 0
elif echo "$ONPAGE_LOG" | grep -q "\[FAIL\]"; then
  echo ""
  echo "[FAIL] SMAPI load PoC failed"
  echo ""
  echo "=== Captured log ==="
  echo "$ONPAGE_LOG"
  echo ""
  echo "=== Full log saved to: $ALL_LOG ==="
  exit 1
else
  echo ""
  echo "[FAIL] Could not find PASS/FAIL marker in output. PoC may have timed out."
  echo ""
  echo "=== On-page log (last 30 lines) ==="
  echo "$ONPAGE_LOG" | tail -30
  echo ""
  echo "=== Console errors ==="
  grep -iE "error|exception|fail" "$CHROME_LOG" | grep -v -E "dbus|histogram|gpu_init|component_updater|VAAPI|media/gpu|certificate" | head -10
  echo ""
  echo "=== Full log saved to: $ALL_LOG ==="
  exit 1
fi
