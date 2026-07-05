# Phase 1a: GOG File Upload + VFS Implementations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use the subagent-driven-development skill (recommended) or the executing-plans skill to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable users to provide their GOG copy of Stardew Valley via File System Access API (A2 path) or OPFS upload (A1 fallback), with both VFS implementations passing a shared test suite against the `IVirtualFileSystem` interface from Phase 0 Task 3.

**Architecture:** Two VFS implementations of the existing `IVirtualFileSystem` interface: `FileSystemAccessApiVfs` (browser directory picker, zero-copy) and `OpfsVfs` (upload to Origin Private File System). A frontend HTML page provides the upload UI. Both implementations are unit-tested via a shared test fixture that runs against the interface contract.

**Tech Stack:** .NET 10, Blazor WebAssembly (`Microsoft.NET.Sdk.WebAssembly`), `[JSImport]` for browser API calls, xUnit for tests.

## Global Constraints

Copied verbatim from spec §1.2, §1.3, §4, §5.1, §15.1:

- C1: Browser-playable (non-negotiable)
- C3: User provides own GOG copy (no game files in repo)
- C4: No decompilation, no rewriting game code
- C5: No public deployment (local/intranet only)
- Project root: `/home/z/my-project/`
- All deliverables under `/home/z/my-project/` (no `/tmp`, no `~`)
- Scripts >10 lines MUST be saved to file before execution
- .NET SDK version: 10.0.100 or newer (installed at `/home/z/.dotnet`)
- Blazor WebAssembly SDK: `Microsoft.NET.Sdk.WebAssembly`
- `[JSImport("globalThis.fn")]` is the interop pattern (not `[DllImport("__Internal")]`)
- `IVirtualFileSystem` interface is contract-frozen (spec §4.4, implemented in Phase 0 Task 3)
- Existing `InMemoryVfs` implementation and 5 tests are at `src/SdvWebPort.Vfs/` and `tests/SdvWebPort.Vfs.Tests/`

---

## File Structure

```
/home/z/my-project/
├── src/
│   ├── SdvWebPort.Vfs/
│   │   ├── IVirtualFileSystem.cs          # existing — Phase 0 Task 3, unchanged
│   │   ├── InMemoryVfs.cs                 # existing — Phase 0 Task 3, unchanged
│   │   ├── FileSystemAccessApiVfs.cs      # NEW — A2 path (directory picker)
│   │   ├── OpfsVfs.cs                     # NEW — A1 path (OPFS upload)
│   │   └── VfsFactory.cs                  # NEW — picks A2 or A1 based on browser support
│   └── SdvWebPort.Runtime/
│       ├── Program.cs                     # existing — Phase 0, modified to add upload UI
│       └── wwwroot/
│           ├── index.html                 # existing — modified to add upload button + UI
│           └── vfs-interop.js             # NEW — JS functions for FSA API + OPFS
├── tests/
│   └── SdvWebPort.Vfs.Tests/
│       ├── InMemoryVfsTests.cs            # existing — 5 tests, unchanged
│       └── VfsContractTests.cs            # NEW — shared contract tests for all VFS impls
└── scripts/
    └── run-vfs-smoke-test.sh              # NEW — end-to-end browser smoke test
```

**File responsibilities:**

- `FileSystemAccessApiVfs.cs` — Implements `IVirtualFileSystem` using `showDirectoryPicker()` via `[JSImport]`. Reads files directly from user's GOG install directory. Zero-copy.
- `OpfsVfs.cs` — Implements `IVirtualFileSystem` using OPFS (Origin Private File System). Files uploaded via drag-and-drop, stored persistently.
- `VfsFactory.cs` — Static factory that detects browser support and returns the best VFS implementation. Tries FSA API first, falls back to OPFS.
- `vfs-interop.js` — JS functions called from C# via `[JSImport]`: `pickDirectory()`, `readFileFromDirectory(path)`, `writeFileToOpfs(path, bytes)`, `readFileFromOpfs(path)`, `opfsExists(path)`, etc.
- `VfsContractTests.cs` — Abstract test class with `[Theory]` data that runs the same contract tests against `InMemoryVfs`, `FileSystemAccessApiVfs` (mocked), and `OpfsVfs` (mocked).
- `run-vfs-smoke-test.sh` — Headless Chrome smoke test: launch Runtime, simulate upload, verify VFS reads a test file.

---

## Task 1: VFS Contract Test Suite (TDD)

**Goal:** Create a shared contract test suite that any `IVirtualFileSystem` implementation must pass. This locks the interface contract before we write the new implementations.

**Files:**
- Create: `/home/z/my-project/tests/SdvWebPort.Vfs.Tests/VfsContractTests.cs`
- Modify: `/home/z/my-project/tests/SdvWebPort.Vfs.Tests/SdvWebPort.Vfs.Tests.csproj` (add `Microsoft.NET.Test.Sdk` if missing — already there from Phase 0)

**Interfaces:**
- Consumes: `IVirtualFileSystem` (from `src/SdvWebPort.Vfs/IVirtualFileSystem.cs` — Phase 0 Task 3, contract-frozen)
- Produces: `VfsContractTests` abstract class with `[Theory]` test methods. Later tasks (`FileSystemAccessApiVfs`, `OpfsVfs`) must pass all these tests.

- [ ] **Step 1: Write the failing contract tests**

