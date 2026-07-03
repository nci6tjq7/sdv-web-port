#!/usr/bin/env bash
# Idempotent .NET 10 SDK installer for Debian/Ubuntu sandbox.
#
# Installs the .NET SDK to $HOME/.dotnet using Microsoft's official
# dotnet-install.sh script. Safe to re-run: skips if .NET 10 is already
# present at the install location.
set -euo pipefail

DOTNET_VERSION="10.0.100"
INSTALL_DIR="$HOME/.dotnet"

if [ -x "$INSTALL_DIR/dotnet" ] && "$INSTALL_DIR/dotnet" --version | grep -q "^10\."; then
  echo "[+] .NET 10 SDK already installed at $INSTALL_DIR"
  echo "    $("$INSTALL_DIR/dotnet" --version)"
  exit 0
fi

echo "[+] Installing .NET $DOTNET_VERSION SDK to $INSTALL_DIR"
mkdir -p "$INSTALL_DIR"
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

# Use Microsoft's dotnet-install script
curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o "$TMP_DIR/dotnet-install.sh"
chmod +x "$TMP_DIR/dotnet-install.sh"
"$TMP_DIR/dotnet-install.sh" --version "$DOTNET_VERSION" --install-dir "$INSTALL_DIR"

# Sanity check the install actually produced a working dotnet binary.
if [ ! -x "$INSTALL_DIR/dotnet" ]; then
  echo "[!] Install completed but $INSTALL_DIR/dotnet not found/executable" >&2
  exit 1
fi

echo "[+] Done. Installed:"
echo "    $($INSTALL_DIR/dotnet --version)"
echo "[+] Add to PATH (also done automatically by verify-environment.sh):"
echo "    export PATH=\"$INSTALL_DIR:\$PATH\""
echo "    export DOTNET_ROOT=\"$INSTALL_DIR\""
