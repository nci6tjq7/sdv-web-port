# SdvWebPort.Rewriter

Cecil-based IL rewriter that redirects SDV's `System.IO.File.*` and
`System.IO.Directory.*` calls to `SdvWebPort.Vfs.SdvFileShim.*` calls,
routing them through the `IVirtualFileSystem` (backed by the user's
uploaded GOG files).

## Why

Real SDV's `Game1` constructor and `Initialize()`/`LoadContent()` call
`File.OpenRead("Content/...")` etc., which throw `FileNotFoundException`
in WASM (no native FS). We intercept these calls and route them to VFS.

## How

Uses Mono.Cecil to scan the SDV DLL's IL for call instructions targeting
`System.IO.File::*` / `System.IO.Directory::*` and rewrites the operand
to point at `SdvFileShim::*` instead. Runs in-memory on the fetched DLL
bytes before `AssemblyLoadContext.LoadFromStream`.

## Rewriting rules

| Original | Redirected to |
|----------|---------------|
| `File.OpenRead(string)` | `SdvFileShim.OpenRead(string)` |
| `File.Exists(string)` | `SdvFileShim.Exists(string)` |
| `File.ReadAllBytes(string)` | `SdvFileShim.ReadAllBytes(string)` |
| `File.ReadAllText(string)` | `SdvFileShim.ReadAllText(string)` |
| `Directory.GetFiles(string)` | `SdvFileShim.GetFiles(string)` |
| `Directory.GetFiles(string, string)` | `SdvFileShim.GetFiles(string, string)` |
| `Directory.Exists(string)` | `SdvFileShim.DirectoryExists(string)` |

## Legal posture

The user's SDV.dll file on disk is NEVER modified. Only the in-memory
copy fetched via HttpClient is rewritten. This respects constraint C4
(no rewriting game code on disk).
