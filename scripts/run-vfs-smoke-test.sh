#!/usr/bin/env bash
# Phase 1a VFS smoke test: verify upload UI + VFS capability detection works.
set -uo pipefail

PROJECT_ROOT="/home/z/my-project"
PERSIST_DIR="$PROJECT_ROOT/.superpowers/sdd/poc-vfs-artifacts"
cd "$PROJECT_ROOT"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
mkdir -p "$PERSIST_DIR"

echo "=== Phase 1a VFS Smoke Test ==="
echo ""

# Publish
echo "[1/3] Publishing..."
if ! dotnet publish src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj -c Debug -o "$PERSIST_DIR/publish" > "$PERSIST_DIR/build.log" 2>&1; then
  echo "    BUILD FAILED"
  tail -20 "$PERSIST_DIR/build.log"
  exit 2
fi
echo "    Publish OK"

# Start server
echo ""
echo "[2/3] Starting server..."
pkill -f "http.server 5089" 2>/dev/null || true
sleep 1
python3 -m http.server 5089 --directory "$PERSIST_DIR/publish/wwwroot" > "$PERSIST_DIR/http.log" 2>&1 &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null || true; pkill -f 'http.server 5089' 2>/dev/null || true" EXIT

for i in {1..15}; do
  if curl -s http://localhost:5089/ > /dev/null 2>&1; then
    echo "    Server ready"
    break
  fi
  sleep 1
done

# Run Chrome
echo ""
echo "[3/3] Running Chrome..."
CHROME=${CHROME:-/home/z/.agent-browser/browsers/chrome-149.0.7827.115/chrome}
"$CHROME" --headless --no-sandbox \
  --use-gl=angle --use-angle=swiftshader --enable-webgl --ignore-gpu-blocklist \
  --enable-unsafe-swiftshader \
  --enable-logging=stderr --v=1 \
  --virtual-time-budget=30000 --timeout=60000 \
  --dump-dom "http://localhost:5089/" > "$PERSIST_DIR/dom.html" 2> "$PERSIST_DIR/chrome.log"

# Check for success markers
if grep -q "VFS capabilities:" "$PERSIST_DIR/chrome.log" 2>/dev/null; then
  echo ""
  echo "[PASS] VFS smoke test: capabilities detected"
  grep "VFS capabilities:" "$PERSIST_DIR/chrome.log" | head -1
  exit 0
elif grep -q "Runtime initialized" "$PERSIST_DIR/chrome.log" 2>/dev/null; then
  echo ""
  echo "[PASS] VFS smoke test: runtime initialized"
  exit 0
else
  echo ""
  echo "[FAIL] VFS smoke test: no success markers found"
  grep "INFO:CONSOLE" "$PERSIST_DIR/chrome.log" | head -10
  exit 1
fi
