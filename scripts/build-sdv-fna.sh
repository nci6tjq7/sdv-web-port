#!/usr/bin/env bash
set -e
export PATH="/home/z/.dotnet:$PATH"
export DOTNET_ROOT="/home/z/.dotnet"
export DOTNET_ROLL_FORWARD=LatestMajor

SDV_DIR="/tmp/sdv-extract/Stardew Valley"
SRC_DIR="/tmp/sdv-fna-src"

echo "=== [1/4] Decompile SDV ==="
dotnet tool install --global ilspycmd --version 8.2.0.7535 2>/dev/null || true
rm -rf "$SRC_DIR" && mkdir -p "$SRC_DIR"
dotnet /home/z/.dotnet/tools/.store/ilspycmd/8.2.0.7535/ilspycmd/8.2.0.7535/tools/net6.0/any/ilspycmd.dll \
  "$SDV_DIR/Stardew Valley.dll" -p -o "$SRC_DIR" 2>&1 | tail -2
rm -f "$SRC_DIR/Stardew Valley.csproj"

echo "=== [2/4] Clone FNA ==="
[ ! -d /tmp/FNA ] && git clone --depth 1 https://github.com/FNA-XNA/FNA.git /tmp/FNA
cd /tmp/FNA && git submodule update --init --recursive 2>/dev/null || true

echo "=== [3/4] Apply fixes and create csproj ==="
# (Fixes are applied by patch-sdv-fna.sh)
bash /home/z/my-project/scripts/patch-sdv-fna.sh "$SRC_DIR"

echo "=== [4/4] Build ==="
cd "$SRC_DIR"
rm -rf bin obj
dotnet build
echo "=== Errors: $(dotnet build 2>&1 | grep 'error CS' | wc -l) ==="
ls -la "bin/Debug/net8.0/Stardew Valley.dll"
