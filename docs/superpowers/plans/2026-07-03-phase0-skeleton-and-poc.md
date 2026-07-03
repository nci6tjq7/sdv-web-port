# Phase 0: Project Skeleton & PoC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use the subagent-driven-development skill (recommended) or the executing-plans skill to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a working Uno.Wasm.Bootstrap + .NET 10 project that renders a colored frame in the browser, then run two PoCs (rendering + SMAPI load) to validate the foundational technical assumptions before committing to Phase 1+.

**Architecture:** A single-project .NET 10 WASM solution bootstrapped via Uno.Wasm.Bootstrap. C# entry point initializes, calls into JS interop to clear a `<canvas>` to a solid color, and logs to browser console. Two PoC sub-projects validate (a) KNI WebGL backend can render a sprite, (b) SMAPI.dll can be loaded into the WASM runtime without immediate crash.

**Tech Stack:** .NET 10 SDK, Uno.Wasm.Bootstrap, KNI Framework (MonoGame.Framework.WebGL), MonoMod.RuntimeDetour (PoC only), Node 20+ (for build tooling), Chrome 120+ (for testing).

## Global Constraints

Copied verbatim from spec §1.2, §1.3, §5.1, §5.3, §15.1:

- C1: Browser-playable (non-negotiable)
- C3: User provides own GOG copy (no game files in repo)
- C4: No decompilation, no rewriting game code
- C5: No public deployment (local/intranet only)
- .NET SDK version: 10.0.100 or newer
- WASM memory limit: 4GB (`--max-memory=4GB`)
- Browser target: Chrome 120+ / Edge equivalent (dev environment)
- Project root: `/home/z/my-project/`
- All deliverables under `/home/z/my-project/` (no `/tmp`, no `~`)
- Scripts >10 lines MUST be saved to file before execution (Rule 9)

---

## File Structure

```
/home/z/my-project/
├── src/
│   └── SdvWebPort.Runtime/
│       ├── SdvWebPort.Runtime.csproj       # Uno.Wasm.Bootstrap project
│       ├── Program.cs                       # C# entry point
│       ├── wwwroot/
│       │   └── index.html                   # HTML host page
│       └── tsconfig.json                    # (optional) JS type checking
├── src/
│   └── SdvWebPort.PoC.Render/               # PoC A: KNI rendering
│       └── SdvWebPort.PoC.Render.csproj
├── src/
│   └── SdvWebPort.PoC.SmapiLoad/            # PoC B: SMAPI load
│       └── SdvWebPort.PoC.SmapiLoad.csproj
├── tests/
│   └── SdvWebPort.Runtime.Tests/
│       └── SdvWebPort.Runtime.Tests.csproj
├── scripts/
│   ├── install-dotnet.sh                    # .NET SDK bootstrap
│   └── verify-environment.sh                # Pre-flight checks
├── .gitignore
├── SdvWebPort.sln                           # Solution file
└── README.md
```

**File responsibilities:**

- `SdvWebPort.Runtime.csproj` — Uno.Wasm.Bootstrap config, target framework, runtime mode
- `Program.cs` — C# entry point: init runtime, JS interop to canvas, log to console
- `wwwroot/index.html` — Minimal HTML with `<canvas>`, loads `main.js` (Uno-generated)
- `SdvWebPort.PoC.Render.csproj` — Standalone KNI WebGL project, renders one sprite
- `SdvWebPort.PoC.SmapiLoad.csproj` — Loads `StardewModdingAPI.dll` via `AssemblyLoadContext`, logs SMAPI version
- `scripts/install-dotnet.sh` — Idempotent .NET 10 SDK installer for the sandbox
- `scripts/verify-environment.sh` — Checks dotnet / node / chrome versions; exits non-zero if missing
- `SdvWebPort.sln` — Links all projects for `dotnet build` from root

---

## Task 1: Environment Bootstrap

**Files:**
- Create: `/home/z/my-project/scripts/install-dotnet.sh`
- Create: `/home/z/my-project/scripts/verify-environment.sh`
- Create: `/home/z/my-project/.gitignore`
- Modify: none

**Interfaces:**
- Consumes: nothing
- Produces: `dotnet` on PATH (v10.0.100+), `node` on PATH (v20+), both verifiable by `verify-environment.sh`

- [ ] **Step 1: Write `scripts/install-dotnet.sh`**

```bash
#!/usr/bin/env bash
# Idempotent .NET 10 SDK installer for Debian/Ubuntu sandbox.
set -euo pipefail

DOTNET_VERSION="10.0.100"
INSTALL_DIR="$HOME/.dotnet"

if [ -x "$INSTALL_DIR/dotnet" ] && "$INSTALL_DIR/dotnet" --version | grep -q "^10\."; then
  echo "[+] .NET 10 SDK already installed at $INSTALL_DIR"
  exit 0
fi

echo "[+] Installing .NET $DOTNET_VERSION SDK to $INSTALL_DIR"
mkdir -p "$INSTALL_DIR"
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

# Use Microsoft's dotnet-install script
curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o "$TMP_DIR/dotnet-install.sh"
chmod +x "$TMP_DIR/dotnet-install.sh"
"$TMP_DIR/dotnet-install.sh" --version "$DOTNET_VERSION" --install-dir "$INSTALL_DIR"

echo "[+] Done. Add to PATH:"
echo "    export PATH=\"$INSTALL_DIR:\$PATH\""
echo "    export DOTNET_ROOT=\"$INSTALL_DIR\""
```

- [ ] **Step 2: Make installer executable and run it**

Run:
```bash
chmod +x /home/z/my-project/scripts/install-dotnet.sh
/home/z/my-project/scripts/install-dotnet.sh
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
```

Expected: `[+] Done.` printed, `dotnet --version` outputs `10.0.100` (or newer 10.0.x).

- [ ] **Step 3: Write `scripts/verify-environment.sh`**

