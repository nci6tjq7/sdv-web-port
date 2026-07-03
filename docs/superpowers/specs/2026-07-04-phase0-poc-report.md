# Phase 0 PoC Report

**Date:** 2026-07-04 (Asia/Shanghai)
**Branch:** `feat/phase0-skeleton-poc`
**Plan:** `docs/superpowers/plans/2026-07-03-phase0-skeleton-and-poc.md`

## Summary

Phase 0 successfully stood up the project skeleton and ran two PoCs to validate the foundational technical assumptions. The runtime layer (.NET 10 WASM + Uno.Wasm.Bootstrap) works. The VFS abstraction is unit-tested. The render PoC surfaced a fundamental platform integration issue (not performance). The SMAPI PoC passed.

## PoC Results

### PoC A — KNI WebGL Rendering: **FAIL** (platform integration issue)

**What happened:**
- ✅ KNI packages install and C# compiles (build succeeds, 0 warnings, 0 errors)
- ✅ .NET 10 WASM runtime loads via Uno.Wasm.Bootstrap
- ✅ Main() runs, `GameFactory.RegisterGameFactory(new ConcreteGameFactory())` succeeds
- ❌ `new PocGame()` hangs at `BlazorGameWindow` constructor

**Root cause:**
The KNI Blazor.GL platform (`nkast.Kni.Platform.Blazor.GL` v4.2.9001.2) is designed for the **Blazor WebAssembly host**, not Uno.Wasm.Bootstrap. It depends on `nkast.Wasm.*` packages (`Canvas`, `Dom`, `JSInterop`, `XHR`, `Audio`) — nkast's own JS interop stack that Blazor WASM bootstraps automatically but Uno.Wasm.Bootstrap does not. When `BlazorGameWindow` tries to access the DOM via this unbootstrapped interop layer, it hangs silently.

**This is NOT a performance issue** (spec §10 R1 framed it as 30% chance of <15 FPS). It's a host platform integration issue not anticipated by the spec.

**Evidence:**
- DLL reflection on `Kni.Platform.dll` confirmed `BlazorGameWindow` constructor calls into `nkast.Wasm.Dom.Window` types
- `nkast.Wasm.Dom.dll` exists but its `Window` type requires `nkast.Wasm.JSInterop` runtime to be bootstrapped
- Chrome console captured only `[PoC.Render] GameFactory registered.` — no further output, no exception, no crash
- 187 framework WASM files all downloaded successfully (no network failures)

### PoC B — SMAPI Assembly Load: **PASS** ✅

