#!/usr/bin/env bash
# Smoke test: verify the published SdvLoad PoC bundle has the right structure
# without requiring an actual browser. Checks file existence + sizes.
set -euo pipefail
cd "$(dirname "$0")/.."

PUBLISH_DIR="src/SdvWebPort.PoC.SdvLoad/bin/Debug/net10.0/publish/wwwroot"

if [ ! -d "$PUBLISH_DIR" ]; then
    echo "[!] Publish dir not found: $PUBLISH_DIR"
    echo "    Run: dotnet publish src/SdvWebPort.PoC.SdvLoad -c Debug"
    exit 1
fi

echo "[+] Checking bundle structure in $PUBLISH_DIR..."

PASS=0
FAIL=0

check() {
    local desc="$1"
    local path="$2"
    local min_size="${3:-1024}"
    if [ -f "$path" ] && [ "$(stat -c%s "$path")" -ge "$min_size" ]; then
        echo "  PASS: $desc ($(stat -c%s "$path") bytes)"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $desc (path=$path)"
        FAIL=$((FAIL + 1))
    fi
}

check "index.html" "$PUBLISH_DIR/index.html" 100
check "main.js" "$PUBLISH_DIR/main.js" 100
check "_framework/dotnet.js (entry module)" "$PUBLISH_DIR/_framework/dotnet.js" 1000
check "_framework/dotnet.native.*.wasm (WASM binary)" "$(ls "$PUBLISH_DIR/_framework/dotnet.native."*.wasm | head -1)" 1000000
check "_framework/dotnet.runtime.*.js (runtime JS)" "$(ls "$PUBLISH_DIR/_framework/dotnet.runtime."*.js | grep -v ".map\|.br\|.gz" | head -1)" 1000
check "_framework/MonoGame.Framework.*.wasm (facade)" "$(ls "$PUBLISH_DIR/_framework/MonoGame.Framework."*.wasm | head -1)" 1024
check "_framework/SdvWebPort.PoC.SdvLoad.*.wasm (PoC)" "$(ls "$PUBLISH_DIR/_framework/SdvWebPort.PoC.SdvLoad."*.wasm | head -1)" 1024

# Optional: Stardew Valley.dll is required for runtime but not for build
if [ -f "$PUBLISH_DIR/Stardew Valley.dll" ]; then
    check "Stardew Valley.dll (user-supplied)" "$PUBLISH_DIR/Stardew Valley.dll" 100000
else
    echo "  SKIP: Stardew Valley.dll (not present — copy from GOG install to actually run the PoC)"
fi

echo ""
echo "[+] Result: $PASS passed, $FAIL failed"
exit $FAIL
