#!/usr/bin/env bash
set -uo pipefail
PROJECT_ROOT="/home/z/my-project"
PERSIST_DIR="$PROJECT_ROOT/.superpowers/sdd/poc-font-artifacts"
cd "$PROJECT_ROOT"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
mkdir -p "$PERSIST_DIR"

echo "=== Phase 1c Font + Content Smoke Test ==="

echo "[1/3] Publishing..."
dotnet publish src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj -c Debug -o "$PERSIST_DIR/publish" > "$PERSIST_DIR/build.log" 2>&1 || { echo "BUILD FAILED"; tail -20 "$PERSIST_DIR/build.log"; exit 2; }
echo "    Publish OK"

echo "[2/3] Starting server..."
pkill -f "http.server 5089" 2>/dev/null || true
sleep 1
python3 -m http.server 5089 --directory "$PERSIST_DIR/publish/wwwroot" > "$PERSIST_DIR/http.log" 2>&1 &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null || true" EXIT
for i in $(seq 1 15); do curl -s http://localhost:5089/ >/dev/null 2>&1 && break; sleep 1; done

echo "[3/3] Running Chrome..."
CHROME=${CHROME:-/home/z/.agent-browser/browsers/chrome-149.0.7827.115/chrome}
timeout 90 "$CHROME" --headless --no-sandbox \
  --use-gl=angle --use-angle=swiftshader --enable-webgl --ignore-gpu-blocklist \
  --enable-unsafe-swiftshader --enable-logging=stderr --v=1 \
  --virtual-time-budget=60000 --dump-dom \
  "http://localhost:5089/" > "$PERSIST_DIR/dom.html" 2> "$PERSIST_DIR/chrome.log"

if grep -q "VFS capabilities" "$PERSIST_DIR/chrome.log" 2>/dev/null; then
  echo "[PASS] Font smoke test: runtime + VFS + content + font renderer loaded"
  grep "VFS capabilities" "$PERSIST_DIR/chrome.log" | head -1
  exit 0
else
  echo "[FAIL] Font smoke test"
  grep "INFO:CONSOLE" "$PERSIST_DIR/chrome.log" | head -5
  exit 1
fi
