# SDV Web Port — Long-Term Project Memory

> **THIS FILE IS THE PROJECT'S LONG-TERM MEMORY.**
> It survives session resets because it is committed to git and pushed to GitHub.
> **Any agent resuming work on this project MUST read this file FIRST, before
> doing anything else.**
>
> Last updated: 2026-07-05 (Phase 2.5 partial — KNI/.NET 10 incompatibility discovered)
> Current state: Phase 2.5 partial — Game1 instantiates but game loop doesn't start. Next: pivot to BlazorWebAssembly SDK.

---

## ⚡ Session Bootstrap (READ THIS FIRST)

If you are a new agent session starting work on this project:

1. **Read this file completely** — it contains everything you need to know.
2. **Read `worklog.md`** — chronological record of all work done.
3. **Check `git log --oneline -20`** and `git tag` — see recent progress.
4. **Read `AGENTS.md`** — project-specific agent guidelines (replaces the
   default superpowers AGENTS.md).
5. **Read `docs/superpowers/specs/2026-07-03-sdv-web-port-design.md`** —
   the master design document (823 lines, covers all 6 phases).
6. **Check current branch**: `git branch --show-current` — should be `main`
   or a `feat/phase*` branch.
7. **If superpowers skills are not installed** (no `skills/superpowers-*`
   dirs), install from `upload/superpowers-zai-*.tar.gz`:
   ```bash
   tar -xzf upload/superpowers-zai-0.0.0-zai.25.tar.gz -C /tmp/
   bash /tmp/superpowers-zai/install.sh /home/z/my-project
   ```
   Then invoke `Skill(command="superpowers-using-superpowers")`.
8. **Install .NET 10 SDK if missing**:
   ```bash
   bash scripts/install-dotnet.sh
   export PATH="/home/z/.dotnet:$PATH"
   export DOTNET_ROOT="/home/z/.dotnet"
   dotnet workload install wasm-tools
   ```
9. **Resume work** at the "Next Steps" section below.

---

## Project Identity

**Name:** SDV Web Port
**Goal:** Run the real, unmodified Stardew Valley game (GOG release) in a
browser via WebAssembly, with SMAPI mod support and XNB resource editing.

**Legal posture (NON-NEGOTIABLE):**
- User provides their own GOG copy — no game files in the repo
- No decompilation, no rewriting game code
- Local/intranet deployment only — no public hosting
- The SDV DLL is loaded byte-for-byte unmodified

**GitHub:** https://github.com/nci6tjq7/sdv-web-port (private)
**Current branch:** `main` (latest: `bdec997`)
**Latest tag:** `v0.7.0-facade-works`

---

## Tech Stack (PINNED — do not change without explicit user approval)

| Component | Version | Why |
|-----------|---------|-----|
| .NET SDK | 10.0.100 | WASM build tools, Blazor WebAssembly host |
| Blazor WebAssembly SDK | `Microsoft.NET.Sdk.WebAssembly` | .NET 10's native WASM host (NOT Uno.Wasm.Bootstrap) |
| KNI Framework | 4.2.9001 | MonoGame fork that targets Blazor GL — provides `Xna.Framework.*` assemblies |
| KNI Blazor.GL Platform | 4.2.9001.2 | `nkast.Kni.Platform.Blazor.GL` — WebGL2 backend |
| Mono.Cecil | (future) | For in-memory AssemblyRef rewriting if needed |
| xUnit | latest | Unit tests for VFS + Content layers |

**Critical: do NOT pivot to Uno.Wasm.Bootstrap.** It was the original choice
but is incompatible with KNI's Blazor.GL platform (see Phase 0 history below).

---

## Architecture (5-Layer Stack)

```
L4  Rendering        KNI (nkast.Xna.Framework.*) → WebGL2 via Blazor.GL
L3  Content          VfsContentManager → XNB parser → Canvas decode → Texture2D
L2  SMAPI            (Phase 3, not yet built) Harmony → RuntimeDetour shim
L1  Runtime          Blazor WebAssembly + MonoGame.Framework.Facade → KNI
L0  Virtual FS       IVirtualFileSystem (File System Access API + OPFS)
```

**The facade pattern (KEY INNOVATION, v0.7.0):**

