# SMAPI Load PoC

## Before running

1. Locate your SMAPI installation (typically `Stardew Valley/` next to your GOG install).
2. Copy `StardewModdingAPI.dll` to this directory:

```bash
cp "/path/to/Stardew Valley/StardewModdingAPI.dll" .
```

3. The file is gitignored and will NOT be committed.

## Running

From project root:

```bash
./scripts/run-smapi-poc.sh
```

## What it validates

- .NET 10 WASM runtime can `AssemblyLoadContext.LoadFromStream` a real SMAPI.dll
- Reflection works: manifest reading, type enumeration, method discovery
- No runtime crash from SMAPI's static initializers or assembly-level attributes

Success criteria: `[PASS] SMAPI loaded successfully` in console output.
