#!/usr/bin/env bash
# Pre-flight environment check for the Stardew Valley web port.
#
# Verifies that the toolchain required for Phase 0 (.NET 10 SDK, Node 20+,
# npm 10+) is on PATH and at-or-above the minimum version. Exits non-zero
# if any required check FAILs. Missing Chrome/Chromium is a WARN only,
# since headless browser testing arrives later in the plan.
set -euo pipefail

# ---------------------------------------------------------------------------
# Auto-discover a locally installed .NET SDK if it isn't on PATH yet.
# The install-dotnet.sh script places the SDK in $HOME/.dotnet. Make this
# verifier work when run from a fresh shell that hasn't sourced the PATH.
# ---------------------------------------------------------------------------
if ! command -v dotnet >/dev/null 2>&1; then
  if [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
    export DOTNET_ROOT="$HOME/.dotnet"
  fi
fi

PASS=0
FAIL=0
WARN=0

# version_ge actual min  -> returns 0 (true) if actual >= min (numeric)
# Uses `sort -V` for proper semver ordering (avoids lexicographic pitfalls
# like "9.0.0" > "10.0.100").
version_ge() {
  local actual="$1" min="$2"
  [ -n "$actual" ] || return 1
  [ "$(printf '%s\n%s\n' "$min" "$actual" | sort -V | tail -n1)" = "$actual" ]
}

check_version() {
  local name="$1" cmd="$2" min_version="$3"
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "  [FAIL] $name: not on PATH"
    FAIL=$((FAIL+1))
    return
  fi
  local actual
  actual=$("$cmd" --version 2>&1 | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)
  if [ -z "$actual" ]; then
    echo "  [FAIL] $name: could not parse version from '$cmd --version'"
    FAIL=$((FAIL+1))
    return
  fi
  if version_ge "$actual" "$min_version"; then
    echo "  [PASS] $name: $actual (>= $min_version)"
    PASS=$((PASS+1))
  else
    echo "  [FAIL] $name: expected >= $min_version, got $actual"
    FAIL=$((FAIL+1))
  fi
}

echo "=== Environment Verification ==="
check_version ".NET SDK" "dotnet" "10.0.100"
check_version "Node.js" "node" "20.0.0"
check_version "npm"     "npm"  "10.0.0"

# Chrome check (for later E2E). WARN, not FAIL, when absent.
if command -v google-chrome >/dev/null 2>&1; then
  echo "  [PASS] Chrome: $(google-chrome --version 2>&1)"
  PASS=$((PASS+1))
elif command -v chromium >/dev/null 2>&1; then
  echo "  [PASS] Chromium: $(chromium --version 2>&1)"
  PASS=$((PASS+1))
elif command -v chromium-browser >/dev/null 2>&1; then
  echo "  [PASS] Chromium: $(chromium-browser --version 2>&1)"
  PASS=$((PASS+1))
else
  echo "  [WARN] Chrome/Chromium not found (needed for Phase 0 PoC testing)"
  WARN=$((WARN+1))
fi

echo ""
echo "Result: $PASS passed, $FAIL failed, $WARN warnings"
[ "$FAIL" -eq 0 ]
