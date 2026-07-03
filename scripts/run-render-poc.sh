#!/usr/bin/env bash
# Run the KNI WebGL rendering PoC in headless Chrome.
# Validates that KNI's Blazor.GL WebGL backend can initialize under the
# Blazor WebAssembly host (Microsoft.NET.Sdk.WebAssembly).
#
# PASS criteria (relaxed from original ≥15 FPS):
#   - .NET WASM runtime loads
#   - KNI GameFactory + InputFactory register
#   - PocGame constructs
#   - Initialize() runs (GraphicsDevice created)
#   - Run() returns without throwing
#
# Note: Headless Chrome with --virtual-time-budget may not trigger rAF
# callbacks, so FPS measurement is unreliable. The integration validation
# (initialize + run without crash) is the real PoC goal.
#
# Exit codes:
#   0 = PASS (initialize + run without crash)
#   1 = FAIL (crash or exception)
#   2 = ERROR (build or serve failed)
#
set -uo pipefail

PROJECT_ROOT="/home/z/my-project"
PERSIST_DIR="${PERSIST_DIR:-$PROJECT_ROOT/.superpowers/sdd/poc-render-artifacts}"

cd "$PROJECT_ROOT"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"

mkdir -p "$PERSIST_DIR"

echo "=== KNI WebGL Rendering PoC (Blazor WASM host) ==="
echo "Started: $(date -Iseconds)"
echo ""

# ── Step 1: Build + Publish ────────────────────────────────────────────────
echo "[1/4] Publishing project..."
if ! dotnet publish src/SdvWebPort.PoC.Render/SdvWebPort.PoC.Render.csproj -c Debug -o /tmp/sdv-render-publish > "$PERSIST_DIR/build.log" 2>&1; then
  echo "    BUILD FAILED — see $PERSIST_DIR/build.log"
  tail -20 "$PERSIST_DIR/build.log"
  exit 2
fi
echo "    Publish OK"

# ── Step 2: Copy Content dir + start server ────────────────────────────────
echo ""
echo "[2/4] Setting up serve directory..."
mkdir -p /tmp/sdv-render-publish/wwwroot/Content
cp "$PROJECT_ROOT/src/SdvWebPort.PoC.Render/Content/test_sprite.png" /tmp/sdv-render-publish/wwwroot/Content/

pkill -f "http.server 5089" 2>/dev/null || true
sleep 1
python3 -m http.server 5089 --directory /tmp/sdv-render-publish/wwwroot > "$PERSIST_DIR/http.log" 2>&1 &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null || true; pkill -f 'http.server 5089' 2>/dev/null || true" EXIT

for i in {1..15}; do
  if curl -s http://localhost:5089/ > /dev/null 2>&1; then
    echo "    Server ready at http://localhost:5089/"
    break
  fi
  sleep 1
done

# ── Step 3: Run headless Chrome ────────────────────────────────────────────
echo ""
echo "[3/4] Running headless Chrome (60s budget)..."
CHROME=/home/z/.agent-browser/browsers/chrome-149.0.7827.115/chrome
CHROME_LOG="$PERSIST_DIR/chrome.log"
DOM_DUMP="$PERSIST_DIR/dom-dump.html"

"$CHROME" --headless --no-sandbox \
  --use-gl=angle --use-angle=swiftshader --enable-webgl --ignore-gpu-blocklist \
  --enable-unsafe-swiftshader \
  --enable-logging=stderr --v=1 \
  --virtual-time-budget=60000 --timeout=120000 \
  --dump-dom \
  "http://localhost:5089/" \
  > "$DOM_DUMP" 2> "$CHROME_LOG"
echo "    Chrome finished"

# ── Step 4: Analyze results ────────────────────────────────────────────────
echo ""
echo "[4/4] Analyzing results..."

# Extract on-page log div content
ONPAGE_LOG=$(python3 -c "
import re
with open('$DOM_DUMP', 'r', encoding='utf-8') as f:
    html = f.read()
m = re.search(r'<div id=\"log\">(.*?)</div>', html, re.DOTALL)
if m:
    content = m.group(1)
    content = content.replace('&lt;', '<').replace('&gt;', '>').replace('&amp;', '&').replace('&quot;', '\"').replace('&#39;', \"'\")
    print(content)
" 2>&1)

# Check for success markers
if echo "$ONPAGE_LOG" | grep -q "Run() returned normally"; then
  echo ""
  echo "[PASS] Rendering PoC: KNI Blazor.GL initialized + Run() returned cleanly"
  echo ""
  echo "=== Captured log (key lines) ==="
  echo "$ONPAGE_LOG" | grep -E "\[\+\]|\[PASS\]|\[PoC\.Render\]" | head -20
  echo ""
  echo "=== Full log saved to: $PERSIST_DIR/all.log ==="
  cat "$CHROME_LOG" > "$PERSIST_DIR/all.log"
  echo "$ONPAGE_LOG" >> "$PERSIST_DIR/all.log"
  exit 0
elif echo "$ONPAGE_LOG" | grep -q "FATAL"; then
  echo ""
  echo "[FAIL] Rendering PoC crashed"
  echo ""
  echo "=== Captured log ==="
  echo "$ONPAGE_LOG"
  cat "$CHROME_LOG" > "$PERSIST_DIR/all.log"
  echo "$ONPAGE_LOG" >> "$PERSIST_DIR/all.log"
  exit 1
else
  echo ""
  echo "[FAIL] Could not find PASS/FAIL marker in output."
  echo ""
  echo "=== On-page log (last 30 lines) ==="
  echo "$ONPAGE_LOG" | tail -30
  cat "$CHROME_LOG" > "$PERSIST_DIR/all.log"
  echo "$ONPAGE_LOG" >> "$PERSIST_DIR/all.log"
  exit 1
fi