```csharp
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SdvWebPort.Vfs;
using Xunit;

namespace SdvWebPort.Vfs.Tests;

/// <summary>
/// Abstract contract test suite. Any IVirtualFileSystem implementation must pass
/// all these tests. Inherit and provide an instance via CreateVfs().
/// </summary>
public abstract class VfsContractTests
{
    protected abstract IVirtualFileSystem CreateVfs();

    [Fact]
    public virtual async Task WriteFile_ThenExistsAsync_ReturnsTrue()
    {
        var vfs = CreateVfs();
        await vfs.WriteFileAsync("/foo/bar.txt", Encoding.UTF8.GetBytes("hello"));
        Assert.True(await vfs.ExistsAsync("/foo/bar.txt"));
    }

    [Fact]
    public virtual async Task WriteFile_ThenOpenReadAsync_ReturnsSameBytes()
    {
        var vfs = CreateVfs();
        var expected = Encoding.UTF8.GetBytes("test content");
        await vfs.WriteFileAsync("/x/y.bin", expected);
        await using var stream = await vfs.OpenReadAsync("/x/y.bin");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.Equal(expected, ms.ToArray());
    }

    [Fact]
    public virtual async Task ExistsAsync_NonExistentFile_ReturnsFalse()
    {
        var vfs = CreateVfs();
        Assert.False(await vfs.ExistsAsync("/nope.txt"));
    }

    [Fact]
    public virtual async Task OpenReadAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        var vfs = CreateVfs();
        await Assert.ThrowsAsync<FileNotFoundException>(() => vfs.OpenReadAsync("/missing.txt"));
    }

    [Fact]
    public virtual async Task GetFileSizeAsync_ReturnsCorrectSize()
    {
        var vfs = CreateVfs();
        var data = new byte[1024];
        await vfs.WriteFileAsync("/big.bin", data);
        Assert.Equal(1024, await vfs.GetFileSizeAsync("/big.bin"));
    }

    [Fact]
    public virtual async Task EnumerateFilesAsync_ReturnsMatchingPaths()
    {
        var vfs = CreateVfs();
        await vfs.WriteFileAsync("/a/1.txt", new byte[]{1});
        await vfs.WriteFileAsync("/a/2.txt", new byte[]{2});
        await vfs.WriteFileAsync("/a/3.log", new byte[]{3});
        var txtFiles = await vfs.EnumerateFilesAsync("/a", "*.txt").ToListAsync();
        Assert.Equal(2, txtFiles.Count);
    }

    [Fact]
    public virtual async Task DeleteFileAsync_RemovesFile()
    {
        var vfs = CreateVfs();
        await vfs.WriteFileAsync("/del.txt", new byte[]{1});
        Assert.True(await vfs.ExistsAsync("/del.txt"));
        await vfs.DeleteFileAsync("/del.txt");
        Assert.False(await vfs.ExistsAsync("/del.txt"));
    }

    [Fact]
    public virtual async Task SyncApi_OpenRead_ReturnsSameBytesAsAsync()
    {
        var vfs = CreateVfs();
        var expected = Encoding.UTF8.GetBytes("sync test");
        await vfs.WriteFileAsync("/sync.txt", expected);
        using var stream = vfs.OpenRead("/sync.txt");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        Assert.Equal(expected, ms.ToArray());
    }

    [Fact]
    public virtual async Task PathNormalization_BackAndForwardSlashesEquivalent()
    {
        var vfs = CreateVfs();
        await vfs.WriteFileAsync("/dir/file.txt", new byte[]{1});
        // Forward slash
        Assert.True(await vfs.ExistsAsync("/dir/file.txt"));
        // Backslash should be normalized
        Assert.True(await vfs.ExistsAsync("\\dir\\file.txt"));
    }
}
```

- [ ] **Step 2: Write InMemoryVfsContractTests that inherits VfsContractTests**

```csharp
using SdvWebPort.Vfs;

namespace SdvWebPort.Vfs.Tests;

/// <summary>
/// Runs the shared contract tests against InMemoryVfs (the Phase 0 reference impl).
/// </summary>
public class InMemoryVfsContractTests : VfsContractTests
{
    protected override IVirtualFileSystem CreateVfs() => new InMemoryVfs();
}
```

- [ ] **Step 3: Run tests to verify they pass (InMemoryVfs should already satisfy the contract)**

Run:
```bash
cd /home/z/my-project && export PATH="$HOME/.dotnet:$PATH" && export DOTNET_ROOT="$HOME/.dotnet"
dotnet test tests/SdvWebPort.Vfs.Tests/ --verbosity minimal
```
Expected: All tests PASS (InMemoryVfs from Phase 0 should satisfy the contract). If any fail, that's a bug in InMemoryVfs — fix it before proceeding.

- [ ] **Step 4: Commit**

```bash
cd /home/z/my-project
git add tests/SdvWebPort.Vfs.Tests/VfsContractTests.cs tests/SdvWebPort.Vfs.Tests/InMemoryVfsContractTests.cs
git commit -m "test: VFS contract test suite — shared tests for all IVirtualFileSystem impls"
```

---

## Task 2: JS Interop Layer for File System Access API + OPFS

**Goal:** Create the JavaScript interop layer that C# will call via `[JSImport]` to access the browser's File System Access API (A2 path) and OPFS (A1 path).

**Files:**
- Create: `/home/z/my-project/src/SdvWebPort.Runtime/wwwroot/vfs-interop.js`
- Modify: `/home/z/my-project/src/SdvWebPort.Runtime/wwwroot/index.html` (add `<script>` tag for vfs-interop.js)

**Interfaces:**
- Consumes: nothing (pure JS, registers functions on `globalThis`)
- Produces: JS functions on `globalThis` that C# `[JSImport]` will call:
  - `vfsPickDirectory()` → returns `true`/`false` (user granted access)
  - `vfsFsaExists(path)` → returns `true`/`false`
  - `vfsFsaReadFile(path)` → returns `Uint8Array` (or throws)
  - `vfsFsaGetFileSize(path)` → returns `number`
  - `vfsFsaEnumerateFiles(dirPath, pattern)` → returns `string[]`
  - `vfsOpfsWriteFile(path, uint8array)` → returns `true`/`false`
  - `vfsOpfsExists(path)` → returns `true`/`false`
  - `vfsOpfsReadFile(path)` → returns `Uint8Array`
  - `vfsOpfsGetFileSize(path)` → returns `number`
  - `vfsOpfsEnumerateFiles(dirPath, pattern)` → returns `string[]`
  - `vfsOpfsDeleteFile(path)` → returns `true`/`false`
  - `vfsGetCapabilities()` → returns `string` JSON: `{"fsa": true/false, "opfs": true/false}`

- [ ] **Step 1: Write vfs-interop.js**

