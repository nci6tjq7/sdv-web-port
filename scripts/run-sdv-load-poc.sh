#!/usr/bin/env bash
# Build the SdvLoad PoC and serve it on http://localhost:8000/
#
# Usage:
#   1. Copy your GOG "Stardew Valley.dll" into src/SdvWebPort.PoC.SdvLoad/
#   2. Run: ./scripts/run-sdv-load-poc.sh
#   3. Open http://localhost:8000/ in a Chromium browser
set -euo pipefail
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
cd "$(dirname "$0")/.."

POC_DIR="src/SdvWebPort.PoC.SdvLoad"
PUBLISH_DIR="$POC_DIR/bin/Debug/net10.0/publish"
WWWROOT_DIR="$PUBLISH_DIR/wwwroot"

echo "[+] Publishing SdvLoad PoC..."
dotnet publish "$POC_DIR" -c Debug 2>&1 | tail -5

echo "[+] Verifying Stardew Valley.dll is present..."
if [ ! -f "$POC_DIR/Stardew Valley.dll" ]; then
    echo "[!] Stardew Valley.dll not found in $POC_DIR/"
    echo "    Copy it from your GOG install:"
    echo "    cp \"/path/to/Stardew Valley/Stardew Valley.dll\" $POC_DIR/"
    exit 1
fi

# Copy the SDV DLL into the served wwwroot so the WASM runtime can fetch it
cp "$POC_DIR/Stardew Valley.dll" "$WWWROOT_DIR/Stardew Valley.dll"

# Copy the fingerprinted dotnet.*.js (which has the boot config inlined
# when <WasmInlineBootConfig>true</WasmInlineBootConfig> is set) to the
# non-fingerprinted dotnet.js path so main.js's
# `import { dotnet } from './_framework/dotnet.js'` resolves.
FRAMEWORK_DIR="$WWWROOT_DIR/_framework"
if [ ! -f "$FRAMEWORK_DIR/dotnet.js" ]; then
    # Find the fingerprinted dotnet.js (NOT dotnet.native.*.js or dotnet.runtime.*.js).
    # The fingerprinted entry module is named like "dotnet.<hash>.js" (no other suffix).
    FINGERPRINTED=$(ls "$FRAMEWORK_DIR"/dotnet.*.js 2>/dev/null | grep -v "dotnet.native\|dotnet.runtime\|dotnet.boot" | head -1 || true)
    if [ -n "$FINGERPRINTED" ]; then
        cp "$FINGERPRINTED" "$FRAMEWORK_DIR/dotnet.js"
        echo "[+] Copied $(basename "$FINGERPRINTED") -> dotnet.js (inlined boot config)"
    else
        echo "[!] Could not find fingerprinted dotnet.*.js to copy as dotnet.js"
        echo "    Framework dir contents:"
        ls "$FRAMEWORK_DIR" | grep "^dotnet" | head -10
        exit 1
    fi
fi

# Copy the facade assembly (MonoGame.Framework.<hash>.wasm) to a stable
# non-fingerprinted path so Program.cs can fetch it via HttpClient.
# This avoids needing to parse the staticwebassets.endpoints.json at runtime.
FACADE_FP=$(ls "$FRAMEWORK_DIR"/MonoGame.Framework.*.wasm 2>/dev/null | grep -v "\.br\|\.gz" | head -1 || true)
if [ -n "$FACADE_FP" ]; then
    cp "$FACADE_FP" "$WWWROOT_DIR/MonoGame.Framework.wasm"
    echo "[+] Copied facade: $(basename "$FACADE_FP") -> MonoGame.Framework.wasm"
else
    echo "[!] Could not find MonoGame.Framework.*.wasm in $FRAMEWORK_DIR"
    exit 1
fi

# Sanity check: list the served directory tree
echo "[+] Served files (top of wwwroot):"
ls "$WWWROOT_DIR" | head -5
echo "[+] _framework contains $(ls "$WWWROOT_DIR/_framework" | wc -l) files"

PORT="${1:-8000}"
echo "[+] Serving on http://localhost:$PORT/ (Ctrl+C to stop)..."
cd "$WWWROOT_DIR"
exec python3 -m http.server "$PORT"