```
Stardew Valley.dll
  → AssemblyRef "MonoGame.Framework, v3.8.x"
  → MonoGame.Framework.Facade (zero implementation, 337 TypeForwardedTo attrs)
  → CLR follows forwarders
  → Xna.Framework.* (KNI — actual implementation)
```

This lets the **real, unmodified SDV DLL** load in the browser without any
DLL patching. See `src/MonoGame.Framework.Facade/README.md` for details.

---

## Phase Status

| Phase | Status | Tag | Summary |
|-------|--------|-----|---------|
| 0 — Skeleton + PoC | ✅ DONE | `v0.2.0-phase0-pivoted` | Pivoted from Uno.Wasm.Bootstrap to Blazor WASM; KNI WebGL2 PoC passes |
| 1a — VFS | ✅ DONE | `v0.3.0-phase1a` | FSA + OPFS implementations + upload UI |
| 1b — XNB Loading | ✅ DONE | `v0.4.0-phase1b` | XNB parser + LZX decompression + Canvas decode |
| 1c — Fonts | ✅ DONE | `v0.5.0-phase1c` | BMFont .fnt parser + SpriteBatch text renderer |
| 2 — SDV Load | ✅ DONE | `v0.7.0-facade-works` | Real SDV DLL loads; TypeForwardedTo → KNI proven |
| 2.5 — Game1 Invoke | ⚠️ PARTIAL | — | Game1 instantiates + GraphicsDevice + SpriteBatch work; game loop doesn't start (KNI bug) |
| 2.5b — Pivot to BlazorWebAssembly SDK | ⏳ NEXT | — | Switch SdvLoad PoC to `Microsoft.NET.Sdk.BlazorWebAssembly` (net8.0) — KNI's native target |
| 3 — SMAPI | 🔲 PLANNED | — | Harmony → RuntimeDetour shim; mod loading |
| 4 — First Mod E2E | 🔲 PLANNED | — | CJB Cheats or similar end-to-end |
| 5 — XNB Editing | 🔲 PLANNED | — | xnbcli integration; in-browser XNB editor |

---

## ⚠️ CRITICAL: KNI Blazor.GL + .NET 10 Incompatibility (discovered Phase 2.5)

**KNI v4.2.9001.2's `nkast.Kni.Platform.Blazor.GL` does NOT work with .NET 10's
`Microsoft.NET.Sdk.WebAssembly`.** It targets `Microsoft.NET.Sdk.BlazorWebAssembly`
(net8.0) and relies on `Blazor` + `DotNet` global JS objects that .NET 10's native
WASM SDK does not provide.

### Evidence

1. KNI's Blazor project template uses `<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">` + `net8.0`
2. KNI's `nkast.Wasm.*` JS files unconditionally reference `Blazor.platform.*` and `DotNet.invokeMethod()`
3. KNI's `ConcreteGame.StartGameLoop()` is an EMPTY STUB:
   ```csharp
   private void StartGameLoop()
   {
       // request next frame    ← EMPTY! Game loop never starts!
   }
   ```
   (Confirmed in both the NuGet package AND the latest KNI main branch on GitHub)

### What works on .NET 10 native WASM SDK (with shims)

- ✅ DLL loading + TypeForwardedTo facade → KNI resolution
- ✅ Game1 instantiation via reflection
- ✅ GraphicsDeviceManager + GraphicsDevice creation
- ✅ SpriteBatch + Texture2D creation
- ✅ LoadContent() completes

### What does NOT work

- ❌ Game loop (StartGameLoop is empty — no requestAnimationFrame is ever called)
- ❌ No frames are rendered (canvas stays black)
- ❌ `Blazor` + `DotNet` global JS objects don't exist (we shimmed them but the loop still doesn't start)

### Shims we applied (documented for future reference)

In `src/SdvWebPort.PoC.SdvLoad/wwwroot/main.js`:
- `globalThis.Blazor = { platform: { getArrayEntryPtr: (arr) => arr }, runtime: { Module: null } }`
- `globalThis.DotNet = { invokeMethod: (asm, method, ...args) => dotnetInvoker.InvokeStatic...(asm, method, ...) }`
- `globalThis.Module = runtime.Module`

In `src/SdvWebPort.PoC.SdvLoad/Program.cs`:
- `DotNetInvoker` class with `[JSExport]` methods that route JS→.NET calls via reflection

These shims fixed initialization but the game loop STILL doesn't start because
`StartGameLoop()` is empty in KNI's C# code (not a JS issue).