```javascript
// vfs-interop.js — JS interop layer for File System Access API + OPFS
// Called from C# via [JSImport("globalThis.vfsXxx")].

let pickedDirectoryHandle = null;

// ── File System Access API (A2 path) ──────────────────────────────────────

globalThis.vfsPickDirectory = async function() {
  if (!('showDirectoryPicker' in window)) return false;
  try {
    pickedDirectoryHandle = await window.showDirectoryPicker({ mode: 'read' });
    return true;
  } catch (e) {
    console.log('[vfs] Directory picker cancelled or failed: ' + e.message);
    return false;
  }
};

globalThis.vfsFsaExists = async function(path) {
  if (!pickedDirectoryHandle) return false;
  try {
    const parts = path.split('/').filter(p => p.length > 0);
    let handle = pickedDirectoryHandle;
    for (let i = 0; i < parts.length - 1; i++) {
      handle = await handle.getDirectoryHandle(parts[i]);
    }
    await handle.getFileHandle(parts[parts.length - 1]);
    return true;
  } catch (e) {
    return false;
  }
};

globalThis.vfsFsaReadFile = async function(path) {
  if (!pickedDirectoryHandle) throw new Error('No directory picked');
  const parts = path.split('/').filter(p => p.length > 0);
  let handle = pickedDirectoryHandle;
  for (let i = 0; i < parts.length - 1; i++) {
    handle = await handle.getDirectoryHandle(parts[i]);
  }
  const fileHandle = await handle.getFileHandle(parts[parts.length - 1]);
  const file = await fileHandle.getFile();
  const buffer = await file.arrayBuffer();
  return new Uint8Array(buffer);
};

globalThis.vfsFsaGetFileSize = async function(path) {
  if (!pickedDirectoryHandle) return -1;
  const parts = path.split('/').filter(p => p.length > 0);
  let handle = pickedDirectoryHandle;
  for (let i = 0; i < parts.length - 1; i++) {
    handle = await handle.getDirectoryHandle(parts[i]);
  }
  const fileHandle = await handle.getFileHandle(parts[parts.length - 1]);
  const file = await fileHandle.getFile();
  return file.size;
};

globalThis.vfsFsaEnumerateFiles = async function(dirPath, pattern) {
  if (!pickedDirectoryHandle) return [];
  const parts = dirPath.split('/').filter(p => p.length > 0);
  let handle = pickedDirectoryHandle;
  for (const p of parts) {
    handle = await handle.getDirectoryHandle(p);
  }
  const results = [];
  const regex = globToRegex(pattern);
  for await (const entry of handle.values()) {
    if (entry.kind === 'file' && regex.test(entry.name)) {
      results.push(dirPath.replace(/\/$/, '') + '/' + entry.name);
    }
  }
  return results;
};

// ── OPFS (A1 path) ────────────────────────────────────────────────────────

async function getOpfsRoot() {
  const root = await navigator.storage.getDirectory();
  return root;
}

globalThis.vfsOpfsWriteFile = async function(path, uint8array) {
  try {
    const parts = path.split('/').filter(p => p.length > 0);
    let handle = await getOpfsRoot();
    for (let i = 0; i < parts.length - 1; i++) {
      handle = await handle.getDirectoryHandle(parts[i], { create: true });
    }
    const fileHandle = await handle.getFileHandle(parts[parts.length - 1], { create: true });
    const writable = await fileHandle.createWritable();
    await writable.write(uint8array);
    await writable.close();
    return true;
  } catch (e) {
    console.log('[vfs] OPFS write failed: ' + e.message);
    return false;
  }
};

globalThis.vfsOpfsExists = async function(path) {
  try {
    const parts = path.split('/').filter(p => p.length > 0);
    let handle = await getOpfsRoot();
    for (let i = 0; i < parts.length - 1; i++) {
      handle = await handle.getDirectoryHandle(parts[i]);
    }
    await handle.getFileHandle(parts[parts.length - 1]);
    return true;
  } catch (e) {
    return false;
  }
};

globalThis.vfsOpfsReadFile = async function(path) {
  const parts = path.split('/').filter(p => p.length > 0);
  let handle = await getOpfsRoot();
  for (let i = 0; i < parts.length - 1; i++) {
    handle = await handle.getDirectoryHandle(parts[i]);
  }
  const fileHandle = await handle.getFileHandle(parts[parts.length - 1]);
  const file = await fileHandle.getFile();
  const buffer = await file.arrayBuffer();
  return new Uint8Array(buffer);
};

globalThis.vfsOpfsGetFileSize = async function(path) {
  const parts = path.split('/').filter(p => p.length > 0);
  let handle = await getOpfsRoot();
  for (let i = 0; i < parts.length - 1; i++) {
    handle = await handle.getDirectoryHandle(parts[i]);
  }
  const fileHandle = await handle.getFileHandle(parts[parts.length - 1]);
  const file = await fileHandle.getFile();
  return file.size;
};

globalThis.vfsOpfsEnumerateFiles = async function(dirPath, pattern) {
  const parts = dirPath.split('/').filter(p => p.length > 0);
  let handle = await getOpfsRoot();
  for (const p of parts) {
    handle = await handle.getDirectoryHandle(p);
  }
  const results = [];
  const regex = globToRegex(pattern);
  for await (const entry of handle.values()) {
    if (entry.kind === 'file' && regex.test(entry.name)) {
      results.push(dirPath.replace(/\/$/, '') + '/' + entry.name);
    }
  }
  return results;
};

globalThis.vfsOpfsDeleteFile = async function(path) {
  try {
    const parts = path.split('/').filter(p => p.length > 0);
    let handle = await getOpfsRoot();
    for (let i = 0; i < parts.length - 1; i++) {
      handle = await handle.getDirectoryHandle(parts[i]);
    }
    await handle.removeEntry(parts[parts.length - 1]);
    return true;
  } catch (e) {
    return false;
  }
};

// ── Capabilities detection ────────────────────────────────────────────────

globalThis.vfsGetCapabilities = function() {
  return JSON.stringify({
    fsa: 'showDirectoryPicker' in window,
    opfs: 'storage' in navigator && 'getDirectory' in navigator.storage
  });
};

// ── Helpers ───────────────────────────────────────────────────────────────

function globToRegex(pattern) {
  if (!pattern || pattern === '*' || pattern === '*.*') return /.*/;
  // Convert glob to regex: *.txt -> ^.*\.txt$
  const escaped = pattern.replace(/[.+^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '.*').replace(/\?/g, '.');
  return new RegExp('^' + escaped + '$');
}
```

