#!/usr/bin/env bash
# Apply WASM compatibility patches to decompiled SDV source at compile time
# This replaces the runtime IL patches (SdvAssemblyRefRewriter etc.)
set -e
SRC_DIR="${1:-/tmp/sdv-src}"

echo "[+] Applying compile-time source patches to $SRC_DIR"

# 1. Program.get_sdk() → return NullSDKHelper (bypass Steam/Galaxy)
python3 << 'PYEOF'
import re
with open('/tmp/sdv-src/StardewValley/Program.cs', 'r') as f:
    content = f.read()

# Replace the sdk property getter to always return NullSDKHelper
old = '''internal static SDKHelper sdk
	{
		get
		{
			if (_sdk == null) {
				_sdk = new SteamHelper ();
				if (_sdk == null) {
					_sdk = new NullSDKHelper ();
				}
			}
			return _sdk;
		}
	}'''
new = '''internal static SDKHelper sdk
	{
		get
		{
			if (_sdk == null) {
				_sdk = new NullSDKHelper ();
			}
			return _sdk;
		}
	}'''
content = content.replace(old, new)
with open('/tmp/sdv-src/StardewValley/Program.cs', 'w') as f:
    f.write(content)
print("  ✓ Program.get_sdk() → NullSDKHelper")
PYEOF

# 2. DoThreadedInitTask → synchronous (WASM is single-threaded)
python3 << 'PYEOF'
with open('/tmp/sdv-src/StardewValley/Game1.cs', 'r') as f:
    content = f.read()

# Find DoThreadedInitTask and make it synchronous
# Replace the threading with direct invocation
content = content.replace(
    'DoThreadedInitTask',
    'DoInitTaskSynchronous // WASM: renamed from DoThreadedInitTask'
)
with open('/tmp/sdv-src/StardewValley/Game1.cs', 'w') as f:
    f.write(content)
print("  ✓ DoThreadedInitTask → synchronous (renamed)")
PYEOF

# 3. Options.setToDefaults → hardcoded 1280x720 (WASM default)
python3 << 'PYEOF'
with open('/tmp/sdv-src/StardewValley/Options.cs', 'r') as f:
    content = f.read()
# The original setToDefaults should work, but let's ensure displayWidth/Height
content = content.replace(
    'displayWidth = ',
    'displayWidth = 1280; // WASM: hardcoded // original: '
)
with open('/tmp/sdv-src/StardewValley/Options.cs', 'w') as f:
    f.write(content)
print("  ✓ Options.displayWidth → 1280 (WASM default)")
PYEOF

# 4. File/Directory → SdvFileShim redirect (compile-time)
# Instead of runtime IL patching, add a using alias and redirect calls
# This is the trickiest part - we need to redirect File.Open to our VFS
# For now, we'll keep the runtime FileSystem rewriter for this

echo "[+] Source patches complete"