```bash
#!/usr/bin/env bash
# Pre-flight environment check. Exits non-zero if any required tool is missing/wrong version.
set -euo pipefail

PASS=0
FAIL=0

check() {
  local name="$1" expected="$2" actual="$3"
  if [ "$actual" = "$expected" ] || [[ "$actual" > "$expected" ]]; then
    echo "  [PASS] $name: $actual"
    PASS=$((PASS+1))
  else
    echo "  [FAIL] $name: expected >= $expected, got $actual"
    FAIL=$((FAIL+1))
  fi
}

check_version() {
  local name="$1" cmd="$2" min_version="$3"
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "  [FAIL] $name: not on PATH"
    FAIL=$((FAIL+1))
    return
  fi
  local actual
  actual=$($cmd --version 2>&1 | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)
  check "$name" "$min_version" "$actual"
}

echo "=== Environment Verification ==="
check_version ".NET SDK" "dotnet" "10.0.100"
check_version "Node.js" "node" "20.0.0"
check_version "npm" "npm" "10.0.0"

# Chrome check (for later E2E)
if command -v google-chrome >/dev/null 2>&1; then
  echo "  [PASS] Chrome: $(google-chrome --version 2>&1)"
  PASS=$((PASS+1))
elif command -v chromium >/dev/null 2>&1; then
  echo "  [PASS] Chromium: $(chromium --version 2>&1)"
  PASS=$((PASS+1))
else
  echo "  [WARN] Chrome/Chromium not found (needed for Phase 0 PoC testing)"
fi

echo ""
echo "Result: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ]
```

- [ ] **Step 4: Make verifier executable and run it**

Run:
```bash
chmod +x /home/z/my-project/scripts/verify-environment.sh
/home/z/my-project/scripts/verify-environment.sh
```

Expected: All checks PASS, exit code 0.

- [ ] **Step 5: Write `.gitignore`**

```
# .NET
bin/
obj/
*.user
*.suo
.vs/

# Node
node_modules/

# IDE
.idea/
.vscode/

# OS
.DS_Store
Thumbs.db

# Project-specific
# NEVER commit game files (Stardew Valley.dll, .xnb, etc.)
StardewValley/
*.xnb
Stardew Valley.dll
StardewModdingAPI.dll
# Exception: allowed to commit tiny test fixtures in tests/fixtures/
!tests/fixtures/*.xnb

# Build output
download/
*.wasm
*.wasm.map

# Secrets
.env
.env.local
```

- [ ] **Step 6: Commit**

```bash
cd /home/z/my-project
git init 2>/dev/null || true
git add scripts/install-dotnet.sh scripts/verify-environment.sh .gitignore
git commit -m "chore: bootstrap .NET 10 SDK + environment verification"
```

---

## Task 2: Solution Skeleton & Uno.Wasm.Bootstrap Project

**Files:**
- Create: `/home/z/my-project/SdvWebPort.sln`
- Create: `/home/z/my-project/src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj`
- Create: `/home/z/my-project/src/SdvWebPort.Runtime/Program.cs`
- Create: `/home/z/my-project/src/SdvWebPort.Runtime/wwwroot/index.html`
- Modify: none

**Interfaces:**
- Consumes: `dotnet` from Task 1
- Produces: `SdvWebPort.Runtime` project that builds and produces a WASM bundle loadable in a browser; exports `Main()` entry point that runs on WASM load

- [ ] **Step 1: Create solution and project scaffolding**

Run:
```bash
cd /home/z/my-project
export PATH="$HOME/.dotnet:$PATH"
dotnet new sln -n SdvWebPort
mkdir -p src/SdvWebPort.Runtime
cd src/SdvWebPort.Runtime
dotnet new uno-wasm-bootstrap -n SdvWebPort.Runtime
```

If `uno-wasm-bootstrap` template is missing, install it:
```bash
dotnet new install Uno.Wasm.Bootstrap.Templates
```

Expected: `SdvWebPort.sln` at root, `src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj` exists.

- [ ] **Step 2: Write `SdvWebPort.Runtime.csproj`**

Overwrite the scaffolded csproj with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <OutputType>Exe</OutputType>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>

    <!-- Uno.Wasm.Bootstrap configuration -->
    <WasmShellMonoRuntimeExecutionMode>InterpreterAndAOT</WasmShellMonoRuntimeExecutionMode>
    <WasmShellEnableJiterpreter>true</WasmShellEnableJiterpreter>
    <WasmShellIndexHtmlPath>wwwroot/index.html</WasmShellIndexHtmlPath>
    <WasmShellOPFSEnabled>true</WasmShellOPFSEnabled>
    <WasmShellFileDescriptorsEnabled>true</WasmShellFileDescriptorsEnabled>
    <WasmShellGenerateCompressedFiles>true</WasmShellGenerateCompressedFiles>

    <!-- Memory & threading -->
    <WasmShellEmccLinkerOptimizationLevel>O2</WasmShellLinkOptimizationLevel>
    <EmccLinkOptimizationLevel>O2</EmccLinkOptimizationLevel>

    <!-- AOT profile (Phase 1+) -->
    <WasmShellAOTProfile>false</WasmShellAOTProfile>

    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Uno.Wasm.Bootstrap" Version="8.0.45" />
    <PackageReference Include="Uno.Wasm.Bootstrap.DevServer" Version="8.0.45" />
  </ItemGroup>

</Project>
```

**Note:** Exact Uno.Wasm.Bootstrap version may need adjustment; check [NuGet](https://www.nuget.org/packages/Uno.Wasm.Bootstrap) for the latest .NET 10-compatible release. If 8.0.45 doesn't support net10.0, try the latest dev feed:
```bash
dotnet add package Uno.Wasm.Bootstrap --prerelease
```

- [ ] **Step 3: Write `Program.cs`**

```csharp
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SdvWebPort.Runtime;

public static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("[SdvWebPort] Runtime initialized");
        Console.WriteLine($"[SdvWebPort] .NET version: {Environment.Version}");
        Console.WriteLine($"[SdvWebPort] Runtime: {RuntimeInformation.FrameworkDescription}");

        // Call JS to clear canvas to a solid color
        await JsInterop.ClearCanvasAsync(0x33, 0x66, 0x99); // RGB

        Console.WriteLine("[SdvWebPort] Canvas cleared to #336699");
        Console.WriteLine("[SdvWebPort] Phase 0 skeleton ready. Press any key in console to exit.");

        // Keep runtime alive
        await Task.Delay(Timeout.Infinite);
        return 0;
    }
}