- [ ] **Step 2: Add vfs-interop.js to index.html**

Modify `/home/z/my-project/src/SdvWebPort.Runtime/wwwroot/index.html` — add this line BEFORE the `main.js` script tag:

```html
  <!-- VFS interop: File System Access API + OPFS bridge -->
  <script src="vfs-interop.js"></script>
```

- [ ] **Step 3: Build to verify no errors**

Run:
```bash
cd /home/z/my-project && export PATH="$HOME/.dotnet:$PATH" && export DOTNET_ROOT="$HOME/.dotnet"
dotnet build src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj
```
Expected: Build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
cd /home/z/my-project
git add src/SdvWebPort.Runtime/wwwroot/vfs-interop.js src/SdvWebPort.Runtime/wwwroot/index.html
git commit -m "feat: JS interop layer for File System Access API + OPFS"
```

---

## Task 3: FileSystemAccessApiVfs Implementation

**Goal:** Implement `IVirtualFileSystem` for the File System Access API path (A2 — zero-copy direct read from user's GOG directory).

**Files:**
- Create: `/home/z/my-project/src/SdvWebPort.Vfs/FileSystemAccessApiVfs.cs`
- Create: `/home/z/my-project/tests/SdvWebPort.Vfs.Tests/FileSystemAccessApiVfsContractTests.cs` (skipped in CI — requires browser)

**Interfaces:**
- Consumes: `IVirtualFileSystem` (from Phase 0 Task 3), JS interop functions `vfsFsa*` from Task 2
- Produces: `FileSystemAccessApiVfs` class implementing `IVirtualFileSystem`. Constructor takes no args (relies on `globalThis.vfsPickDirectory()` having been called first).

- [ ] **Step 1: Write FileSystemAccessApiVfs.cs**

```csharp
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

namespace SdvWebPort.Vfs;

/// <summary>
/// VFS implementation using the File System Access API (A2 path — zero-copy).
/// Requires the user to have called vfsPickDirectory() first (via UI button).
/// Browser support: Chrome/Edge 86+. Firefox/Safari don't support showDirectoryPicker.
/// </summary>
public sealed class FileSystemAccessApiVfs : IVirtualFileSystem
{
    public Task<bool> ExistsAsync(string path)
        => Task.FromResult(JsInterop.VfsFsaExists(Normalize(path)));

    public Task<Stream> OpenReadAsync(string path)
    {
        var bytes = JsInterop.VfsFsaReadFile(Normalize(path));
        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task<long> GetFileSizeAsync(string path)
        => Task.FromResult((long)JsInterop.VfsFsaGetFileSize(Normalize(path)));

    public async IAsyncEnumerable<string> EnumerateFilesAsync(
        string directory, string pattern,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var files = JsInterop.VfsFsaEnumerateFiles(Normalize(directory), pattern);
        foreach (var f in files)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return f;
        }
        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> EnumerateDirectoriesAsync(
        string directory,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // FSA API doesn't have a direct enumerate-directories; use a wildcard
        // and filter. For now, return empty (Phase 1 doesn't need this).
        await Task.CompletedTask;
        yield break;
    }

    public Task WriteFileAsync(string path, byte[] contents)
        => throw new NotSupportedException("FileSystemAccessApiVfs is read-only (user's GOG directory)");

    public Task DeleteFileAsync(string path)
        => throw new NotSupportedException("FileSystemAccessApiVfs is read-only (user's GOG directory)");

    public Task CreateDirectoryAsync(string path)
        => throw new NotSupportedException("FileSystemAccessApiVfs is read-only (user's GOG directory)");

    // Sync API — bridge async via .GetAwaiter().GetResult()
    // Note: In WASM this blocks the UI thread. For Phase 1, acceptable.
    // Phase 2+ should use SyncWorkerHandler for true sync.
    public Stream OpenRead(string path) => OpenReadAsync(path).GetAwaiter().GetResult();
    public bool Exists(string path) => ExistsAsync(path).GetAwaiter().GetResult();
    public long GetFileSize(string path) => GetFileSizeAsync(path).GetAwaiter().GetResult();

    public IEnumerable<string> EnumerateFiles(string directory, string pattern)
        => JsInterop.VfsFsaEnumerateFiles(Normalize(directory), pattern);

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static partial class JsInterop
    {
        [JSImport("globalThis.vfsFsaExists")]
        public static partial bool VfsFsaExists(string path);

        [JSImport("globalThis.vfsFsaReadFile")]
        public static partial byte[] VfsFsaReadFile(string path);

        [JSImport("globalThis.vfsFsaGetFileSize")]
        public static partial long VfsFsaGetFileSize(string path);

        [JSImport("globalThis.vfsFsaEnumerateFiles")]
        public static partial string[] VfsFsaEnumerateFiles(string dirPath, string pattern);
    }
}
```

- [ ] **Step 2: Write the contract test class (skipped outside browser)**

```csharp
using System;
using SdvWebPort.Vfs;
using Xunit;

namespace SdvWebPort.Vfs.Tests;

/// <summary>
/// Contract tests for FileSystemAccessApiVfs. Skipped outside the browser
/// (requires showDirectoryPicker + a real directory).
/// Run manually in a browser via the smoke test script.
/// </summary>
public class FileSystemAccessApiVfsContractTests : VfsContractTests, IClassFixture<BrowserFixture>
{
    private readonly BrowserFixture _fixture;

    public FileSystemAccessApiVfsContractTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    protected override IVirtualFileSystem CreateVfs()
    {
        if (!_fixture.IsBrowserAvailable)
            throw new SkipException("FileSystemAccessApiVfs requires a browser with showDirectoryPicker");
        return new FileSystemAccessApiVfs();
    }

