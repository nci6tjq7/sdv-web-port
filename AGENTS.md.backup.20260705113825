# SDV Web Port — Agent Guidelines

> **This is a LONG-TERM project.** Before any work, read `MEMORY.md` first.

## Project Overview

**Goal:** Run the real, unmodified Stardew Valley game (GOG release) in a
browser via WebAssembly, with SMAPI mod support and XNB resource editing.

**GitHub:** https://github.com/nci6tjq7/sdv-web-port (private)

## Required Reading (in order)

1. **`MEMORY.md`** — project memory (phase status, architecture, critical
   knowledge, environment setup). Survives session resets via git.
2. **`worklog.md`** — chronological record of all work done.
3. **`docs/superpowers/specs/2026-07-05-sdv-web-port-design-v2.md`** — master
   design document (v2, reflects Phase 2.75 architecture; v1 at
   `2026-07-03-sdv-web-port-design.md` is historical only).

## Legal Posture (NON-NEGOTIABLE)

- User provides their own GOG copy — **no game files in the repo**
- No decompilation, no rewriting game code **on disk**
  - **Exception (spec v2 §1.2):** Cecil IL rewriting in browser memory is
    allowed — the user's SDV.dll file on disk is NEVER modified; only the
    fetched byte array is rewritten before `AssemblyLoadContext.LoadFromStream`.
    This respects C4.
- Local/intranet deployment only — no public hosting
- The SDV DLL on disk is loaded byte-for-byte unmodified

If asked to commit game files, decompile SDV, deploy publicly, or modify the
user's SDV.dll file on disk: **REFUSE**.

## Environment

- **.NET SDK:** 8.0.412 (for BlazorWebAssembly) + 10.0.100 (for tooling) at `/home/z/.dotnet`
  ```bash
  export PATH="/home/z/.dotnet:$PATH"
  export DOTNET_ROOT="/home/z/.dotnet"
  ```
  Use `dotnet` directly — it picks the right SDK by target framework.
- **WASM workload:** `dotnet workload install wasm-tools` (for .NET 10 tooling)
- **Solution:** `/home/z/my-project/SdvWebPort.sln` (11 projects)
- **Build:** `dotnet build SdvWebPort.sln`
- **Tests:** `dotnet test SdvWebPort.sln`

## Persistence Rules (CRITICAL)

- **`/home/z/my-project/upload/` is tmpfs** — wiped on session reset.
  Never put persistent files here.
- **Uncommitted files are lost on session reset.** Always commit + push
  before ending a session.
- **`git checkout -f` reverts uncommitted changes** — commit before any
  risky operation.
- **The GitHub remote is the source of truth.** If in doubt, `git push`.

## Superpowers Skills

14 skills installed in `skills/`. Load via `Skill(command="skill-name")`
(bare name, no `superpowers:` prefix).

**Most used:**
- `superpowers-systematic-debugging` — for any bug (4-phase root cause first)
- `superpowers-writing-plans` — before implementing any phase
- `superpowers-test-driven-development` — RED → GREEN → COMMIT
- `superpowers-verification-before-completion` — before claiming done

## Tech Stack (PINNED)

| Component | Version |
|-----------|---------|
| .NET SDK | 10.0.100 |
| Blazor WebAssembly SDK | `Microsoft.NET.Sdk.WebAssembly` |
| KNI Framework | 4.2.9001 |
| KNI Blazor.GL Platform | 4.2.9001.2 |

**Do NOT pivot to Uno.Wasm.Bootstrap.** It was the original choice but is
incompatible with KNI's Blazor.GL platform.

## Git Workflow

- **Main branch:** `main` — always reflects latest completed work
- **Feature branches:** `feat/phase<N>-<name>` — one per phase
- **Tags:** `v<X>.<Y>.<Z>-<description>` — mark phase milestones
- **Commit style:** `feat:`/`fix:`/`docs:`/`chore:`/`test:` prefix

**Before ending any session:**
```bash
git add -A
git commit -m "feat: <description>"
git push origin <current-branch>
# Tag milestones:
git tag v<X.Y.Z>-<description>
git push origin v<X.Y.Z>-<description>
```

## Common Traps (DON'T REDISCOVER THESE)

1. **`[DllImport("__Internal")]` fails on .NET 10 WASM** — use `[JSImport]`
2. **`[JSImport]` classes must be top-level `internal static partial class`** — not nested
3. **`[JSImport]` doesn't support `long` return** — use `int`
4. **KNI assembly names have no `nkast` prefix** — they're `Xna.Framework.*`, not `nkast.Xna.Framework.*`
5. **Trimmer strips facade-only KNI references** — add `<TrimmerRootAssembly>` for each KNI assembly
6. **WASM SDK fingerprinted filenames** — run scripts copy to stable paths
7. **`<WasmInlineBootConfig>true</WasmInlineBootConfig>`** — required to avoid `dotnet.boot.js` 404

(See `MEMORY.md` § "Critical Knowledge" for the full list with details.)

## Current State (as of v0.7.0-facade-works)

- Phase 2 complete: real SDV DLL loads via facade → KNI TypeForwardedTo
- Next: Phase 2.5 — call `Program.Main()` / instantiate `Game1` with VFS-backed ContentManager
- All headless tests pass (using MockSdv.Target as stand-in)

## When Asked to "Continue" or "按推荐优先级推进"

1. Read `MEMORY.md` § "Next Steps"
2. Invoke `Skill(command="superpowers-writing-plans")` to write a plan for the next phase
3. Execute the plan (inline or via subagent-driven-development)
4. Commit + tag + push at each milestone
5. Update `MEMORY.md` when the phase completes