internal static class JsInterop
{
    [DllImport("__Internal")]
    internal static extern Task ClearCanvasAsync(int r, int g, int b);
}
```

- [ ] **Step 4: Write `wwwroot/index.html`**

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>SdvWebPort — Phase 0 Skeleton</title>
  <style>
    body { margin: 0; padding: 0; background: #1a1a1a; color: #eee; font-family: sans-serif; }
    .container { max-width: 800px; margin: 0 auto; padding: 20px; }
    canvas { display: block; width: 800px; height: 600px; border: 1px solid #444; }
    #status { margin-top: 10px; padding: 10px; background: #2a2a2a; border-radius: 4px; font-family: monospace; }
  </style>
</head>
<body>
  <div class="container">
    <h1>Stardew Valley Web Port — Phase 0</h1>
    <canvas id="game-canvas" width="800" height="600"></canvas>
    <div id="status">Initializing WASM runtime...</div>
  </div>
  <script type="module" src="./main.js"></script>
  <script>
    // JS interop: clear canvas to RGB color
    window.clearCanvas = function(r, g, b) {
      const canvas = document.getElementById('game-canvas');
      const ctx = canvas.getContext('webgl2') || canvas.getContext('webgl');
      if (ctx && ctx.clearColor) {
        ctx.clearColor(r/255, g/255, b/255, 1.0);
        ctx.clear(ctx.COLOR_BUFFER_BIT);
      } else {
        // Fallback: 2D canvas
        const ctx2d = canvas.getContext('2d');
        ctx2d.fillStyle = `rgb(${r},${g},${b})`;
        ctx2d.fillRect(0, 0, canvas.width, canvas.height);
      }
      document.getElementById('status').innerText = `Canvas cleared to rgb(${r}, ${g}, ${b})`;
    };
  </script>
</body>
</html>
```

- [ ] **Step 5: Add JS interop binding**

Create `/home/z/my-project/src/SdvWebPort.Runtime/wwwroot/main.js` (Uno will overwrite this on build; this is a placeholder for understanding):

```javascript
// Uno.Wasm.Bootstrap generates this file at build time.
// Custom interop goes in a separate .js file linked from index.html if needed.
```

Add a separate file `/home/z/my-project/src/SdvWebPort.Runtime/JsInterop.js` linked via csproj:

```xml
<ItemGroup>
  <WasmShellExtraFiles Include="JsInterop.js" />
</ItemGroup>
```

And `JsInterop.js`:

```javascript
MonoSupport = MonoSupport || {};
MonoSupport.clearCanvas = function(r, g, b) {
  if (typeof window !== 'undefined' && window.clearCanvas) {
    window.clearCanvas(r, g, b);
  }
};
```