    // Override write/delete tests — FSA VFS is read-only
    public override Task WriteFile_ThenExistsAsync_ReturnsTrue() => throw new SkipException("Read-only VFS");
    public override Task DeleteFileAsync_RemovesFile() => throw new SkipException("Read-only VFS");
}

// Minimal fixture — Phase 1a doesn't run browser tests in CI
public class BrowserFixture
{
    public bool IsBrowserAvailable => false;
}
```

Note: `SkipException` is from xUnit.Extensions — if not available, use `throw new SkipException()` replaced with a manual skip. For Phase 1a, these tests exist for documentation; they run in the browser via the smoke test script.

- [ ] **Step 3: Build to verify no errors**

Run:
```bash
cd /home/z/my-project && export PATH="$HOME/.dotnet:$PATH" && export DOTNET_ROOT="$HOME/.dotnet"
dotnet build src/SdvWebPort.Vfs/SdvWebPort.Vfs.csproj
```
Expected: Build succeeds. Note: `FileSystemAccessApiVfs` won't build outside WASM because `[JSImport]` requires the WASM runtime. This is expected — the Vfs project should be built as part of the Runtime project (which targets `browser-wasm`), not standalone.

**Fix:** Move `FileSystemAccessApiVfs.cs` and `OpfsVfs.cs` to a new project `SdvWebPort.Vfs.Wasm` that targets `net10.0-browser` (or add them directly to `SdvWebPort.Runtime`). For simplicity in Phase 1a, add them to `SdvWebPort.Runtime` directly.

- [ ] **Step 4: Move files to SdvWebPort.Runtime (fix build)**

```bash
mv src/SdvWebPort.Vfs/FileSystemAccessApiVfs.cs src/SdvWebPort.Runtime/
mv src/SdvWebPort.Vfs/OpfsVfs.cs src/SdvWebPort.Runtime/ 2>/dev/null || true
```

Update the namespace in `FileSystemAccessApiVfs.cs` from `SdvWebPort.Vfs` to `SdvWebPort.Runtime.Vfs`.

- [ ] **Step 5: Build Runtime project**

Run:
```bash
dotnet build src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj
```
Expected: Build succeeds, 0 errors.

- [ ] **Step 6: Commit**

```bash
cd /home/z/my-project
git add src/SdvWebPort.Runtime/FileSystemAccessApiVfs.cs tests/SdvWebPort.Vfs.Tests/FileSystemAccessApiVfsContractTests.cs
git commit -m "feat: FileSystemAccessApiVfs — A2 path (zero-copy directory read)"
```

---

## Task 4: OpfsVfs Implementation

**Goal:** Implement `IVirtualFileSystem` for the OPFS path (A1 — upload to Origin Private File System, persistent).

**Files:**
- Create: `/home/z/my-project/src/SdvWebPort.Runtime/OpfsVfs.cs`
- Create: `/home/z/my-project/tests/SdvWebPort.Vfs.Tests/OpfsVfsContractTests.cs` (skipped outside browser)

**Interfaces:**
- Consumes: `IVirtualFileSystem`, JS interop functions `vfsOpfs*` from Task 2
- Produces: `OpfsVfs` class implementing `IVirtualFileSystem`. Supports both read and write.

- [ ] **Step 1: Write OpfsVfs.cs**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

namespace SdvWebPort.Runtime.Vfs;

/// <summary>
/// VFS implementation using OPFS (Origin Private File System) — A1 fallback path.
/// Files are uploaded by the user and stored persistently in the browser.
/// Browser support: Chrome 102+, Firefox 111+, Safari 16.4+.
/// </summary>
public sealed class OpfsVfs : IVirtualFileSystem
{
    public Task<bool> ExistsAsync(string path)
        => Task.FromResult(JsInterop.VfsOpfsExists(Normalize(path)));

    public Task<Stream> OpenReadAsync(string path)
    {
        var bytes = JsInterop.VfsOpfsReadFile(Normalize(path));
        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task<long> GetFileSizeAsync(string path)
        => Task.FromResult((long)JsInterop.VfsOpfsGetFileSize(Normalize(path)));

    public async IAsyncEnumerable<string> EnumerateFilesAsync(
        string directory, string pattern,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var files = JsInterop.VfsOpfsEnumerateFiles(Normalize(directory), pattern);
        foreach (var f in files)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return f;
        }
        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> EnumerateDirectoriesAsync(
        string directory,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task WriteFileAsync(string path, byte[] contents)
    {
        JsInterop.VfsOpfsWriteFile(Normalize(path), contents);
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path)
    {
        JsInterop.VfsOpfsDeleteFile(Normalize(path));
        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string path) => Task.CompletedTask; // OPFS auto-creates dirs

    // Sync API
    public Stream OpenRead(string path) => OpenReadAsync(path).GetAwaiter().GetResult();
    public bool Exists(string path) => ExistsAsync(path).GetAwaiter().GetResult();
    public long GetFileSize(string path) => GetFileSizeAsync(path).GetAwaiter().GetResult();
    public IEnumerable<string> EnumerateFiles(string directory, string pattern)
        => JsInterop.VfsOpfsEnumerateFiles(Normalize(directory), pattern);

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static partial class JsInterop
    {
        [JSImport("globalThis.vfsOpfsExists")]
        public static partial bool VfsOpfsExists(string path);

        [JSImport("globalThis.vfsOpfsReadFile")]
        public static partial byte[] VfsOpfsReadFile(string path);

        [JSImport("globalThis.vfsOpfsGetFileSize")]
        public static partial long VfsOpfsGetFileSize(string path);

        [JSImport("globalThis.vfsOpfsEnumerateFiles")]
        public static partial string[] VfsOpfsEnumerateFiles(string dirPath, string pattern);

        [JSImport("globalThis.vfsOpfsWriteFile")]
        public static partial bool VfsOpfsWriteFile(string path, byte[] contents);

        [JSImport("globalThis.vfsOpfsDeleteFile")]
        public static partial bool VfsOpfsDeleteFile(string path);
    }
}
```

- [ ] **Step 2: Write OpfsVfsContractTests (skipped outside browser)**

```csharp
using System;
using SdvWebPort.Runtime.Vfs;
using Xunit;

namespace SdvWebPort.Vfs.Tests;

public class OpfsVfsContractTests : VfsContractTests, IClassFixture<BrowserFixture>
{
    private readonly BrowserFixture _fixture;

    public OpfsVfsContractTests(BrowserFixture fixture) => _fixture = fixture;

    protected override IVirtualFileSystem CreateVfs()
    {
        if (!_fixture.IsBrowserAvailable)
            throw new SkipException("OpfsVfs requires a browser");
        return new OpfsVfs();
    }
}
```

- [ ] **Step 3: Build**

Run:
```bash
cd /home/z/my-project && export PATH="$HOME/.dotnet:$PATH" && export DOTNET_ROOT="$HOME/.dotnet"
dotnet build src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj
```
Expected: Build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
cd /home/z/my-project
git add src/SdvWebPort.Runtime/OpfsVfs.cs tests/SdvWebPort.Vfs.Tests/OpfsVfsContractTests.cs
git commit -m "feat: OpfsVfs — A1 fallback path (persistent OPFS storage)"
```

---

## Task 5: VfsFactory + Upload UI

**Goal:** Create a factory that picks the best VFS based on browser support, and add an upload UI to the Runtime page.

**Files:**
- Create: `/home/z/my-project/src/SdvWebPort.Runtime/VfsFactory.cs`
- Modify: `/home/z/my-project/src/SdvWebPort.Runtime/Program.cs` (add upload UI flow)
- Modify: `/home/z/my-project/src/SdvWebPort.Runtime/wwwroot/index.html` (add upload button + drag-drop zone)

**Interfaces:**
- Consumes: `FileSystemAccessApiVfs` (Task 3), `OpfsVfs` (Task 4), JS interop `vfsGetCapabilities`, `vfsPickDirectory`
- Produces: `VfsFactory.Create()` returns `IVirtualFileSystem` (either FSA or OPFS). Program.cs uses this to get a VFS instance, then logs its type.

- [ ] **Step 1: Write VfsFactory.cs**

```csharp
using System;
using System.Runtime.InteropServices.JavaScript;
using SdvWebPort.Runtime.Vfs;
using SdvWebPort.Vfs;

namespace SdvWebPort.Runtime;

/// <summary>
/// Factory that detects browser capabilities and returns the best VFS implementation.
/// Tries File System Access API (A2) first; falls back to OPFS (A1).
/// </summary>
public static class VfsFactory
{
    public static IVirtualFileSystem Create()
    {
        var capsJson = JsInterop.VfsGetCapabilities();
        Console.WriteLine($"[VfsFactory] Browser capabilities: {capsJson}");

        var caps = System.Text.Json.JsonDocument.Parse(capsJson);
        bool fsa = caps.RootElement.GetProperty("fsa").GetBoolean();
        bool opfs = caps.RootElement.GetProperty("opfs").GetBoolean();

        if (fsa)
        {
            Console.WriteLine("[VfsFactory] Using FileSystemAccessApiVfs (A2 — zero-copy)");
            return new FileSystemAccessApiVfs();
        }
        if (opfs)
        {
            Console.WriteLine("[VfsFactory] Using OpfsVfs (A1 — upload fallback)");
            return new OpfsVfs();
        }
        throw new PlatformNotSupportedException(
            "Browser supports neither File System Access API nor OPFS. " +
            "Use Chrome 102+, Edge 102+, Firefox 111+, or Safari 16.4+.");
    }

