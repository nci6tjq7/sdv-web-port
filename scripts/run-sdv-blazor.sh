#!/usr/bin/env bash
# Build + serve SdvBlazor PoC on http://localhost:8765/
# Then optionally run headless test.
set -euo pipefail
export PATH="/home/z/.dotnet:$PATH"
export DOTNET_ROOT="/home/z/.dotnet"
cd /home/z/my-project

POC_DIR="src/SdvWebPort.PoC.SdvBlazor"
PUBLISH_DIR="$POC_DIR/bin/Release/net8.0/publish/wwwroot"

# Copy real SDV DLL + Content into publish dir (these are not in repo)
if [ -f "/tmp/sdv-extract/Stardew Valley/Stardew Valley.dll" ]; then
    cp "/tmp/sdv-extract/Stardew Valley/Stardew Valley.dll" "$PUBLISH_DIR/"
    echo "[+] Copied Stardew Valley.dll"
else
    echo "[!] SDV DLL not found at /tmp/sdv-extract/"
    exit 1
fi

# Copy Content dir (XNB files) — only if missing or stale
if [ ! -d "$PUBLISH_DIR/deps/content" ] || [ -z "$(ls -A "$PUBLISH_DIR/deps/content" 2>/dev/null)" ]; then
    mkdir -p "$PUBLISH_DIR/deps/content"
    if [ -d "/tmp/sdv-extract/Stardew Valley/Content" ]; then
        cp -r "/tmp/sdv-extract/Stardew Valley/Content/"* "$PUBLISH_DIR/deps/content/" 2>/dev/null || true
        echo "[+] Copied Content to deps/content/"
    fi
fi

# Start a static HTTP server (Python)
echo "[+] Serving $PUBLISH_DIR on http://localhost:8765/"
cd "$PUBLISH_DIR"
python3 -m http.server 8765 --bind 127.0.0.1 >/tmp/sdv-blazor-server.log 2>&1 &
SERVER_PID=$!
echo "[+] Server PID: $SERVER_PID"

# Wait for server
sleep 1

# Run headless test if requested
if [ "${1:-}" = "--test" ]; then
    echo "[+] Running headless test..."
    shift
    node /home/z/my-project/scripts/test-phase28-diagnostic.js 8765 "$@"
    kill $SERVER_PID 2>/dev/null || true
else
    echo "[+] Server running. Press Ctrl+C to stop."
    echo "[+] Open: http://localhost:8765/"
    wait $SERVER_PID
fi
