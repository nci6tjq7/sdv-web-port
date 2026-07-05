# SDV Web Port — Long-Term Project Memory

> **THIS FILE IS THE PROJECT'S LONG-TERM MEMORY.**
> It survives session resets because it is committed to git and pushed to GitHub.
> **Any agent resuming work on this project MUST read this file FIRST, before
> doing anything else.**
>
> Last updated: 2026-07-05 (post-Phase 2.75 health check — spec v2 + code review + retrospective)
> Current state: Phase 2.75 complete (v1.1.0-sdv-fs-redirect) + health check done. Next: Phase 2.8 with subagent-driven-development.

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
| 2.5 — Game1 Invoke (.NET 10) | ⚠️ PARTIAL | `v0.8.0-phase2.5-partial` | Game1 instantiates but game loop doesn't start (KNI/.NET 10 mismatch) |
| 2.5b — Blazor Game Loop (net8.0) | ✅ DONE | `v0.9.0-blazor-loop-works` | KNI game loop WORKS on net8.0 BlazorWebAssembly — canvas renders |
| 2.6 — SdvBlazor Load + Render | ✅ DONE | `v1.0.0-sdv-renders` | Real SDV Game1 (MockSdv) loads via facade + renders — 6/6 checks PASS |
| 2.75 — Cecil FS Redirect | ✅ DONE | `v1.1.0-sdv-fs-redirect` | File.OpenRead → SdvFileShim → VFS — 'Hello from VFS!' loaded + rendered |
| 2.8 — Real GOG SDV.dll Test | ⏳ NEXT | — | User supplies GOG SDV.dll + Content/*.xnb; test title screen renders |
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

## Next Steps (Phase 2.8 — Real GOG SDV.dll Test)

**Goal:** Test the full pipeline with the REAL GOG `Stardew Valley.dll` + user's
uploaded Content/*.xnb files. The user supplies their GOG copy; we load it,
run the Cecil rewriter, and verify the SDV title screen renders in the browser.

**Context:** Phase 2.75 proved the Cecil FS redirect works with MockSdv's
`FileSystemTestGame` (a minimal Game that calls `File.OpenRead`). The next
step is to test with real SDV, which has far more complex file system usage
(Content pipeline, save files, configs, etc.) and may surface additional
File/Directory patterns the rewriter doesn't cover yet.

**Required work:**
1. User copies their GOG `Stardew Valley.dll` + `Content/` folder into
   `src/SdvWebPort.PoC.SdvBlazor/wwwroot/`
2. Wire up the real VFS (FSA/OPFS from Phase 1a) instead of InMemoryVfs —
   the user uploads their GOG files via the browser UI
3. Run the PoC — the Cecil rewriter will rewrite real SDV's File/Directory
   calls; SdvFileShim routes them to the VFS
4. If SDV uses File/Directory patterns not in `_rewriteMap`, add them
   (e.g., `File.ReadAllText(string, Encoding)`, `Directory.GetDirectories`,
   `Path.Combine`, etc.)
5. Verify the SDV title screen renders (headless screenshot)

**Why this approach:** Phase 2.75 proved the Cecil rewriting approach works.
The only remaining gap for real SDV is coverage of all its file system
patterns. Iteratively add patterns until the title screen renders.

**Branch:** create `feat/phase2.8-real-sdv-test` from `main`.

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

Symptoms on .NET 10:
- Game1 instantiates ✅
- GraphicsDevice + SpriteBatch create ✅
- `game.Run()` returns normally ✅
- BUT no frames render (canvas stays black) ❌

Root cause: KNI's `ConcreteGame.StartGameLoop()` is an empty stub. The game
loop is supposed to be EXTERNALLY driven by JS requestAnimationFrame calling
`[JSInvokable] TickDotNet`, not by `Run()` blocking.

**SOLUTION (Phase 2.5b — PROVEN):** Use `Microsoft.NET.Sdk.BlazorWebAssembly`
+ `net8.0`. The game loop pattern (from KNI's CanvasGL sample):
1. Blazor component `OnAfterRender(firstRender)` calls `JsRuntime.InvokeAsync("initRenderJS", DotNetObjectReference.Create(this))`
2. JS `initRenderJS` stores the .NET ref + starts `requestAnimationFrame(tickJS)`
3. JS `tickJS` calls `window.theInstance.invokeMethod('TickDotNet')` each frame + re-queues RAF
4. C# `[JSInvokable] TickDotNet()` creates the Game once (call `game.Run()`) + calls `game.Tick()` each frame

This is in `src/SdvWebPort.PoC.BlazorGameLoop/` (Phase 2.5b, v0.9.0).
See `Pages/Home.razor.cs` + `wwwroot/index.html` for the working pattern.

### 12. WebGL canvas pixel verification in headless tests (Phase 2.5b)

`canvas.getContext('webgl2').readPixels()` does NOT work for verifying KNI's
rendering — getting a new WebGL context doesn't see KNI's framebuffer. Also
`drawImage(canvas, 0, 0)` to a 2D canvas doesn't work for WebGL canvases.

**Working approach:** Use Playwright's `elementHandle.screenshot()` to capture
the canvas as a PNG, then analyze with `sharp` (Node package):
```javascript
await canvas.screenshot({ path: '/tmp/canvas.png' });
const { data } = await sharp('/tmp/canvas.png').raw().toBuffer({ resolveWithObject: true });
// data is a Buffer of RGBA bytes
```

### 13. Blazor WASM HttpClient needs absolute URL (Phase 2.6)

On Blazor WebAssembly, `HttpClient.GetByteArrayAsync("Stardew Valley.dll")`
(relative URL) fails with `net_http_client_invalid_requesturi`. The HttpClient
must have a BaseAddress set, OR you must construct an absolute URL.

**Fix:** Inject `IWebAssemblyHostEnvironment` and construct the absolute URL:
```csharp
[Inject] public IWebAssemblyHostEnvironment HostEnv { get; set; } = null!;
// ...
var absoluteUri = new Uri(new Uri(HostEnv.BaseAddress), "Stardew Valley.dll");
var bytes = await Http.GetByteArrayAsync(absoluteUri);
```
Program.cs must also register HttpClient with BaseAddress:
```csharp
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
```

### 14. Phase 2.6 complete: real SDV Game1 loads + renders (v1.0.0-sdv-renders)

The full pipeline is PROVEN end-to-end (headless Chromium test, 6/6 checks PASS):
```
MockSdv.dll (compiled against MonoGame.Framework v3.8.5.0)
  → HTTP fetch via HttpClient (absolute URL via IWebAssemblyHostEnvironment)
  → AssemblyLoadContext.Default.LoadFromStream
  → facade MonoGame.Framework (TypeForwardedTo)
  → KNI Xna.Framework.* (actual implementation)
  → Activator.CreateInstance(Game1)
  → game.Run() (Initialize + LoadContent + returns — StartGameLoop is empty stub)
  → JS requestAnimationFrame drives game.Tick() each frame
  → KNI GraphicsDevice → WebGL2 → visible pixels on canvas
```
Project: `src/SdvWebPort.PoC.SdvBlazor/` (net8.0 BlazorWebAssembly)
Screenshot: `download/phase2.6-sdv-blazor-canvas.png` (477,500 CornflowerBlue pixels)

### 15. Cecil IL rewriting for file system redirect (Phase 2.75, v1.1.0)

Mono.Cecil 0.11.6 works in WASM (pure managed, no native deps) for rewriting
SDV's `System.IO.File.*` / `System.IO.Directory.*` calls to `SdvFileShim.*`.

**Two critical fixes discovered during Phase 2.75:**

1. **TypeLoadException** — Cecil's `new TypeReference(...)` with `scope: module.TypeSystem.CoreLibrary`
   scopes the type to `System.Runtime`, but `SdvFileShim` lives in `SdvWebPort.Vfs`.
   Fix: use `module.ImportReference(Type)` from the loaded AppDomain assembly.

2. **MissingMethodException** — Using `callee.ReturnType` (e.g., `FileStream` from
   `File.OpenRead`) for the shim method reference fails because `SdvFileShim.OpenRead`
   returns `Stream` (not `FileStream`). Fix: import each shim method via
   `module.ImportReference(MethodInfo)` so return types + parameter types are correct.

**Working pattern** (in `src/SdvWebPort.Rewriter/SdvFileSystemRewriter.cs`):
```csharp
// Find SdvFileShim type in the loaded AppDomain
var vfsAsm = AppDomain.CurrentDomain.GetAssemblies()
    .FirstOrDefault(a => a.GetName().Name == "SdvWebPort.Vfs");
var shimType = vfsAsm.GetType("SdvWebPort.Vfs.SdvFileShim");
var shimTypeRef = module.ImportReference(shimType);

// Import each shim method (correct return types)
var shimMethods = new Dictionary<(string, int), MethodReference>();
foreach (var mi in shimType.GetMethods(BindingFlags.Public | BindingFlags.Static))
    shimMethods[(mi.Name, mi.GetParameters().Length)] = module.ImportReference(mi);

// In IL: replace File.OpenRead callee with SdvFileShim.OpenRead callee
instr.Operand = shimMethods[("OpenRead", 1)];
```

The rewriter runs in-memory on fetched DLL bytes — the user's SDV.dll file
on disk is NEVER modified (respects constraint C4).

Screenshot: `download/phase2.75-sdv-fs-redirect-canvas.png`
Evidence: `loadedText='Hello from VFS!'` logged on every frame — the
`File.OpenRead("Content/test.txt")` call was successfully redirected to VFS.

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

### Phase 2.5b (v0.9.0 — KNI game loop WORKS on net8.0)
- Created `SdvWebPort.PoC.BlazorGameLoop` project (`Microsoft.NET.Sdk.BlazorWebAssembly` + `net8.0`)
- Implemented the externally-driven game loop pattern (from KNI's CanvasGL sample):
  - Blazor component `OnAfterRender` → `JsRuntime.InvokeAsync("initRenderJS", DotNetObjectReference)`
  - JS `initRenderJS` → starts `requestAnimationFrame(tickJS)`
  - JS `tickJS` → `window.theInstance.invokeMethod('TickDotNet')` + re-queue RAF
  - C# `[JSInvokable] TickDotNet` → creates Game + calls `game.Tick()` each frame
- `LoopGame` (Game subclass) renders CornflowerBlue background + bouncing red 50x50 box
- **HEADLESS TEST PASSES:** 300+ frames drawn, canvas has 91 non-black pixel samples,
  30 CornflowerBlue samples, sampleColor = `[100, 149, 237]` (CornflowerBlue)
- Screenshot: `download/phase2.5b-blazor-loop-canvas.png`
- Tagged `v0.9.0-blazor-loop-works`
- **Next: Phase 2.6 — migrate SdvLoad facade→KNI to net8.0, load real SDV Game1**

### Phase 2.6 (v1.0.0 — real SDV Game1 loads + renders)
- Retargeted `MonoGame.Framework.Facade` + `MockSdv.Target` from net10.0 → net8.0
- Created `SdvWebPort.PoC.SdvBlazor` project (net8.0 BlazorWebAssembly)
- Combined Phase 2 (facade→KNI SDV load) + Phase 2.5b (externally-driven game loop)
- `Home.razor.cs` TickDotNet: fetch DLL → ALC.LoadFromStream → reflection find Game1 →
  Activator.CreateInstance → game.Run() → game.Tick() each frame
- Fix: HttpClient needs absolute URL via `IWebAssemblyHostEnvironment.BaseAddress`
- **HEADLESS TEST PASSES ALL 6 CHECKS:**
  - SDV loaded ✅
  - Game1 found ✅
  - Game1 instantiated ✅
  - Run() returned ✅
  - Frames rendered ✅ (2220+ frames)
  - Pixels non-black ✅ (91 nonBlack, 30 cornflower, sampleColor=[100,149,237])
- Screenshot: `download/phase2.6-sdv-blazor-canvas.png` (477,500 CornflowerBlue pixels)
- Tagged `v1.0.0-sdv-renders`
- **Next: Phase 2.75 — redirect real SDV's file system calls to VFS (Cecil rewriter)**

### Phase 2.75 (v1.1.0 — Cecil FS redirect works, VFS-backed SDV renders)
- Retargeted `SdvWebPort.Vfs` from net10.0 → net8.0
- Created `SdvWebPort.Vfs.SdvFileShim` — static class with File/Directory-equivalent signatures
- Created `SdvWebPort.Rewriter` project (net8.0) using Mono.Cecil 0.11.6
- `SdvFileSystemRewriter.Rewrite(byte[])` scans IL for File/Directory calls, rewrites to SdvFileShim
- 3 unit tests PASS (verify File.OpenRead, File.Exists, Directory.GetFiles redirected)
- Created `MockSdv.Target.FileSystemTestGame` — Game that calls `File.OpenRead("Content/test.txt")`
- Wired rewriter into `SdvBlazor/Pages/Home.razor.cs`: fetch → rewrite → load → instantiate → Run → Tick
- VFS set up with `InMemoryVfs` containing "Hello from VFS!" test file
- Two debugging fixes:
  1. TypeLoadException — use `module.ImportReference(Type)` not `new TypeReference(..., CoreLibrary)`
  2. MissingMethodException — use `module.ImportReference(MethodInfo)` for correct return types
- **HEADLESS TEST PASSES ALL 9 CHECKS:**
  - SDV loaded + Game found + instantiated + Run() returned ✅
  - Rewriter ran + SdvFileShim called + VFS text loaded ✅
  - Frames rendered (2220+) + Pixels non-black ✅
- Key evidence: `loadedText='Hello from VFS!'` on every frame
- Screenshot: `download/phase2.75-sdv-fs-redirect-canvas.png`
- Tagged `v1.1.0-sdv-fs-redirect`
- **Next: Phase 2.8 — test with real GOG SDV.dll + user's Content/*.xnb files**

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

---

## Retrospective: Phase 0 → Phase 2.75 (2026-07-05)

### What went well

1. **MEMORY.md persistence layer** — solved the session-reset context loss
   problem. Future agents can resume from git + GitHub even with zero
   conversation history.

2. **systematic-debugging skill** — Phase 2.5→2.5b architecture pivot was
   driven by the 4-phase root-cause process, not guessing. Discovered
   `StartGameLoop()` empty stub by reading KNI source on GitHub.

3. **writing-plans skill** — every phase had a plan document. Plans caught
   scope creep and forced decomposition into bite-sized tasks.

4. **TDD for Rewriter** — 3 (now 7) unit tests caught the Cecil return-type
   bug early. Tests verify IL structure correctness, not just "didn't throw."

5. **Frequent commits + tags** — 9 version tags (v0.1.0 → v1.1.0) give
   clear rollback points. Every phase is a recoverable checkpoint.

6. **Headless browser verification** — every rendering PoC verified via
   Playwright + sharp pixel analysis, not just console.log. Real evidence
   (477,500 CornflowerBlue pixels) vs assertions.

### What went wrong (and lessons)

1. **Phase 2.5 .NET 10 dead end (3+ days wasted)**
   - **What:** Assumed KNI works on .NET 10 `Microsoft.NET.Sdk.WebAssembly`.
     Spent 5 shim attempts before discovering KNI only supports net8.0
     BlazorWebAssembly.
   - **Root cause:** No upfront research on KNI's SDK compatibility. v1 spec
     assumed .NET 10 without verifying.
   - **Lesson:** Before committing to a tech stack, verify third-party
     compatibility by checking the library's target framework + sample
     templates. KNI's BlazorGL template clearly shows `net8.0` +
     `Microsoft.NET.Sdk.BlazorWebAssembly`.
   - **Action:** spec v2 now documents this; risk R3 reassessed down (net8.0
     is mature LTS).

2. **No code review until Phase 2.75 health check (50+ commits unreviewed)**
   - **What:** From Phase 0 to 2.75, no agent reviewed the code. Code review
     found 6 Important + 10 Minor issues, including a logic bug
     (`DirectoryExists` always-true) and a broken fallback method.
   - **Root cause:** "不停推进" mindset — kept moving forward without
     stopping to verify quality.
   - **Lesson:** Code review is not optional. The `requesting-code-review`
     skill exists for a reason. Review after each major feature, not after
     5 features.
   - **Action:** Phase 2.8+ will use `subagent-driven-development` with
     review after EACH task.

3. **Brainstorming skipped — all decisions made unilaterally**
   - **What:** Every phase's approach was chosen by the main agent without
     the "one question at a time" dialogue the brainstorming skill prescribes.
   - **Root cause:** User said "按推荐推进" so I just picked an approach
     and executed.
   - **Lesson:** "按推荐推进" means "use your judgment on the recommendation,"
     not "skip design exploration." Even a 5-minute brainstorming dialogue
     would have surfaced the .NET 10 risk before wasting 3 days.
   - **Action:** For Phase 2.8+, present 2-3 options with trade-offs even
     when user says "按推荐推进."

4. **Test coverage too narrow**
   - **What:** Only Rewriter had tests (3/7 entries covered). SdvFileShim,
     Home.razor.cs, facade integrity — all untested.
   - **Root cause:** Rushed to "make it work" without "make it correct."
   - **Lesson:** TDD isn't just for one component. Every public method
     should have at least one test.
   - **Action:** Phase 2.8 includes test coverage for SdvFileShim + facade.

5. **Technical debt accumulated (9 projects, some stale)**
   - **What:** PoC.SdvLoad, PoC.Render, PoC.VfsRender, PoC.SmapiLoad, Runtime
     are all stale (superseded by SdvBlazor). Nobody cleaned up.
   - **Root cause:** No `finishing-a-development-branch` discipline.
   - **Lesson:** Each phase should end with cleanup: mark/branch/archive
     superseded PoCs.
   - **Action:** spec v2 §17 lists stale projects; Phase 2.8 will archive them.

6. **Spec document not updated through 7 phases**
   - **What:** v1 spec (823 lines) was Phase 0 era. 3 core assumptions were
     wrong by Phase 2.5b but nobody updated it.
   - **Root cause:** "Keep moving" mindset — documentation felt less urgent
     than code.
   - **Lesson:** Spec is the source of truth. If it's wrong, every future
     agent inherits the wrong assumptions.
   - **Action:** spec v2 written; risk register reassessed; v1 marked
     historical. Update spec at every phase boundary going forward.

### Process improvements for Phase 2.8+

1. **Use `subagent-driven-development`** — fresh subagent per task, review
   between tasks. Prevents context bloat + catches issues early.

2. **Brainstorm before planning** — even for "按推荐推进," present 2-3
   options with trade-offs and get explicit approval on the approach.

3. **Code review after each task** — not after each phase. Use
   `requesting-code-review` skill's template.

4. **Update spec at phase boundaries** — not after 7 phases pile up.

5. **TDD for all new code** — every public method gets a test. No exceptions.

6. **End each phase with `finishing-a-development-branch`** — merge, cleanup,
   archive superseded code.

7. **Retrospective at phase boundaries** — what went well, what didn't,
   what to change. Write it into this section.

### Metrics

- Phases completed: 0, 1a, 1b, 1c, 2, 2.5(partial), 2.5b, 2.6, 2.75 = 9
- Tags: v0.1.0 → v1.1.0 = 9 releases
- Commits: ~60
- Projects: 11 (6 active, 5 stale)
- Tests: 7 (all for Rewriter; 0 for other components — gap)
- Code reviews: 1 (this health check; should have been 9)
- Headless verifications: 4 (Phase 2, 2.5b, 2.6, 2.75)