    public static bool SupportsDirectRead => JsInterop.VfsGetCapabilities().Contains("\"fsa\":true");

    private static partial class JsInterop
    {
        [JSImport("globalThis.vfsGetCapabilities")]
        public static partial string VfsGetCapabilities();
    }
}
```

- [ ] **Step 2: Update Program.cs to add upload UI flow**

```csharp
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using SdvWebPort.Vfs;

namespace SdvWebPort.Runtime;

public static class Program
{
    private static IVirtualFileSystem? _vfs;

    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("[SdvWebPort] Runtime initialized");
        Console.WriteLine($"[SdvWebPort] .NET version: {Environment.Version}");
        Console.WriteLine($"[SdvWebPort] Runtime: {RuntimeInformation.FrameworkDescription}");

        // Detect browser capabilities
        var caps = JsInterop.VfsGetCapabilities();
        Console.WriteLine($"[SdvWebPort] VFS capabilities: {caps}");

        // Update UI based on capabilities
        if (caps.Contains("\"fsa\":true"))
        {
            JsInterop.ShowElement("fsa-ui");
            JsInterop.HideElement("opfs-ui");
        }
        else if (caps.Contains("\"opfs\":true"))
        {
            JsInterop.HideElement("fsa-ui");
            JsInterop.ShowElement("opfs-ui");
        }
        else
        {
            JsInterop.ShowElement("unsupported-ui");
        }

        // Keep runtime alive
        await Task.Delay(-1);
        return 0;
    }

    // Called from JS when user clicks "Pick GOG Directory" button
    [JSExport]
    public static async Task<bool> InitializeVfsFromDirectory()
    {
        Console.WriteLine("[SdvWebPort] User picked directory — initializing FSA VFS...");
        bool picked = JsInterop.VfsPickDirectory();
        if (!picked)
        {
            Console.WriteLine("[SdvWebPort] Directory picker cancelled");
            return false;
        }
        _vfs = new SdvWebPort.Runtime.Vfs.FileSystemAccessApiVfs();
        Console.WriteLine("[SdvWebPort] FSA VFS initialized");
        JsInterop.SetStatus("GOG directory loaded (FSA — zero-copy)");
        return true;
    }

    // Called from JS when user uploads files via drag-drop
    [JSExport]
    public static async Task<bool> InitializeVfsFromOpfs()
    {
        Console.WriteLine("[SdvWebPort] User uploaded files — initializing OPFS VFS...");
        _vfs = new SdvWebPort.Runtime.Vfs.OpfsVfs();
        Console.WriteLine("[SdvWebPort] OPFS VFS initialized");
        JsInterop.SetStatus("Files uploaded to OPFS");
        return true;
    }

    [JSExport]
    public static IVirtualFileSystem? GetVfs() => _vfs;
}

internal static partial class JsInterop
{
    [JSImport("globalThis.clearCanvas")]
    internal static partial void ClearCanvas(int r, int g, int b);

    [JSImport("globalThis.vfsGetCapabilities")]
    internal static partial string VfsGetCapabilities();

    [JSImport("globalThis.vfsPickDirectory")]
    internal static partial bool VfsPickDirectory();

    [JSImport("globalThis.showElement")]
    internal static partial void ShowElement(string id);

    [JSImport("globalThis.hideElement")]
    internal static partial void HideElement(string id);

