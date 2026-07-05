# SdvWebPort PoC — SDV Load

Validates that the real, unmodified `Stardew Valley.dll` from a GOG install
can be loaded into the browser-side Blazor WebAssembly runtime, with all
MonoGame.Framework type references resolved via a TypeForwardedTo facade
that targets KNI.

## What this PoC proves

- The user's GOG `Stardew Valley.dll` can be byte-loaded into WASM
- All ~1900 SDV types resolve without errors
- The MonoGame.Framework → KNI forwarding works
- `StardewValley.Game1` base type chain resolves through KNI's `Game`
- `StardewValley.Program` is discoverable via reflection

## What this PoC does NOT do (yet)

- Does NOT call `Program.Main()` (Phase 2.5)
- Does NOT instantiate `Game1` (Phase 2.5)
- Does NOT render anything — this is a load-and-inspect PoC

## Before running

1. Locate your GOG Stardew Valley install (typically `~/GOG Games/Stardew Valley/`).
2. Copy `Stardew Valley.dll` into this directory:

   ```bash
   cp "/path/to/Stardew Valley/Stardew Valley.dll" .
   ```

3. The file is gitignored and will NOT be committed.

## Running

```bash
cd /home/z/my-project
./scripts/run-sdv-load-poc.sh
```

Then open http://localhost:8000/ in a Chromium-based browser. The on-page
log shows the entire load + enumeration sequence.

## Success criteria

The on-page log ends with:

```
[PASS] All 6 known SDV entry types resolved!
[PASS] MonoGame.Framework -> KNI facade pattern WORKS.
[NEXT] Ready for Phase 2.5: invoke Program.Main() or instantiate Game1.
```

## Troubleshooting

**"Could not fetch Stardew Valley.dll"** — the file is missing from
`src/SdvWebPort.PoC.SdvLoad/` (or `wwwroot/` after the run script copies it).

**"MonoGame.Framework facade assembly not found"** — the project file is
missing the `ProjectReference` to `MonoGame.Framework.Facade.csproj`. Run
`dotnet restore` and rebuild.

**"Partial load: N OK, M failed"** — some SDV types reference types not in
the facade. Run `scripts/generate-facade-types.sh` to refresh the facade
list, or check the loader exceptions to see which assembly SDV is asking
for that we don't provide.

## Architecture

```
Browser fetches "Stardew Valley.dll" via HTTP
  ↓
AssemblyLoadContext.LoadFromStream(bytes)
  ↓
SDV's AssemblyRef "MonoGame.Framework, v3.8.0.1641" → ALC.Load() callback
  ↓
Returns the MonoGame.Framework.Facade assembly (same name + version)
  ↓
CLR resolves types via TypeForwardedTo metadata
  ↓
Type lookups routed to Xna.Framework / .Game / .Graphics / .Content / .Input (KNI)
```

See `src/MonoGame.Framework.Facade/README.md` for details on the facade.