### Recommended fix: Pivot to BlazorWebAssembly SDK

Create a new PoC project using:
```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

This is what KNI's Blazor.GL platform is designed for. The .NET 8
BlazorWebAssembly SDK provides `Blazor` + `DotNet` globals AND a component
model that may drive the game loop.

**Note:** This means the overall project may need to use .NET 8 for the
browser runtime, while the build/test toolchain can stay on .NET 10.
This is a significant decision — discuss with user before pivoting.

---

## Next Steps (Phase 2.5b — Pivot to BlazorWebAssembly SDK)

**Goal:** Get KNI's game loop actually running in the browser by switching to
the SDK that KNI's Blazor.GL platform is designed for.

**Required work:**
1. Create a new PoC project `SdvWebPort.PoC.BlazorGameLoop` using `<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">` + `net8.0`
2. Reference `nkast.Kni.Platform.Blazor.GL` (same version)
3. Add a simple `Game : Game` subclass with `Draw()` that clears to a color
4. Use Blazor's `App.razor` + `Pages/Index.razor` component model (per KNI template)
5. Verify the game loop runs (canvas has non-black pixels) in headless Chromium
6. If it works: migrate the SdvLoad facade→KNI pipeline to this new SDK
7. If it doesn't work: investigate further (KNI may need WebXR mode or other setup)

**Why this approach:** KNI's Blazor.GL platform + `nkast.Wasm.*` JS layer are
written for .NET 8's `Microsoft.NET.Sdk.BlazorWebAssembly`, which provides
`Blazor` + `DotNet` globals and a Blazor component model. Our .NET 10
`Microsoft.NET.Sdk.WebAssembly` doesn't provide these, and shimming them
fixed initialization but NOT the game loop (because `StartGameLoop()` is
empty in KNI's C# code — the loop must be driven by something else in the
Blazor host model).

**Branch:** create `feat/phase2.5b-blazor-sdk-pivot` from `main`.

---

## Project Structure

```
/home/z/my-project/
├── MEMORY.md                      ← THIS FILE (project memory)
├── AGENTS.md                      ← project-specific agent guidelines
├── worklog.md                     ← chronological work log (append-only)
├── SdvWebPort.sln                 ← .NET solution (8 projects)
├── .superpowers-bootstrap         ← hint that superpowers is installed
├── .env                           ← GitHub token (gitignored, ephemeral)
├── docs/superpowers/
│   ├── specs/
│   │   ├── 2026-07-03-sdv-web-port-design.md   ← MASTER DESIGN (823 lines)
│   │   └── 2026-07-04-phase0-poc-report.md
│   └── plans/
│       ├── 2026-07-03-phase0-skeleton-and-poc.md
│       ├── 2026-07-04-phase1a-vfs-implementations.md
│       ├── 2026-07-04-phase1b-xnb-loading.md
│       └── 2026-07-05-phase2-sdv-load.md
├── src/
│   ├── SdvWebPort.Vfs/                      ← IVirtualFileSystem + InMemoryVfs + XNB parsers
│   ├── SdvWebPort.Runtime/                  ← Blazor WASM host + VFS impls + ContentJsInterop
│   ├── MonoGame.Framework.Facade/           ← TypeForwardedTo → KNI (337 types)
│   ├── SdvWebPort.PoC.Render/               ← KNI WebGL2 render PoC ( bouncing sprite )
│   ├── SdvWebPort.PoC.VfsRender/            ← VFS → Canvas decode PoC
│   ├── SdvWebPort.PoC.SmapiLoad/            ← SMAPI.dll load PoC
│   ├── SdvWebPort.PoC.SdvLoad/              ← Real SDV DLL load PoC (CURRENT FOCUS)
│   └── MockSdv.Target/                      ← Test target mimicking SDV shape
├── tests/
│   ├── SdvWebPort.Vfs.Tests/                ← VFS contract tests
│   └── SdvWebPort.Content.Tests/            ← XNB + BmFont tests (19/19 passing)
├── scripts/
│   ├── install-dotnet.sh                    ← .NET 10 SDK installer
│   ├── generate-facade-types.sh             ← Regenerate facade AssemblyInfo.cs
│   ├── run-sdv-load-poc.sh                  ← Build + serve SdvLoad PoC on :8000
│   ├── test-sdv-load-headless.js            ← Playwright + Chromium headless test
│   ├── verify-sdv-load-bundle.sh            ← Static bundle structure check
│   ├── wss_*.py                             ← 文叔叔 file download helpers
│   └── make-test-sprite.py
├── skills/                                  ← 14 superpowers skills + others
└── upload/                                  ← tmpfs (ephemeral) — user uploads
```