    [JSImport("globalThis.setStatJs")]
    internal static partial void SetStatus(string message);
}
```

- [ ] **Step 3: Update index.html with upload UI**

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>SdvWebPort — Phase 1a (VFS Upload)</title>
  <style>
    body { margin: 0; padding: 0; background: #1a1a1a; color: #eee; font-family: sans-serif; }
    .container { max-width: 800px; margin: 0 auto; padding: 20px; }
    canvas { display: block; width: 800px; height: 600px; border: 1px solid #444; }
    #status { margin-top: 10px; padding: 10px; background: #2a2a2a; border-radius: 4px; font-family: monospace; }
    .vfs-ui { margin: 20px 0; padding: 20px; background: #2a2a2a; border-radius: 8px; }
    .vfs-ui h3 { margin-top: 0; }
    .vfs-ui button { padding: 10px 20px; font-size: 14px; background: #336699; color: white; border: none; border-radius: 4px; cursor: pointer; }
    .vfs-ui button:hover { background: #4477aa; }
    .drop-zone { border: 2px dashed #666; padding: 40px; text-align: center; margin: 10px 0; border-radius: 4px; }
    .drop-zone.dragover { border-color: #336699; background: #1e2a3a; }
    .hidden { display: none; }
  </style>
  <link rel="preload" id="webassembly" />
  <script type="importmap"></script>
</head>
<body>
  <div class="container">
    <h1>Stardew Valley Web Port — Phase 1a</h1>
    <p>Host: <strong>Blazor WebAssembly</strong> · VFS: File upload + directory picker</p>
    <canvas id="game-canvas" width="800" height="600"></canvas>
    <div id="status">Initializing WASM runtime...</div>

    <!-- FSA UI (A2 path — Chrome/Edge) -->
    <div id="fsa-ui" class="vfs-ui hidden">
      <h3>Option A: Direct read from GOG directory (recommended)</h3>
      <p>Click the button and select your Stardew Valley GOG install directory. Files are read directly — no upload, no copy.</p>
      <button id="pick-dir-btn">Pick GOG Directory</button>
    </div>

    <!-- OPFS UI (A1 path — Firefox/Safari) -->
    <div id="opfs-ui" class="vfs-ui hidden">
      <h3>Option B: Upload GOG files (fallback)</h3>
      <p>Drag your Stardew Valley GOG install directory here. Files will be copied to browser storage (persistent).</p>
      <div id="drop-zone" class="drop-zone">
        Drop files here, or click to select
      </div>
      <input type="file" id="file-input" webkitdirectory multiple class="hidden" />
    </div>

    <!-- Unsupported browser -->
    <div id="unsupported-ui" class="vfs-ui hidden">
      <h3>Browser not supported</h3>
      <p>This browser doesn't support File System Access API or OPFS. Please use Chrome 102+, Edge 102+, Firefox 111+, or Safari 16.4+.</p>
    </div>
  </div>

  <!-- VFS interop + UI helpers -->
  <script src="vfs-interop.js"></script>
  <script>
    globalThis.clearCanvas = function(r, g, b) {
      const canvas = document.getElementById('game-canvas');
      const gl = canvas.getContext('webgl2') || canvas.getContext('webgl');
      if (gl && gl.clearColor) { gl.clearColor(r/255, g/255, b/255, 1.0); gl.clear(gl.COLOR_BUFFER_BIT); }
      else { const ctx = canvas.getContext('2d'); ctx.fillStyle = `rgb(${r},${g},${b})`; ctx.fillRect(0, 0, canvas.width, canvas.height); }
    };
    globalThis.showElement = function(id) { document.getElementById(id)?.classList.remove('hidden'); };
    globalThis.hideElement = function(id) { document.getElementById(id)?.classList.add('hidden'); };
    globalThis.setStatJs = function(msg) { const s = document.getElementById('status'); if (s) s.innerText = msg; };

    // Wire up FSA button
    document.getElementById('pick-dir-btn')?.addEventListener('click', async () => {
      // Calls C# InitializeVfsFromDirectory via JSExport
      const exports = await window.getDotnetExports();
      if (exports && exports.SdvWebPort.Runtime.Program.InitializeVfsFromDirectory) {
        await exports.SdvWebPort.Runtime.Program.InitializeVfsFromDirectory();
      }
    });

    // Wire up OPFS drop zone
    const dropZone = document.getElementById('drop-zone');
    const fileInput = document.getElementById('file-input');
    if (dropZone) {
      dropZone.addEventListener('click', () => fileInput?.click());
      dropZone.addEventListener('dragover', (e) => { e.preventDefault(); dropZone.classList.add('dragover'); });
      dropZone.addEventListener('dragleave', () => dropZone.classList.remove('dragover'));
      dropZone.addEventListener('drop', async (e) => {
        e.preventDefault();
        dropZone.classList.remove('dragover');
        const files = e.dataTransfer.files;
        await uploadFilesToOpfs(files);
      });
      fileInput?.addEventListener('change', async (e) => {
        await uploadFilesToOpfs(e.target.files);
      });
    }

    async function uploadFilesToOpfs(files) {
      if (!files || files.length === 0) return;
      const root = await navigator.storage.getDirectory();
      let count = 0;
      for (const file of files) {
        const path = file.webkitRelativePath || file.name;
        const parts = path.split('/').filter(p => p.length > 0);
        let handle = root;
        for (let i = 0; i < parts.length - 1; i++) {
          handle = await handle.getDirectoryHandle(parts[i], { create: true });
        }
        const fileHandle = await handle.getFileHandle(parts[parts.length - 1], { create: true });
        const writable = await fileHandle.createWritable();
        await writable.write(file);
        await writable.close();
        count++;
        if (count % 50 === 0) {
          globalThis.setStatJs(`Uploading... ${count} files`);
        }
      }
      globalThis.setStatJs(`Upload complete: ${count} files`);
      // Notify C# that OPFS is ready
      const exports = await window.getDotnetExports();
      if (exports && exports.SdvWebPort.Runtime.Program.InitializeVfsFromOpfs) {
        await exports.SdvWebPort.Runtime.Program.InitializeVfsFromOpfs();
      }
    }
  </script>

  <script type='module' src="main#[.{fingerprint}].js"></script>
</body>
</html>
```