**What happened:**
- ✅ SMAPI.dll (v4.5.1.0, 1,037,312 bytes / 0.99 MB) fetched via HttpClient from served URL
- ✅ Loaded into `AssemblyLoadContext` named "SmapiPoC" via `LoadFromStream(MemoryStream)`
- ✅ Assembly manifest read: Name=StardewModdingAPI, Version=4.5.1.0, Culture=neutral
- ✅ 16 custom attributes enumerable (ExtensionAttribute, CompilationRelaxationsAttribute, RuntimeCompatibilityAttribute, DebuggableAttribute, InternalsVisibleToAttribute ×4, etc.)
- ⚠️ `GetTypes()` throws `FileNotFoundException` for `MonoGame.Framework, Version=3.8.0.1641` (SMAPI's compile-time dependency)

**The GetTypes() failure is expected and acceptable** — the PoC's goal (per spec §10.1) is "try loading StardewModdingAPI.dll into WASM, see if startup log can output (does not require hook success)". Load + manifest + log output all succeeded. Full type closure resolution (including MonoGame.Framework) is a Phase 3 concern.

**Evidence:**
- Console output: `[+] Loaded: StardewModdingAPI, Version=4.5.1.0` followed by `[PASS] SMAPI loaded successfully — all checks passed`
- Exit code 0 from `scripts/run-smapi-poc.sh`
- Full log at `.superpowers/sdd/poc-smapi-artifacts/all.log`

## Decision Matrix

Per spec §10.1:

| Render PoC | SMAPI PoC | Decision |
|---|---|---|
| PASS | PASS | Proceed to Phase 1+2+3 |
| PASS | FAIL | Degrade to "no-mod browser version". Cancel Phases 3-4. |
| FAIL | PASS | Retry render optimization (1-2 weeks), then re-evaluate |
| FAIL | FAIL | STOP project. Re-evaluate technical choices. |

**Actual result:** Render FAIL + SMAPI PASS → "Retry render optimization, then re-evaluate"

But the spec's matrix assumed Render FAIL = performance issue. Our Render FAIL = platform integration issue. The "optimization" is actually a **host pivot**:

## Recommended Decision: Pivot Render Host to Blazor WebAssembly

### Why pivot
1. **KNI's primary web target is Blazor WebAssembly.** The `nkast.Wasm.*` packages and `BlazorGameWindow` are purpose-built for it.
2. **Blazor WebAssembly in .NET 10 also supports Jiterpreter + Mixed-Mode** — these are .NET runtime features, not Uno-specific.
3. **The original Uno.Wasm.Bootstrap advantage** ("finer-grained runtime mode control") doesn't matter if the rendering stack can't initialize.
4. **SMAPI PoC was host-agnostic** — it works on any .NET 10 WASM runtime, so pivoting hosts doesn't lose SMAPI progress.

### What pivoting affects

| Spec section | Current | After pivot |
|---|---|---|
| §5.1 Runtime | `Uno.Wasm.Bootstrap` | `Microsoft.NET.Sdk.WebAssembly` (Blazor WASM) |
| §5.4 csproj | `WasmShell*` properties | `RunAOTCompilation`, `WasmRuntimeExecutionMode` (Blazor WASM standard) |
| Task 2 | Uno project skeleton | Rewrite as Blazor WASM project |
| Task 4 | KNI PoC against Uno | Re-run KNI PoC against Blazor WASM (should work) |
| Task 5 | SMAPI PoC (passed) | No changes needed — host-agnostic |
| Spec §3.1 Layer 1 | "Uno.Wasm.Bootstrap (Mixed-Mode + Jiterpreter)" | "Blazor WebAssembly (.NET 10 with Jiterpreter + Mixed-Mode)" |

### Cost
- Rewrite Task 2: ~2 hours (csproj + Program.cs + index.html — straightforward port)
- Re-run Task 4: ~4 hours (KNI should work against Blazor WASM with minimal changes)
- Update spec: ~1 hour (revisions to §5.1, §5.4, §3.1)
- Total: ~1 day

### Alternatives considered (rejected)

**Option B: Skip KNI, write minimal WebGL interop directly**
- Estimated cost: months of work (reimplement MonoGame API surface)
- Defeats "use existing MonoGame-compatible framework" purpose
- Risk: high (SDV uses non-trivial MonoGame features)

**Option C: Try older KNI v3.14.9001 (non-Blazor-specific WebGL platform)**
- Found `nkast.Xna.Framework.Blazor` v3.14.9001 in NuGet search
- May or may not have a non-Blazor-specific WebGL backend
- Risk: medium (older version, less maintained)
- Cost: ~half a day to evaluate

**Option D: STOP project**
- Not warranted — SMAPI PoC passed, runtime layer works, only render layer needs pivot
- Premature termination

## Phase 0 Deliverables Status

| Spec §9 Phase 0 acceptance | Status | Evidence |
|---|---|---|
| `dotnet 10 + Uno.Wasm.Bootstrap project can build` | ✅ PASS | `dotnet build SdvWebPort.sln` exits 0 for Runtime, PoC.Render, PoC.SmapiLoad, Vfs, Vfs.Tests |
| `Browser can load WASM bundle without errors` | ✅ PASS | Chrome loads page, `dotnet.create()` completes, Main runs (both PoCs) |
| `Canvas displays a frame of specified color` | ⚠️ PARTIAL | Task 2's Runtime project clears canvas via JS interop (no KNI needed); Task 4's KNI rendering didn't initialize |
| `WASM runtime logs visible in browser console` | ✅ PASS | All PoCs produce `[PoC.X]` log lines visible in console + captured by on-page log div |

## Spec Risk Register Update

Per spec §10:

| # | Risk | Original estimate | Resolved status |
|---|---|---|---|
| R1 | KNI+WebGL2 <15 FPS | 30% probability | **Confirmed FAIL — but for non-performance reason** (platform integration). Spec needs revision: split R1 into R1a (performance) and R1b (platform integration) |
| R2 | SMAPI in WASM completely fails | 25% probability | **Resolved PASS** — `AssemblyLoadContext.LoadFromStream` works, manifest readable. Full type enumeration needs Phase 3 dependency closure |
| R3 | .NET 10 WASM toolchain bug blocks | 20% probability | **Partially confirmed** — AOT publish broken (`MonoAOTCompiler task not found`), `dotnet run` broken (`no perHostConfigs found`). Workarounds exist (Interpreter mode only, `python3 -m http.server`) |
| R4 | Memory >2GB crashes low-end devices | 15% probability | Not tested in Phase 0 |

## What was actually built in Phase 0

### Source code
- `SdvWebPort.sln` — solution with 5 projects (Runtime, Vfs, Vfs.Tests, PoC.Render, PoC.SmapiLoad)
- `src/SdvWebPort.Runtime/` — Uno.Wasm.Bootstrap skeleton (Task 2)
- `src/SdvWebPort.Vfs/` — VFS interface + InMemoryVfs (Task 3)
- `src/SdvWebPort.PoC.Render/` — KNI WebGL PoC (Task 4, FAIL)
- `src/SdvWebPort.PoC.SmapiLoad/` — SMAPI load PoC (Task 5, PASS)
- `tests/SdvWebPort.Vfs.Tests/` — 5 unit tests, all passing

### Scripts
- `scripts/install-dotnet.sh` — .NET 10 SDK bootstrap
- `scripts/verify-environment.sh` — pre-flight checks
- `scripts/make-test-sprite.py` — generate 256×256 PNG
- `scripts/run-render-poc.sh` — render PoC orchestrator
- `scripts/run-smapi-poc.sh` — SMAPI PoC orchestrator

### Documentation
- `docs/superpowers/specs/2026-07-03-sdv-web-port-design.md` — design spec (810 lines)
- `docs/superpowers/plans/2026-07-03-phase0-skeleton-and-poc.md` — implementation plan (1482 lines)
- `.superpowers/sdd/progress.md` — SDD ledger
- `.superpowers/sdd/task-{1..5}-report.md` — per-task reports
- `.superpowers/sdd/phase0-poc-report.md` — this file

### Commits on `feat/phase0-skeleton-poc`
1. `422b4f8` — Task 1: bootstrap .NET 10 SDK + environment verification
2. `73a8a08` — Task 2: scaffold Uno.Wasm.Bootstrap + .NET 10 runtime with canvas interop
3. `283ff64` — Task 2: docs fix
4. `4173a22` — Task 2: remove deprecated DevServer reference
5. `8eb38ec` — Task 3: IVirtualFileSystem abstraction + InMemoryVfs impl with tests
6. `af20f20` — Task 3: worklog entry
7. `854bcaf` — Task 4: KNI WebGL rendering PoC (FAIL — platform integration issue)
8. `713d638` — Task 5: SMAPI assembly load PoC (PASS)

## Next Steps (Pending User Decision)

The user needs to decide between three paths forward:

### Path 1 (Recommended): Pivot to Blazor WebAssembly host
- Update spec §5.1, §5.4, §3.1
- Rewrite Task 2 as Blazor WASM project
- Re-run Task 4 KNI PoC against Blazor WASM (should work, since KNI is built for Blazor)
- Then proceed to Phase 1 (title screen rendering)

### Path 2: Try KNI v3.14.9001 (older, possibly non-Blazor-specific)
- Add `nkast.Xna.Framework.Blazor` v3.14.9001 to PoC.Render
- See if it has a non-Blazor-specific WebGL backend
- If it works, no pivot needed — keep Uno.Wasm.Bootstrap
- Risk: older version, less maintained

### Path 3: Re-evaluate overall architecture
- Given the platform integration surprise, re-examine other spec assumptions
- Consider if NativeAOT-LLVM (originally rejected for SMAPI incompatibility) might actually be viable for the rendering layer (separate from SMAPI which is interpreter-only)
- This would be a more significant spec revision

## Phase 0 Verdict

**Phase 0 produced mixed results:**
- ✅ Project skeleton is solid (build works, runtime loads, VFS abstracted)
- ✅ SMAPI load layer is viable
- ❌ Render layer needs architecture pivot before Phase 1 can start

**Not a Phase 0 failure** — the PoCs did exactly what they were supposed to do: surface foundational risks before committing to deeper work. The platform integration issue would have been far more expensive to discover in Phase 1 or later.

**Estimated time to recover and proceed to Phase 1:** 1-2 days (pivot + re-run PoC + spec revision).
