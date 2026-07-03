# Superpowers z.ai Worklog

## Task 1 — Environment Bootstrap
- **Task ID:** 1
- **Agent:** implementer-subagent
- **Branch:** `feat/phase0-skeleton-poc`
- **Commit:** `422b4f8` — `chore: bootstrap .NET 10 SDK + environment verification`
- **Status:** DONE

### Summary
Bootstrapped the Phase 0 dev environment for the Stardew Valley web port:
- Installed .NET 10.0.100 SDK to `~/.dotnet` via `scripts/install-dotnet.sh` (idempotent; uses Microsoft's `dotnet-install.sh`).
- Wrote `scripts/verify-environment.sh` that checks .NET ≥ 10.0.100, Node ≥ 20, npm ≥ 10, and WARNs (not FAILs) on missing Chrome. Auto-discovers `~/.dotnet` on PATH so it works from a fresh shell.
- Wrote `.gitignore` per the plan (excludes game files `*.xnb`, `StardewValley/`, `Stardew Valley.dll`, `StardewModdingAPI.dll`; allows `tests/fixtures/*.xnb`) plus a small sandbox-overlay section for `skills/` and `.superpowers/`.

### Verification
`verify-environment.sh` exit 0 — 3 passed, 0 failed, 1 warning (Chrome absent).

### Files
- `scripts/install-dotnet.sh` (new, executable)
- `scripts/verify-environment.sh` (new, executable)
- `.gitignore` (modified)

### Concerns flagged
- Pre-existing tracked pollution in repo history (notably a 490 MB `download/星露谷物语.zip` game archive committed by prior sandbox sessions). `.gitignore` prevents new additions but does not untrack existing files. Out of scope for Task 1.
- Git branch state does not persist across separate Bash tool calls in this sandbox — chained `checkout && add && commit` in a single command to land the commit on the correct branch. Downstream tasks should do the same.
- Chrome/Chromium not installed; will need to be addressed when Phase 0 PoC E2E tasks run.

### Report
Full report at `/home/z/my-project/.superpowers/sdd/task-1-report.md`.

---

## Task 2 — Solution Skeleton & Uno.Wasm.Bootstrap Project
- **Task ID:** 2
- **Agent:** implementer-subagent
- **Branch:** `feat/phase0-skeleton-poc`
- **Commit:** `73a8a08` — `feat: scaffold Uno.Wasm.Bootstrap + .NET 10 runtime with canvas interop`
- **Status:** DONE_WITH_CONCERNS

### Summary
Set up the Phase 0 solution skeleton and the `SdvWebPort.Runtime` WASM project:
- Created `SdvWebPort.sln` at project root (classic `.sln` format via `dotnet new sln --format sln`; .NET 10 defaults to `.slnx`).
- Created `src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj` targeting `net10.0` / `browser-wasm` with Uno.Wasm.Bootstrap configuration (Mixed-Mode `InterpreterAndAOT`, Jiterpreter on, OPFS + FileDescriptors + compressed files, custom `WasmShellIndexHtmlPath`).
- Wrote `Program.cs` with a `Task<int> Main` that logs runtime info, calls a JS interop `ClearCanvas(0x33,0x66,0x99)` to fill the `<canvas>` with `#336699`, then keeps the runtime alive via `Task.Delay(Timeout.Infinite)`.
- Wrote `wwwroot/index.html` with the `<canvas id="game-canvas">`, a status `<div>`, and an inline `<script>` that registers `globalThis.clearCanvas` as the JS interop target.
- Installed the `wasm-tools` .NET 10 workload (required for `browser-wasm` builds).

### Deviations from plan
- **Uno.Wasm.Bootstrap version** — the plan specified `8.0.45`, which does not exist on NuGet. Used **`10.0.96`** (the latest stable release, the highest version compatible with the .NET 10 SDK; the `10.0.x` line is the .NET 10 family).
- **`Uno.Wasm.Bootstrap.DevServer` package removed** — discovered during build that v10.0.96 of `Uno.Wasm.Bootstrap.DevServer` is a **deprecated empty package** ("This package is not needed anymore and can be removed"). Removed the `<PackageReference>` because it pulled in no targets or assemblies. The main `Uno.Wasm.Bootstrap` package now includes everything.
- **JS interop API** — the plan's `[DllImport("__Internal")] extern Task ClearCanvasAsync(...)` fails at native link time on .NET 10 (`wasm-ld: undefined symbol: ClearCanvas`). The .NET 10 WASM linker treats `__Internal` P/Invokes as native C symbols and does not auto-wire them to global JS functions in this configuration. Switched to the modern .NET 8+ **`[JSImport("globalThis.clearCanvas")]`** attribute (in `System.Runtime.InteropServices.JavaScript`), which resolves the import path at runtime against the JS global scope. Changed the return type from `Task` to `void` (synchronous P/Invoke) and dropped the `await`.
- **`WasmShellEmccLinkerOptimizationLevel>O2</WasmShellLinkOptimizationLevel>` typo** — the plan had mismatched open/close tags for this property. Replaced with a single self-consistent `<WasmShellEmccLinkOptimizationLevel>O2</WasmShellEmccLinkOptimizationLevel>` entry.
- **Dev server workaround** — `dotnet run` / `dotnet msbuild -t:Run` invokes `WasmAppHost` (the .NET 10 native host), which fails with `Error: no perHostConfigs found` because the runtimeconfig.json's `perHostConfig` array is empty. Uno.Wasm.Bootstrap v10.0.96 ships no `Run` target override (the DevServer package used to provide one but is now deprecated/empty). For dev-server HTML verification, served a constructed `/tmp/sdv-serve/` directory (source `wwwroot/index.html` + built `AppBundle/_framework/*` + a `<script type="module">` bootstrap that imports `dotnet.js`) via `python3 -m http.server 8000`. `curl http://localhost:8000/` returns the HTML; `_framework/dotnet.js` and `_framework/dotnet.native.wasm` (≈14.8 MB) are served with correct MIME types.

### Verification
- `dotnet build SdvWebPort.sln` — exit 0, 0 warnings, 0 errors. App bundle emitted at `src/SdvWebPort.Runtime/bin/Debug/net10.0/browser-wasm/AppBundle/`.
- `dotnet publish -c Release` — **fails** with `error MSB4036: The "MonoAOTCompiler" task was not found` when AOT is enabled (`InterpreterAndAOT` mode + Release config). The `Microsoft.NET.Runtime.MonoAOTCompiler.Task` workload pack installs but MSBuild does not pick it up. This is the same root cause behind the empty `perHostConfig` issue. Publish **does** succeed with `-p:WasmShellMonoRuntimeExecutionMode=Interpreter -p:RunAOTCompilation=false`, but the publish output is flat (no `wwwroot/`/`_framework/` layout), so Uno's `_UnoPublishConfigJsPath` validation emits `warning : [Uno] Could not find dotnet.js file in publish output at: .../wwwroot/_framework/` — Uno.Wasm.Bootstrap v10.0.96 appears to predate a final RTM layout change in the .NET 10 SDK.
- `python3 -m http.server 8000` on the constructed serve dir: `curl -sI http://localhost:8000/` → `HTTP/1.0 200 OK`, `Content-type: text/html`, `Content-Length: 2417`. `curl -sI http://localhost:8000/_framework/dotnet.js` → `200 OK`, `text/javascript`. `curl -sI http://localhost:8000/_framework/dotnet.native.wasm` → `200 OK`, `application/wasm`.
- **Browser visual verification NOT done** — Chrome/Chromium is not installed in the sandbox (Task 1 already flagged this). Could not verify canvas actually renders `#336699` or that the runtime logs `[SdvWebPort] Runtime initialized` in the browser console. Runtime load is unverified end-to-end; only the static-asset serving path is verified.

### Files
- `SdvWebPort.sln` (new)
- `src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj` (new)
- `src/SdvWebPort.Runtime/Program.cs` (new)
- `src/SdvWebPort.Runtime/wwwroot/index.html` (new)
- `worklog.md` (modified — this entry, plus backfilled Task 1 entry that was left unstaged by Task 1)

### Concerns flagged
- **Uno.Wasm.Bootstrap v10.0.96 + .NET 10 RTM compatibility is incomplete.** Two concrete defects:
  1. `dotnet run` / `dotnet msbuild -t:Run` invokes `WasmAppHost` (native host) instead of a browser dev server, and `WasmAppHost` errors out with `no perHostConfigs found` because the runtimeconfig.json's `perHostConfig` is empty. The deprecated `Uno.Wasm.Bootstrap.DevServer` package no longer provides a `Run` target override.
  2. `dotnet publish -c Release` with `InterpreterAndAOT` mode fails because `MonoAOTCompiler` MSBuild task is not found, even though the workload pack is installed. This will block Phase 1 AOT work unless resolved.
  Both look like version-skew between Uno.Wasm.Bootstrap 10.0.96 (released against a .NET 10 preview) and the .NET 10.0.100 RTM SDK. Recommend trying a newer Uno.Wasm.Bootstrap prerelease or pinning a slightly older .NET 10 SDK if AOT/dev-server is needed for downstream tasks.
- **`[DllImport("__Internal")]` no longer works out of the box** for Uno.Wasm.Bootstrap on .NET 10 — the native linker needs a C symbol, not a JS function. Either use `[JSImport]` (as done here) or wire up `WasmShellExtraFiles` + emscripten `mergeInto(LibraryManager.library, {…})`. The plan's claim that "Uno.Wasm.Bootstrap wires `__Internal` to global JS functions automatically" is not true on v10.0.96.
- **Browser E2E verification deferred** until Chrome/Chromium is available in the sandbox (Task 1 concern, restated).
- **No `JsInterop.js` / `WasmShellExtraFiles` file was created** — the task brief's Critical Note #7 explicitly authorized the simpler inline-`<script>` approach when the JS function lives on `globalThis`. The csproj is therefore one ItemGroup shorter than the plan literally specified.

### Report
Full report at `/home/z/my-project/.superpowers/sdd/task-2-report.md`.