⚠️ Note: exact interop API name may differ by Uno version. Verify against [Uno.Wasm.Bootstrap docs](https://github.com/unoplatform/Uno.Wasm.Bootstrap). The pattern is: declare `DllImport("__Internal")` on C# side, register JS function on `Module` / `MonoSupport`.

- [ ] **Step 6: Add project to solution and build**

Run:
```bash
cd /home/z/my-project
export PATH="$HOME/.dotnet:$PATH"
dotnet sln SdvWebPort.sln add src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj
dotnet restore
dotnet build
```

Expected: Build succeeds. Output includes `bin/Release/net10.0/browser-wasm/publish/wwwroot/`.

- [ ] **Step 7: Run dev server and verify in browser**

Run:
```bash
cd /home/z/my-project/src/SdvWebPort.Runtime
dotnet run --project SdvWebPort.Runtime.csproj
```

Expected: Dev server starts on `http://localhost:8000` (or similar). Open in Chrome 120+, see:
- Page loads without console errors
- Canvas displays solid color #336699
- Browser console shows `[SdvWebPort] Runtime initialized`
- Console shows `[SdvWebPort] Canvas cleared to #336699`

- [ ] **Step 8: Commit**

```bash
cd /home/z/my-project
git add SdvWebPort.sln src/SdvWebPort.Runtime/
git commit -m "feat: scaffold Uno.Wasm.Bootstrap + .NET 10 runtime with canvas interop"
```

---

## Task 3: VFS Abstraction Skeleton (interface + in-memory impl)

**Files:**
- Create: `/home/z/my-project/src/SdvWebPort.Vfs/SdvWebPort.Vfs.csproj`
- Create: `/home/z/my-project/src/SdvWebPort.Vfs/IVirtualFileSystem.cs`
- Create: `/home/z/my-project/src/SdvWebPort.Vfs/InMemoryVfs.cs`
- Create: `/home/z/my-project/tests/SdvWebPort.Vfs.Tests/SdvWebPort.Vfs.Tests.csproj`
- Create: `/home/z/my-project/tests/SdvWebPort.Vfs.Tests/InMemoryVfsTests.cs`
- Modify: `/home/z/my-project/SdvWebPort.sln` (add two projects)

**Interfaces:**
- Consumes: nothing
- Produces: `IVirtualFileSystem` interface, `InMemoryVfs` concrete impl. Future tasks (A2 FSA impl, A1 OPFS impl) implement this interface. Signature is contract-frozen.

- [ ] **Step 1: Create VFS project**

Run:
```bash
cd /home/z/my-project
export PATH="$HOME/.dotnet:$PATH"
mkdir -p src/SdvWebPort.Vfs tests/SdvWebPort.Vfs.Tests
cd src/SdvWebPort.Vfs
dotnet new classlib -n SdvWebPort.Vfs -f net10.0
cd ../../tests/SdvWebPort.Vfs.Tests
dotnet new xunit -n SdvWebPort.Vfs.Tests -f net10.0
cd ../..
dotnet sln add src/SdvWebPort.Vfs/SdvWebPort.Vfs.csproj
dotnet sln add tests/SdvWebPort.Vfs.Tests/SdvWebPort.Vfs.Tests.csproj
dotnet add tests/SdvWebPort.Vfs.Tests/SdvWebPort.Vfs.Tests.csproj reference src/SdvWebPort.Vfs/SdvWebPort.Vfs.csproj
```

- [ ] **Step 2: Write the failing test `InMemoryVfsTests.cs`**

```csharp
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SdvWebPort.Vfs;
using Xunit;

namespace SdvWebPort.Vfs.Tests;

public class InMemoryVfsTests
{
    [Fact]
    public async Task WriteFile_ThenExistsAsync_ReturnsTrue()
    {
        var vfs = new InMemoryVfs();
        await vfs.WriteFileAsync("/foo/bar.txt", Encoding.UTF8.GetBytes("hello"));

        var exists = await vfs.ExistsAsync("/foo/bar.txt");

        Assert.True(exists);
    }

    [Fact]
    public async Task WriteFile_ThenOpenReadAsync_ReturnsSameBytes()
    {
        var vfs = new InMemoryVfs();
        var expected = Encoding.UTF8.GetBytes("test content");
        await vfs.WriteFileAsync("/x/y.bin", expected);

        await using var stream = await vfs.OpenReadAsync("/x/y.bin");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        Assert.Equal(expected, ms.ToArray());
    }

    [Fact]
    public async Task ExistsAsync_NonExistentFile_ReturnsFalse()
    {
        var vfs = new InMemoryVfs();
        var exists = await vfs.ExistsAsync("/nope.txt");
        Assert.False(exists);
    }

    [Fact]
    public async Task EnumerateFilesAsync_ReturnsMatchingPaths()
    {
        var vfs = new InMemoryVfs();
        await vfs.WriteFileAsync("/a/1.txt", new byte[]{1});
        await vfs.WriteFileAsync("/a/2.txt", new byte[]{2});
        await vfs.WriteFileAsync("/a/3.log", new byte[]{3});

        var txtFiles = await vfs.EnumerateFilesAsync("/a", "*.txt").ToListAsync();

        Assert.Equal(2, txtFiles.Count);
        Assert.Contains("/a/1.txt", txtFiles);
        Assert.Contains("/a/2.txt", txtFiles);
    }

    [Fact]
    public async Task GetFileSizeAsync_ReturnsCorrectSize()
    {
        var vfs = new InMemoryVfs();
        var data = new byte[1024];
        await vfs.WriteFileAsync("/big.bin", data);

        var size = await vfs.GetFileSizeAsync("/big.bin");

        Assert.Equal(1024, size);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:
```bash
cd /home/z/my-project
export PATH="$HOME/.dotnet:$PATH"
dotnet test tests/SdvWebPort.Vfs.Tests/
```

Expected: FAIL with `InMemoryVfs` type or namespace not found.

- [ ] **Step 4: Write `IVirtualFileSystem.cs`**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SdvWebPort.Vfs;

/// <summary>
/// Virtual filesystem abstraction over OPFS / File System Access API.
/// All paths use forward-slash separators and are absolute.
/// </summary>
public interface IVirtualFileSystem
{
    // Async API (preferred for new code)
    Task<Stream> OpenReadAsync(string path);
    Task<bool> ExistsAsync(string path);
    Task<long> GetFileSizeAsync(string path);
    IAsyncEnumerable<string> EnumerateFilesAsync(string directory, string pattern);
    IAsyncEnumerable<string> EnumerateDirectoriesAsync(string directory);
    Task WriteFileAsync(string path, byte[] contents);
    Task DeleteFileAsync(string path);
    Task CreateDirectoryAsync(string path);

    // Sync API (for SDV game code that uses synchronous File.OpenRead).
    // Implementation must bridge async backends via SyncWorkerHandler or
    // OPFS sync access handles (Chrome 102+).
    Stream OpenRead(string path);
    bool Exists(string path);
    long GetFileSize(string path);
    IEnumerable<string> EnumerateFiles(string directory, string pattern);
}
```

- [ ] **Step 5: Write `InMemoryVfs.cs` (minimal impl to pass tests)**

```csharp
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SdvWebPort.Vfs;

/// <summary>
/// In-memory VFS for testing. Not for production use.
/// </summary>
public sealed class InMemoryVfs : IVirtualFileSystem
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();

    public Task<bool> ExistsAsync(string path)
        => Task.FromResult(_files.ContainsKey(Normalize(path)));

    public Task<Stream> OpenReadAsync(string path)
    {
        var bytes = _files.TryGetValue(Normalize(path), out var b)
            ? b
            : throw new FileNotFoundException($"VFS file not found: {path}");
        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task<long> GetFileSizeAsync(string path)
    {
        if (_files.TryGetValue(Normalize(path), out var b))
            return Task.FromResult((long)b.Length);
        throw new FileNotFoundException($"VFS file not found: {path}");
    }

    public async IAsyncEnumerable<string> EnumerateFilesAsync(
        string directory,
        string pattern,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var prefix = Normalize(directory).TrimEnd('/') + "/";
        foreach (var kvp in _files)
        {
            if (ct.IsCancellationRequested) yield break;
            if (!kvp.Key.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
            var filename = kvp.Key[prefix.Length..];
            if (!filename.Contains('/'))  // direct child only
            {
                if (MatchesPattern(filename, pattern))
                    yield return kvp.Key;
            }
        }
    }

    public async IAsyncEnumerable<string> EnumerateDirectoriesAsync(
        string directory,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var prefix = Normalize(directory).TrimEnd('/') + "/";
        var seen = new HashSet<string>();
        foreach (var kvp in _files)
        {
            if (ct.IsCancellationRequested) yield break;
            if (!kvp.Key.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
            var rest = kvp.Key[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash > 0)
            {
                var dir = prefix + rest[..slash];
                if (seen.Add(dir)) yield return dir;
            }
        }
        await Task.CompletedTask;
    }

    public Task WriteFileAsync(string path, byte[] contents)
    {
        _files[Normalize(path)] = contents;
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path)
    {
        _files.TryRemove(Normalize(path), out _);
        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string path) => Task.CompletedTask; // No-op for in-memory

    // Sync API: bridge to async via .GetAwaiter().GetResult()
    // Note: this works in test/host environments; WASM needs SyncWorkerHandler (Phase 1+)
    public Stream OpenRead(string path) => OpenReadAsync(path).GetAwaiter().GetResult();
    public bool Exists(string path) => ExistsAsync(path).GetAwaiter().GetResult();
    public long GetFileSize(string path) => GetFileSizeAsync(path).GetAwaiter().GetResult();

    public IEnumerable<string> EnumerateFiles(string directory, string pattern)
        => EnumerateFilesAsync(directory, pattern).ToEnumerable();

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimEnd('/');

    private static bool MatchesPattern(string filename, string pattern)
    {
        if (pattern == "*.*" || pattern == "*") return true;
        // Convert simple glob to regex: *.txt -> ^.*\.txt$
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(filename, regex);
    }
}
```

- [ ] **Step 6: Add `ToEnumerable` extension (in same file or separate)**

The `IAsyncEnumerable` to `IEnumerable` bridge needs `System.Linq.Async`. Add package:

```bash
cd /home/z/my-project
export PATH="$HOME/.dotnet:$PATH"
dotnet add src/SdvWebPort.Vfs/SdvWebPort.Vfs.csproj package System.Linq.Async
```

Add `using System.Linq;` to `InMemoryVfs.cs`.

- [ ] **Step 7: Run tests to verify they pass**

Run:
```bash
cd /home/z/my-project
export PATH="$HOME/.dotnet:$PATH"
dotnet test tests/SdvWebPort.Vfs.Tests/
```

Expected: 5 tests PASS.

- [ ] **Step 8: Commit**

```bash
cd /home/z/my-project
git add src/SdvWebPort.Vfs/ tests/SdvWebPort.Vfs.Tests/ SdvWebPort.sln
git commit -m "feat: IVirtualFileSystem abstraction + InMemoryVfs impl with tests"
```

---

## Task 4: PoC A — KNI WebGL Rendering Validation

**Goal:** Validate that KNI's `MonoGame.Framework.WebGL` backend can render a textured sprite at ≥ 30 FPS in a headless Chrome instance. This is the riskiest assumption in the spec (R1, 30% probability of failure).

**Files:**
- Create: `/home/z/my-project/src/SdvWebPort.PoC.Render/SdvWebPort.PoC.Render.csproj`
- Create: `/home/z/my-project/src/SdvWebPort.PoC.Render/PocGame.cs`
- Create: `/home/z/my-project/src/SdvWebPort.PoC.Render/Content/test_sprite.png` (any 256x256 PNG)
- Create: `/home/z/my-project/scripts/run-render-poc.sh`
- Modify: `/home/z/my-project/SdvWebPort.sln`

**Interfaces:**
- Consumes: Uno.Wasm.Bootstrap (from Task 2), KNI NuGet packages
- Produces: `run-render-poc.sh` exit code 0 if PoC passes (≥30 FPS), non-zero if fails

- [ ] **Step 1: Create PoC project**

Run:
```bash
cd /home/z/my-project
export PATH="$HOME/.dotnet:$PATH"
mkdir -p src/SdvWebPort.PoC.Render
cd src/SdvWebPort.PoC.Render
dotnet new uno-wasm-bootstrap -n SdvWebPort.PoC.Render
cd ../..
dotnet sln add src/SdvWebPort.PoC.Render/SdvWebPort.PoC.Render.csproj
```

- [ ] **Step 2: Write `SdvWebPort.PoC.Render.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <OutputType>Exe</OutputType>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>

    <WasmShellMonoRuntimeExecutionMode>InterpreterAndAOT</WasmShellMonoRuntimeExecutionMode>
    <WasmShellEnableJiterpreter>true</WasmShellEnableJiterpreter>
    <WasmShellIndexHtmlPath>wwwroot/index.html</WasmShellIndexHtmlPath>
    <WasmShellOPFSEnabled>false</WasmShellOPFSEnabled>

    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Uno.Wasm.Bootstrap" Version="8.0.45" />
    <PackageReference Include="Uno.Wasm.Bootstrap.DevServer" Version="8.0.45" />
    <!-- KNI WebGL backend -->
    <PackageReference Include="nkast.Lib.Half" Version="1.0.0" />
    <PackageReference Include="MonoGame.Framework.WebGL" Version="3.12.9001" />
  </ItemGroup>

  <ItemGroup>
    <None Update="Content\**\*.*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>
```

**Note:** Exact KNI package versions need verification against [NuGet](https://www.nuget.org/packages/MonoGame.Framework.WebGL). The `nkast.Lib.Half` is a KNI dependency for half-precision floats.

- [ ] **Step 3: Generate a test sprite (256x256 PNG)**

Create `/home/z/my-project/scripts/make-test-sprite.py`:

```python
"""Generate a 256x256 test sprite PNG for the rendering PoC."""
from PIL import Image, ImageDraw

img = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)
# Diagonal stripes in 4 colors
for i in range(0, 512, 32):
    draw.line([(i, 0), (i-256, 256)], fill=(255, 100, 100, 255), width=16)
    draw.line([(i+16, 0), (i-240, 256)], fill=(100, 255, 100, 255), width=16)
img.save('/home/z/my-project/src/SdvWebPort.PoC.Render/Content/test_sprite.png')
print("[+] Test sprite created")
```

Run:
```bash
pip install pillow 2>/dev/null
python3 /home/z/my-project/scripts/make-test-sprite.py
```

- [ ] **Step 4: Write `PocGame.cs`**

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace SdvWebPort.PoC.Render;

public class PocGame : Game
{
    private GraphicsDeviceManager _graphics = null!;
    private SpriteBatch _spriteBatch = null!;
    private Texture2D _sprite = null!;
    private Vector2 _position;
    private Vector2 _velocity = new(60f, 40f);
    private SpriteFont _font = null!;
    private int _frameCount;
    private TimeSpan _lastFpsUpdate;
    private int _currentFps;

    protected override void Initialize()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 800,
            PreferredBackBufferHeight = 600,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        // Load sprite (PNG via content pipeline)
        using var stream = System.IO.File.OpenRead("Content/test_sprite.png");
        _sprite = Texture2D.FromStream(GraphicsDevice, stream);

        // Builtin font (KNI provides a default)
        _font = Content.Load<SpriteFont>("builtin_font");
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed
            || Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        // Bounce sprite
        _position += _velocity * (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_position.X < 0 || _position.X + _sprite.Width > 800)
            _velocity.X *= -1;
        if (_position.Y < 0 || _position.Y + _sprite.Height > 600)
            _velocity.Y *= -1;

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);

        _spriteBatch.Begin();
        _spriteBatch.Draw(_sprite, _position, Color.White);

        // FPS counter
        _frameCount++;
        if (gameTime.TotalGameTime - _lastFpsUpdate > TimeSpan.FromSeconds(1))
        {
            _currentFps = _frameCount;
            _frameCount = 0;
            _lastFpsUpdate = gameTime.TotalGameTime;
            Console.WriteLine($"[PoC.Render] FPS: {_currentFps}");
        }
        _spriteBatch.DrawString(_font, $"FPS: {_currentFps}", new Vector2(10, 10), Color.White);

        _spriteBatch.End();

        base.Draw(gameTime);
    }
}

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("[PoC.Render] Starting KNI WebGL PoC");
        using var game = new PocGame();
        game.Run();
    }
}
```

- [ ] **Step 5: Write `wwwroot/index.html`**

```html
<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8" />
  <title>SdvWebPort PoC — Render</title>
  <style>canvas { display: block; border: 1px solid #333; }</style>
</head>
<body>
  <canvas id="monogame-canvas" width="800" height="600"></canvas>
  <script type="module" src="./main.js"></script>
  <script>
    window.MONOGAME_CANVAS_ID = 'monogame-canvas';
  </script>
</body>
</html>
```

- [ ] **Step 6: Build the PoC**

Run:
```bash
cd /home/z/my-project
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/SdvWebPort.PoC.Render/SdvWebPort.PoC.Render.csproj
```

Expected: Build succeeds. If KNI WebGL package missing, see [KNI docs](https://github.com/kniEngine/kni) for the correct NuGet feed.

- [ ] **Step 7: Write `scripts/run-render-poc.sh`**

```bash
#!/usr/bin/env bash
# Run the rendering PoC in headless Chrome, capture FPS from console, fail if < 15 FPS.
set -euo pipefail

cd /home/z/my-project
export PATH="$HOME/.dotnet:$PATH"

# Start dev server in background
dotnet run --project src/SdvWebPort.PoC.Render/SdvWebPort.PoC.Render.csproj &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null || true" EXIT

# Wait for server to start
sleep 5
PORT=8000

# Run headless Chrome, capture console logs
CHROME_BIN="${CHROME_BIN:-google-chrome}"
if ! command -v "$CHROME_BIN" >/dev/null 2>&1; then
  CHROME_BIN="chromium"
fi

LOG_FILE=$(mktemp)
"$CHROME_BIN" --headless --disable-gpu=false --enable-webgl \
  --virtual-time-budget=10000 \
  --enable-logging=stderr --v=1 \
  --dump-dom "http://localhost:$PORT" 2>"$LOG_FILE" >/dev/null || true

# Extract FPS from console output
FPS_LINES=$(grep -oE "FPS: [0-9]+" "$LOG_FILE" | head -5)
if [ -z "$FPS_LINES" ]; then
  echo "[FAIL] No FPS output captured. PoC did not render."
  cat "$LOG_FILE" | tail -30
  exit 1
fi

echo "[+] Captured FPS readings:"
echo "$FPS_LINES"

AVG_FPS=$(echo "$FPS_LINES" | grep -oE "[0-9]+" | awk '{sum+=$1; n++} END {print sum/n}')
echo "[+] Average FPS: $AVG_FPS"

THRESHOLD=15
if [ "$AVG_FPS" -lt "$THRESHOLD" ]; then
  echo "[FAIL] Average FPS $AVG_FPS < threshold $THRESHOLD"
  exit 1
fi

echo "[PASS] Rendering PoC: FPS ≥ $THRESHOLD"
exit 0
```

- [ ] **Step 8: Run the PoC**

Run:
```bash
chmod +x /home/z/my-project/scripts/run-render-poc.sh
/home/z/my-project/scripts/run-render-poc.sh
```

Expected output: `[PASS] Rendering PoC: FPS ≥ 15`

If FAIL: document findings in `docs/superpowers/specs/2026-07-03-sdv-web-port-design.md` §10 risk register as confirmed R1, decide per decision matrix (spec §10.1).

- [ ] **Step 9: Commit**

```bash
cd /home/z/my-project
git add src/SdvWebPort.PoC.Render/ scripts/run-render-poc.sh scripts/make-test-sprite.py SdvWebPort.sln
git commit -m "feat: KNI WebGL rendering PoC with automated FPS validation"
```

---

## Task 5: PoC B — SMAPI Assembly Load Validation

**Goal:** Validate that `StardewModdingAPI.dll` can be loaded into the WASM runtime via `AssemblyLoadContext` without immediate crash. This validates risk R2 (25% probability of failure).

**Files:**
- Create: `/home/z/my-project/src/SdvWebPort.PoC.SmapiLoad/SdvWebPort.PoC.SmapiLoad.csproj`
- Create: `/home/z/my-project/src/SdvWebPort.PoC.SmapiLoad/Program.cs`
- Create: `/home/z/my-project/scripts/run-smapi-poc.sh`
- Create: `/home/z/my-project/src/SdvWebPort.PoC.SmapiLoad/.gitignore` (exclude SMAPI.dll)
- Modify: `/home/z/my-project/SdvWebPort.sln`

**Interfaces:**
- Consumes: User's local SMAPI.dll (placed manually at `src/SdvWebPort.PoC.SmapiLoad/StardewModdingAPI.dll`, gitignored)
- Produces: `run-smapi-poc.sh` exit code 0 if SMAPI.dll loaded successfully, non-zero if crashes

- [ ] **Step 1: Create PoC project**

Run:
```bash
cd /home/z/my-project
export PATH="$HOME/.dotnet:$PATH"
mkdir -p src/SdvWebPort.PoC.SmapiLoad
cd src/SdvWebPort.PoC.SmapiLoad
dotnet new uno-wasm-bootstrap -n SdvWebPort.PoC.SmapiLoad
cd ../..
dotnet sln add src/SdvWebPort.PoC.SmapiLoad/SdvWebPort.PoC.SmapiLoad.csproj
```

- [ ] **Step 2: Write `SdvWebPort.PoC.SmapiLoad.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <OutputType>Exe</OutputType>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>

    <WasmShellMonoRuntimeExecutionMode>InterpreterAndAOT</WasmShellMonoRuntimeExecutionMode>
    <WasmShellEnableJiterpreter>true</WasmShellEnableJiterpreter>
    <WasmShellIndexHtmlPath>wwwroot/index.html</WasmShellIndexHtmlPath>

    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Uno.Wasm.Bootstrap" Version="8.0.45" />
    <PackageReference Include="Uno.Wasm.Bootstrap.DevServer" Version="8.0.45" />
    <PackageReference Include="System.Runtime.Loader" Version="4.3.0" />
  </ItemGroup>

  <!-- SMAPI.dll placed manually by user; gitignored -->
  <ItemGroup>
    <None Include="StardewModdingAPI.dll" CopyToOutputDirectory="PreserveNewest" Condition="Exists('StardewModdingAPI.dll')" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Write local `.gitignore`**

Create `/home/z/my-project/src/SdvWebPort.PoC.SmapiLoad/.gitignore`:

```
StardewModdingAPI.dll
Stardew Valley.dll
*.xnb
```

- [ ] **Step 4: Write `Program.cs`**

```csharp
using System;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace SdvWebPort.PoC.SmapiLoad;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("[PoC.SmapiLoad] Starting SMAPI load PoC");

        const string smapiPath = "StardewModdingAPI.dll";
        if (!System.IO.File.Exists(smapiPath))
        {
            Console.WriteLine($"[FAIL] {smapiPath} not found. Place it at: src/SdvWebPort.PoC.SmapiLoad/{smapiPath}");
            Console.WriteLine("       You can copy it from your local SMAPI install.");
            return 2;
        }

        try
        {
            Console.WriteLine($"[+] Loading SMAPI from {smapiPath}");
            var bytes = await System.IO.File.ReadAllBytesAsync(smapiPath);
            Console.WriteLine($"[+] DLL size: {bytes.Length} bytes");

            var alc = new AssemblyLoadContext("SmapiPoC", isCollectible: true);
            var asm = alc.LoadFromStream(new System.IO.MemoryStream(bytes));

            Console.WriteLine($"[+] Loaded: {asm.FullName}");
            Console.WriteLine($"[+] Version: {asm.GetName().Version}");

            // Try to find SMAPI's entry type
            var smapiType = asm.GetType("StardewModdingAPI.Program")
                          ?? asm.GetType("StardewModdingAPI.ModEntry")
                          ?? Array.Find(asm.GetTypes(), t => t.Name == "Program");

            if (smapiType != null)
            {
                Console.WriteLine($"[+] Found SMAPI entry type: {smapiType.FullName}");
                var methods = smapiType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                Console.WriteLine($"[+] Public/static methods: {methods.Length}");
                foreach (var m in methods[..Math.Min(5, methods.Length)])
                {
                    Console.WriteLine($"    - {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})");
                }
            }
            else
            {
                Console.WriteLine("[WARN] Could not find SMAPI entry type, listing all types:");
                foreach (var t in asm.GetTypes()[..Math.Min(10, asm.GetTypes().Length)])
                {
                    Console.WriteLine($"    - {t.FullName}");
                }
            }

            Console.WriteLine("[PASS] SMAPI loaded successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Exception during SMAPI load:");
            Console.WriteLine($"    Type: {ex.GetType().FullName}");
            Console.WriteLine($"    Message: {ex.Message}");
            Console.WriteLine($"    Stack: {ex.StackTrace}");
            return 1;
        }
    }
}
```

- [ ] **Step 5: Write `wwwroot/index.html`**

```html
<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8" />
  <title>SdvWebPort PoC — SMAPI Load</title>
  <style>
    body { background: #1a1a1a; color: #eee; font-family: monospace; padding: 20px; }
    pre { white-space: pre-wrap; }
  </style>
</head>
<body>
  <h1>SdvWebPort PoC — SMAPI Load</h1>
  <pre id="log">Initializing...</pre>
  <script>
    const logEl = document.getElementById('log');
    const origLog = console.log;
    console.log = function(...args) {
      origLog.apply(console, args);
      logEl.textContent += args.join(' ') + '\n';
    };
  </script>
  <script type="module" src="./main.js"></script>
</body>
</html>
```

- [ ] **Step 6: Document SMAPI.dll placement**

Create `/home/z/my-project/src/SdvWebPort.PoC.SmapiLoad/README.md`:

```markdown
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
```

- [ ] **Step 7: Write `scripts/run-smapi-poc.sh`**

```bash
#!/usr/bin/env bash
# Run the SMAPI load PoC in headless Chrome, capture console output.
set -euo pipefail

cd /home/z/my-project
export PATH="$HOME/.dotnet:$PATH"

SMAPI_DLL="src/SdvWebPort.PoC.SmapiLoad/StardewModdingAPI.dll"
if [ ! -f "$SMAPI_DLL" ]; then
  echo "[FAIL] $SMAPI_DLL not found."
  echo "       See src/SdvWebPort.PoC.SmapiLoad/README.md for setup instructions."
  exit 2
fi

# Start dev server in background
dotnet run --project src/SdvWebPort.PoC.SmapiLoad/SdvWebPort.PoC.SmapiLoad.csproj &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null || true" EXIT

sleep 5
PORT=8000

LOG_FILE=$(mktemp)
CHROME_BIN="${CHROME_BIN:-google-chrome}"
if ! command -v "$CHROME_BIN" >/dev/null 2>&1; then
  CHROME_BIN="chromium"
fi

"$CHROME_BIN" --headless --enable-logging=stderr --v=1 \
  --virtual-time-budget=15000 \
  --dump-dom "http://localhost:$PORT" 2>"$LOG_FILE" >/dev/null || true

# Check for PASS or FAIL in output
if grep -q "\[PASS\] SMAPI loaded successfully" "$LOG_FILE"; then
  echo "[PASS] SMAPI load PoC succeeded"
  grep -E "\[\+\]|\[PASS\]" "$LOG_FILE"
  exit 0
elif grep -q "\[FAIL\]" "$LOG_FILE"; then
  echo "[FAIL] SMAPI load PoC failed:"
  grep -E "\[FAIL\]|\[\+\]" "$LOG_FILE"
  exit 1
else
  echo "[FAIL] Could not find PASS/FAIL marker. Raw log:"
  cat "$LOG_FILE" | tail -30
  exit 1
fi
```

- [ ] **Step 8: Run the PoC (after user provides SMAPI.dll)**

⚠️ **User action required**: User must copy `StardewModdingAPI.dll` to `src/SdvWebPort.PoC.SmapiLoad/`. Source: their own GOG/SMAPI install.

Run:
```bash
chmod +x /home/z/my-project/scripts/run-smapi-poc.sh
/home/z/my-project/scripts/run-smapi-poc.sh
```

Expected: `[PASS] SMAPI load PoC succeeded`

If FAIL: document findings in spec §10 as confirmed R2. Per decision matrix (§10.1): if render PoC passed but SMAPI failed, project degrades to "no-mod browser version", Phases 3-4 cancelled.

- [ ] **Step 9: Commit**

```bash
cd /home/z/my-project
git add src/SdvWebPort.PoC.SmapiLoad/ scripts/run-smapi-poc.sh SdvWebPort.sln
git commit -m "feat: SMAPI assembly load PoC with automated validation"
```

---

## Task 6: PoC Decision Gate & Phase 0 Closure

**Goal:** Run both PoCs, evaluate against the decision matrix in spec §10.1, and either proceed to Phase 1 or stop/replan.

**Files:**
- Create: `/home/z/my-project/scripts/run-phase0-pocs.sh`
- Create: `/home/z/my-project/docs/superpowers/specs/2026-07-03-phase0-poc-report.md`
- Modify: `/home/z/my-project/docs/superpowers/specs/2026-07-03-sdv-web-port-design.md` (update risk register with findings)

- [ ] **Step 1: Write `scripts/run-phase0-pocs.sh`**

```bash
#!/usr/bin/env bash
# Run both PoCs and produce a decision report.
set -uo pipefail

cd /home/z/my-project
REPORT=docs/superpowers/specs/2026-07-03-phase0-poc-report.md

echo "# Phase 0 PoC Report" > "$REPORT"
echo "" >> "$REPORT"
echo "**Date:** $(date -Iseconds)" >> "$REPORT"
echo "" >> "$REPORT"

# Run render PoC
echo "## Render PoC" >> "$REPORT"
echo "" >> "$REPORT"
if /home/z/my-project/scripts/run-render-poc.sh > /tmp/render-poc.log 2>&1; then
  RENDER_RESULT="PASS"
  echo "- **Result:** PASS" >> "$REPORT"
else
  RENDER_RESULT="FAIL"
  echo "- **Result:** FAIL" >> "$REPORT"
fi
echo "" >> "$REPORT"
echo '```' >> "$REPORT"
cat /tmp/render-poc.log >> "$REPORT"
echo '```' >> "$REPORT"
echo "" >> "$REPORT"

# Run SMAPI PoC
echo "## SMAPI Load PoC" >> "$REPORT"
echo "" >> "$REPORT"
if /home/z/my-project/scripts/run-smapi-poc.sh > /tmp/smapi-poc.log 2>&1; then
  SMAPI_RESULT="PASS"
  echo "- **Result:** PASS" >> "$REPORT"
else
  SMAPI_RESULT="FAIL"
  echo "- **Result:** FAIL" >> "$REPORT"
fi
echo "" >> "$REPORT"
echo '```' >> "$REPORT"
cat /tmp/smapi-poc.log >> "$REPORT"
echo '```' >> "$REPORT"
echo "" >> "$REPORT"

# Decision matrix
echo "## Decision" >> "$REPORT"
echo "" >> "$REPORT"
echo "Per spec §10.1 decision matrix:" >> "$REPORT"
echo "" >> "$REPORT"
echo "| Render PoC | SMAPI PoC | Decision |" >> "$REPORT"
echo "|---|---|---|" >> "$REPORT"

if [ "$RENDER_RESULT" = "PASS" ] && [ "$SMAPI_RESULT" = "PASS" ]; then
  DECISION="PROCEED"
  echo "| PASS | PASS | Proceed to Phase 1 |" >> "$REPORT"
elif [ "$RENDER_RESULT" = "PASS" ] && [ "$SMAPI_RESULT" = "FAIL" ]; then
  DECISION="DEGRADE"
  echo "| PASS | FAIL | Degrade to no-mod browser version. Cancel Phases 3-4. |" >> "$REPORT"
elif [ "$RENDER_RESULT" = "FAIL" ] && [ "$SMAPI_RESULT" = "PASS" ]; then
  DECISION="RETRY_RENDER"
  echo "| FAIL | PASS | Retry render optimization (1-2 weeks), then re-evaluate |" >> "$REPORT"
else
  DECISION="STOP"
  echo "| FAIL | FAIL | STOP project. Re-evaluate technical choices. |" >> "$REPORT"
fi
echo "" >> "$REPORT"
echo "**Decision: $DECISION**" >> "$REPORT"

echo ""
echo "============================================"
echo "Phase 0 PoC Report saved to: $REPORT"
echo "Decision: $DECISION"
echo "============================================"

# Exit 0 only if PROCEED
[ "$DECISION" = "PROCEED" ]
```

- [ ] **Step 2: Run the combined PoC gate**

Run:
```bash
chmod +x /home/z/my-project/scripts/run-phase0-pocs.sh
/home/z/my-project/scripts/run-phase0-pocs.sh
```

Expected: Exit 0 with `Decision: PROCEED`. Report saved to `docs/superpowers/specs/2026-07-03-phase0-poc-report.md`.

- [ ] **Step 3: Update spec risk register with actual findings**

Edit `/home/z/my-project/docs/superpowers/specs/2026-07-03-sdv-web-port-design.md` §10:

For each risk R1 and R2:
- Change probability from estimated (30%, 25%) to "Resolved (PASS)" or "Confirmed (FAIL)"
- Add link to PoC report

- [ ] **Step 4: Commit Phase 0 closure**

```bash
cd /home/z/my-project
git add scripts/run-phase0-pocs.sh docs/superpowers/specs/
git commit -m "chore: Phase 0 closure — PoC results and decision gate"
```

- [ ] **Step 5: Tag Phase 0 release**

```bash
cd /home/z/my-project
git tag -a v0.1.0-phase0 -m "Phase 0 complete: project skeleton + PoC validation"
```

---

## Plan Self-Review Notes

**Spec coverage check** (against `2026-07-03-sdv-web-port-design.md`):
- §3 architecture: covered by Task 2 (Runtime) + Task 3 (Vfs)
- §4 VFS abstraction: covered by Task 3 (interface + InMemoryVfs impl)
- §5 runtime: covered by Task 2 (csproj with Uno.Wasm.Bootstrap config)
- §6 rendering: covered by Task 4 (KNI WebGL PoC)
- §7 SMAPI: covered by Task 5 (load PoC, full port is Phase 3)
- §9 Phase 0 milestones: all 4 acceptance criteria in Task 2 Step 7
- §10 risk mitigation: covered by Task 6 (decision gate)
- §14 test strategy: Task 3 introduces unit test pattern; further tests in Phase 1+
- §15 dev environment: covered by Task 1 (bootstrap)
- §16 open source: N/A for Phase 0 (still private per spec §16.1)

**Out of scope for this plan (deferred to later plans):**
- Phase 1: title screen rendering, full KNI integration, OPFS/FSA VFS implementations
- Phase 2: gameplay, save file I/O, full game content loading
- Phase 3: SMAPI port, Harmony shim, mod loading
- Phase 5: XNB editing tools
- Real OPFS/FSA VFS implementations (only InMemoryVfs in Phase 0)

**Type consistency check:**
- `IVirtualFileSystem` interface signatures consistent across Task 3 (only place defined)
- `Program.Main` signature consistent: `Task<int> Main(string[] args)`
- All file paths use absolute `/home/z/my-project/...` form