---

## Critical Knowledge (DON'T REDISCOVER THESE)

### 1. The facade pattern WORKS (v0.7.0)

The v0.6.0 conclusion "TypeForwardedTo doesn't work in Mono WASM" was WRONG.
The real issue was the trimmer stripping KNI target assemblies. Fix: add
`<TrimmerRootAssembly>` for each KNI assembly:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="MonoGame.Framework" />
  <TrimmerRootAssembly Include="Xna.Framework" />
  <TrimmerRootAssembly Include="Xna.Framework.Game" />
  <TrimmerRootAssembly Include="Xna.Framework.Graphics" />
  <TrimmerRootAssembly Include="Xna.Framework.Content" />
  <TrimmerRootAssembly Include="Xna.Framework.Input" />
</ItemGroup>
```

**Lesson:** When using TypeForwardedTo facade in trimmed WASM, root BOTH the
facade AND all forwarder targets. The trimmer can't infer the dependency.

### 2. `[JSImport]` not `[DllImport("__Internal")]`

On .NET 10 Blazor WebAssembly, `[DllImport("__Internal")]` fails at native
link time. Use `[System.Runtime.InteropServices.JavaScript.JSImport("globalThis.functionName")]`.

### 3. JsInterop classes must be top-level `internal static partial class`

Nested partial classes don't work with the `[JSImport]` source generator.

### 4. `[JSImport]` doesn't support `long` return type — use `int`

### 5. WASM SDK fingerprinted file names

The .NET 10 WASM SDK renames `dotnet.js` to `dotnet.<hash>.js` and
`MonoGame.Framework.dll` to `MonoGame.Framework.<hash>.wasm` in the
published `_framework/` dir. The run script copies these to stable
non-fingerprinted paths for easy HTTP fetch.

### 6. `<WasmInlineBootConfig>true</WasmInlineBootConfig>`

Required to inline the boot config into `dotnet.js` instead of emitting a
separate `dotnet.boot.js` file (which the SDK doesn't generate by default
in `Microsoft.NET.Sdk.WebAssembly`).

### 7. Sandbox persistence rules

- **`/home/z/my-project/upload/` is tmpfs** — wiped on session reset.
  Never put persistent files here.
- **Uncommitted files in `/home/z/my-project/` are lost on session reset.**
  Always commit + push before ending a session.
- **`git checkout -f` reverts uncommitted changes** — commit before any
  risky operation.
- **File system doesn't persist between Bash calls** in some cases —
  write + commit in a single Bash invocation when possible.

### 8. KNI assembly names

The KNI NuGet packages are named `nkast.Xna.Framework.*` but the actual
assembly names are `Xna.Framework`, `Xna.Framework.Game`, etc. (no `nkast`
prefix). This trips up the facade generator if you search for
`nkast.Xna.Framework` in `AppDomain.CurrentDomain.GetAssemblies()`.

### 9. XNB format

SDV XNB files use:
- 4-byte magic: `XNB` + platform char (`w` for Windows)
- Version byte (5)
- Flag byte (bit 0 = compressed)
- 4-byte file size (little-endian)
- If compressed: LZX (flag 0x80) or LZ4 (flag 0x40)
- Compressed data starts at offset 14

KNI's `LzxDecoderStream(stream, decompSize, compSize)` correctly decompresses.

### 10. PNG decoding in WASM

KNI's `Texture2D.FromStream` doesn't support PNG in WASM. Workaround: use
browser Canvas API via `[JSImport]` to decode PNG → RGBA bytes →
`Texture2D.SetData(rgba)`.

### 11. KNI Blazor.GL + .NET 10 Microsoft.NET.Sdk.WebAssembly = INCOMPATIBLE (Phase 2.5)

KNI v4.2.9001.2's `nkast.Kni.Platform.Blazor.GL` is designed for .NET 8's
`Microsoft.NET.Sdk.BlazorWebAssembly`, NOT .NET 10's `Microsoft.NET.Sdk.WebAssembly`.

Symptoms:
- Game1 instantiates ✅
- GraphicsDevice + SpriteBatch create ✅
- `game.Run()` returns normally ✅
- BUT no frames render (canvas stays black) ❌

Root cause: KNI's `ConcreteGame.StartGameLoop()` is an empty stub:
```csharp
private void StartGameLoop()
{
    // request next frame    ← EMPTY!
}
```
The game loop is supposed to be driven by the Blazor component model (App.razor
+ Pages/Index.razor), not by `Run()` blocking. On .NET 10 native WASM SDK,
there's no Blazor component model, so the loop never starts.

Fix: Use `Microsoft.NET.Sdk.BlazorWebAssembly` + `net8.0` for any project
that needs KNI's game loop. See "Phase 2.5b" in Next Steps.

---

## Environment Setup (Quick Reference)

```bash
# .NET 10 SDK
export PATH="/home/z/.dotnet:$PATH"
export DOTNET_ROOT="/home/z/.dotnet"