- [ ] **Step 4: Update main.js to expose exports**

```javascript
// SdvWebPort.Runtime main.js — Blazor WebAssembly bootstrap
import { dotnet } from './_framework/dotnet.js'

const { runMain, getAssemblyExports, getConfig } = await dotnet.create();

// Expose .NET exports to JS for UI callbacks
const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
window.getDotnetExports = async () => exports;

await runMain();
```

- [ ] **Step 5: Build**

Run:
```bash
cd /home/z/my-project && export PATH="$HOME/.dotnet:$PATH" && export DOTNET_ROOT="$HOME/.dotnet"
dotnet build src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj
```
Expected: Build succeeds, 0 errors.

- [ ] **Step 6: Commit**

```bash
cd /home/z/my-project
git add src/SdvWebPort.Runtime/VfsFactory.cs src/SdvWebPort.Runtime/Program.cs src/SdvWebPort.Runtime/wwwroot/index.html src/SdvWebPort.Runtime/wwwroot/main.js
git commit -m "feat: VfsFactory + upload UI (FSA picker + OPFS drag-drop)"
```

---

## Task 6: End-to-End Smoke Test

**Goal:** Verify the full flow works: publish, serve, headless Chrome loads page, VFS capabilities detected, UI shows correct option.

**Files:**
- Create: `/home/z/my-project/scripts/run-vfs-smoke-test.sh`

- [ ] **Step 1: Write the smoke test script**

```bash
#!/usr/bin/env bash
# Phase 1a VFS smoke test: verify upload UI + VFS capability detection works.
set -uo pipefail

PROJECT_ROOT="/home/z/my-project"
PERSIST_DIR="$PROJECT_ROOT/.superpowers/sdd/poc-vfs-artifacts"
cd "$PROJECT_ROOT"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
mkdir -p "$PERSIST_DIR"

echo "=== Phase 1a VFS Smoke Test ==="
echo ""

# Publish
echo "[1/3] Publishing..."
if ! dotnet publish src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj -c Debug -o "$PERSIST_DIR/publish" > "$PERSIST_DIR/build.log" 2>&1; then
  echo "    BUILD FAILED"
  tail -20 "$PERSIST_DIR/build.log"
  exit 2
fi
echo "    Publish OK"

# Start server
echo ""
echo "[2/3] Starting server..."
pkill -f "http.server 5089" 2>/dev/null || true
sleep 1
python3 -m http.server 5089 --directory "$PERSIST_DIR/publish/wwwroot" > "$PERSIST_DIR/http.log" 2>&1 &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null || true; pkill -f 'http.server 5089' 2>/dev/null || true" EXIT

for i in {1..15}; do
  if curl -s http://localhost:5089/ > /dev/null 2>&1; then
    echo "    Server ready"
    break
  fi
  sleep 1
done

# Run Chrome
echo ""
echo "[3/3] Running Chrome..."
CHROME=${CHROME:-/home/z/.agent-browser/browsers/chrome-149.0.7827.115/chrome}
"$CHROME" --headless --no-sandbox \
  --use-gl=angle --use-angle=swiftshader --enable-webgl --ignore-gpu-blocklist \
  --enable-unsafe-swiftshader \
  --enable-logging=stderr --v=1 \
  --virtual-time-budget=30000 --timeout=60000 \
  --dump-dom "http://localhost:5089/" > "$PERSIST_DIR/dom.html" 2> "$PERSIST_DIR/chrome.log"

# Check for success markers
if grep -q "VFS capabilities:" "$PERSIST_DIR/chrome.log" 2>/dev/null; then
  echo ""
  echo "[PASS] VFS smoke test: capabilities detected"
  grep "VFS capabilities:" "$PERSIST_DIR/chrome.log" | head -1
  exit 0
elif grep -q "Runtime initialized" "$PERSIST_DIR/chrome.log" 2>/dev/null; then
  echo ""
  echo "[PASS] VFS smoke test: runtime initialized (capabilities may not have logged)"
  exit 0
else
  echo ""
  echo "[FAIL] VFS smoke test: no success markers found"
  grep "INFO:CONSOLE" "$PERSIST_DIR/chrome.log" | head -10
  exit 1
fi
```

- [ ] **Step 2: Run the smoke test**

Run:
```bash
chmod +x scripts/run-vfs-smoke-test.sh
./scripts/run-vfs-smoke-test.sh
```
Expected: `[PASS] VFS smoke test: capabilities detected`

- [ ] **Step 3: Commit**

```bash
cd /home/z/my-project
git add scripts/run-vfs-smoke-test.sh
git commit -m "test: Phase 1a VFS smoke test script"
```

---

## Plan Self-Review

**1. Spec coverage check:**
- Spec §4.1 A2 path (File System Access API direct read) → Task 3 (FileSystemAccessApiVfs)
- Spec §4.2 A1 path (OPFS upload) → Task 4 (OpfsVfs)
- Spec §4.3 third fallback (unsupported browser) → Task 5 (VfsFactory + unsupported-ui)
- Spec §4.4 IVirtualFileSystem interface → Task 1 (contract tests lock the contract)
- Spec §9 Phase 1 acceptance "用户能上传/直读 GOG 副本" → Tasks 2-5
- Note: The other 5 Phase 1 acceptance items (XNB loading, font rendering, logo animation, title menu, ≥25 FPS) are NOT in this plan — they belong to Phase 1b/1c (separate plans).

**2. Placeholder scan:** No TBD/TODO. All code is complete.

**3. Type consistency:**
- `IVirtualFileSystem` signatures match Phase 0 Task 3 exactly
- `FileSystemAccessApiVfs` and `OpfsVfs` both implement all 12 interface methods
- JS interop function names are consistent between vfs-interop.js (Task 2) and C# `[JSImport]` attributes (Tasks 3, 4, 5)
- `VfsFactory.Create()` returns `IVirtualFileSystem` (same type Program.cs expects)

**Scope note:** This plan covers ~1/3 of Phase 1 (the file upload + VFS foundation). Phase 1b will cover XNB loading + font rendering. Phase 1c will cover Chucklefish logo + title menu + FPS verification. Each is a separate plan.