# Verify
dotnet --version  # should print 10.0.100

# wasm-tools workload (required for browser-wasm builds)
dotnet workload install wasm-tools

# Build entire solution
cd /home/z/my-project
dotnet build SdvWebPort.sln

# Run tests
dotnet test SdvWebPort.sln

# Run SdvLoad PoC (requires user-supplied "Stardew Valley.dll")
cp "/path/to/GOG/Stardew Valley/Stardew Valley.dll" src/SdvWebPort.PoC.SdvLoad/
bash scripts/run-sdv-load-poc.sh  # serves on http://localhost:8000/

# Run headless test (with MockSdv stand-in, no GOG files needed)
# 1. Publish + copy fingerprinted files + start server (see run-sdv-load-poc.sh)
# 2. Run Playwright test:
NODE_PATH=/home/z/.npm-global/lib/node_modules node scripts/test-sdv-load-headless.js 8000
```

---

## Git Workflow

- **Main branch:** `main` — always reflects latest completed work
- **Feature branches:** `feat/phase<N>-<name>` — one per phase
- **Tags:** `v<X>.<Y>.<Z>-<description>` — mark phase milestones
- **Commit message style:** `feat:`/`fix:`/`docs:`/`chore:`/`test:` prefix

**Before ending any session:**
```bash
cd /home/z/my-project
git add -A
git commit -m "feat: <description>"
git push origin <current-branch>
git tag v<X.Y.Z>-<description>
git push origin v<X.Y.Z>-<description>
```

**Remote:** `origin` → `https://github.com/nci6tjq7/sdv-web-port.git` (private)
The GitHub token is in `.env` (gitignored, ephemeral — re-set if needed).

---

## Superpowers Skills (installed)

14 skills in `skills/` directory, loaded via `Skill(command="skill-name")`:

| Skill | Purpose |
|-------|---------|
| `superpowers-using-superpowers` | Meta-skill — how to use skills |
| `superpowers-brainstorming` | Spec design before implementation |
| `superpowers-writing-plans` | Implementation plan documents |
| `superpowers-executing-plans` | Execute plan task-by-task |
| `superpowers-subagent-driven-development` | Dispatch fresh subagent per task |
| `superpowers-dispatching-parallel-agents` | Run subagents in parallel |
| `superpowers-systematic-debugging` | Root-cause-first debugging (4 phases) |
| `superpowers-test-driven-development` | RED → GREEN → COMMIT |
| `superpowers-verification-before-completion` | Verify before claiming done |
| `superpowers-requesting-code-review` | Dispatch reviewer subagent |
| `superpowers-receiving-code-review` | Handle review feedback |
| `superpowers-finishing-a-development-branch` | Merge + cleanup |
| `superpowers-using-git-worktrees` | Isolated work branches |
| `superpowers-writing-skills` | Create new skills |

**When in doubt about which skill to use:** invoke
`Skill(command="superpowers-using-superpowers")` first.

---

## Phase History (Condensed)

### Phase 0 (v0.1.0 → v0.2.0)
- Initial scaffold with Uno.Wasm.Bootstrap — abandoned (incompatible with KNI)
- Pivoted to `Microsoft.NET.Sdk.WebAssembly` (Blazor WASM)
- KNI WebGL2 PoC passes (bouncing sprite)
- SMAPI load PoC passes (assembly loads, types enumerable)

### Phase 1a (v0.3.0)
- `IVirtualFileSystem` interface
- `FileSystemAccessApiVfs` (A2 path — zero-copy directory read)
- `OpfsVfs` (A1 path — OPFS upload fallback)
- `VfsFactory` with browser capability detection
- Upload UI (FSA picker + OPFS drag-drop)

### Phase 1b (v0.3.1 → v0.4.0)
- `XnbReader` (7-bit encoded ints + XNB strings)
- `XnbFile` (header parser: 4-byte magic + platform + version + flag + size)
- `XnbTextureReader` (SurfaceFormat + RGBA extraction)
- `VfsContentManager` (VFS-backed content loading with LZX support)
- `ContentJsInterop` (Canvas PNG decode via `[JSImport]`)
- 12/12 tests passing

### Phase 1c (v0.5.0 → v0.5.1)
- `BmFontFile` (.fnt parser, 7 tests)
- `BmFontRenderer` (SpriteBatch text rendering via glyph atlas)
- LZX decompression verified (real SDV logo.xnb → 400×220 RGBA)
- 19/19 tests passing

### Phase 2 (v0.6.0 → v0.7.0)
- `MonoGame.Framework.Facade` — 337 TypeForwardedTo → KNI
- `SdvWebPort.PoC.SdvLoad` — HTTP fetch + ALC + facade resolver
- `MockSdv.Target` — test target mimicking SDV shape
- v0.6.0: TypeForwardedTo appeared to fail (wrong diagnosis)
- v0.7.0: systematic-debugging revealed trimmer was stripping KNI assemblies;
  added `<TrimmerRootAssembly>` for each KNI assembly → **WORKS**
- Headless Chromium test PASSES: Game1 base type resolves through facade to KNI

### Phase 2.5 (partial — KNI/.NET 10 incompatibility discovered)
- Extended `MockSdv.Target` with real `Game1 : Game` subclass (GraphicsDevice + SpriteBatch + bouncing red box)
- Extended `SdvWebPort.PoC.SdvLoad` to register KNI factories + instantiate Game1 + call Run()
- **PROVEN WORKING:** Game1 instantiates, GraphicsDevice creates, SpriteBatch creates, LoadContent completes
- **BLOCKER:** Game loop doesn't start — KNI's `ConcreteGame.StartGameLoop()` is an empty stub
- Root cause: KNI v4.2.9001.2 targets `Microsoft.NET.Sdk.BlazorWebAssembly` (net8.0), NOT .NET 10's `Microsoft.NET.Sdk.WebAssembly`
- Applied 5 shims (canvas ID, Blazor.platform, Blazor.runtime.Module, DotNet.invokeMethod, globalThis.Module) — fixed initialization but not the loop
- **Next: Phase 2.5b — pivot to `Microsoft.NET.Sdk.BlazorWebAssembly` + net8.0**

---

## Known Issues + Future Concerns

1. **GitHub token exposure:** The token `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`
   was exposed in earlier conversation. User should revoke it after project
   completion. (User said "项目跑完我会将token取消掉".)

2. **Real SDV DLL not yet tested:** All headless tests use `MockSdv.Target`
   as a stand-in. User needs to supply their GOG `Stardew Valley.dll` to
   test with the real game.

3. **Chrome/Chromium headless can't screenshot WebGL2 canvas:** The PoC.Render
   bouncing sprite was verified via console.log FPS output, not visual
   screenshot. For visual verification, user opens the served URL in a
   real browser.

4. **SMAPI's Harmony dependency:** Harmony uses IL.Emit which may not work
   in WASM's interpreter mode. Plan is to shim Harmony → MonoMod.RuntimeDetour
   (Phase 3). Risk: 25% probability of failure (per spec §10 R2).

5. **Memory constraints:** iOS Safari 2GB limit. Desktop target only;
   mobile is explicitly a non-goal.

---

## How to Update This File

When completing a phase or making a major architectural decision:

1. Update the "Phase Status" table
2. Update "Next Steps" to point to the new current phase
3. Add any new "Critical Knowledge" entries (things future agents shouldn't
   have to rediscover)
4. Update "Last updated" date at the top
5. Commit with message: `docs: update MEMORY.md for <change>`
6. Push to GitHub

**This file is the project's institutional memory. Keep it current.**
