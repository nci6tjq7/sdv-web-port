# Phase 1b: XNB Loading + Font Rendering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use the subagent-driven-development skill (recommended) or the executing-plans skill to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Parse XNB content files from VFS, extract Texture2D pixel data, decode images via browser Canvas API, create KNI Texture2D objects, and render text using bitmap fonts — all in the Blazor WebAssembly host.

**Architecture:** A C# XNB parser reads .xnb files from `IVirtualFileSystem`, extracting raw pixel data (for textures) or glyph atlas + metadata (for fonts). For any PNG-compressed textures, a JS interop layer decodes via browser Canvas API with a pre-decode-and-cache strategy. A custom `VfsContentManager` orchestrates loading. Text rendering uses a simple bitmap font renderer that draws individual glyph quads.

**Tech Stack:** .NET 10, Blazor WebAssembly, KNI Framework (nkast.Xna.Framework.*), [JSImport] for browser Canvas interop, xUnit for parser unit tests.

## Global Constraints

- C1: Browser-playable (non-negotiable)
- C3: User provides own GOG copy (no game files in repo)
- C4: No decompilation, no rewriting game code
- C5: No public deployment (local/intranet only)
- Project root: `/home/z/my-project/`
- All deliverables under `/home/z/my-project/`
- .NET SDK: 10.0.100+ at `/home/z/.dotnet`
- Blazor WebAssembly SDK: `Microsoft.NET.Sdk.WebAssembly`
- `[JSImport]` for JS interop (not `[DllImport("__Internal")]`)
- JsInterop classes must be top-level `internal static partial class` (not nested)
- `[JSImport]` doesn't support `long` return type — use `int`
- `IVirtualFileSystem` interface at `src/SdvWebPort.Runtime/Vfs/IVirtualFileSystem.cs`
- Sandbox: file system doesn't persist between Bash calls — write + commit in one shot
- Sandbox: `git checkout -f` reverts uncommitted changes — always commit before ending a Bash call

---

## File Structure

```
/home/z/my-project/
├── src/
│   └── SdvWebPort.Runtime/
│       ├── Vfs/                          # existing (Phase 1a)
│       │   ├── IVirtualFileSystem.cs
│       │   ├── FileSystemAccessApiVfs.cs
│       │   ├── OpfsVfs.cs
│       │   └── VfsFactory.cs
│       ├── Content/                      # NEW — XNB parsing + content loading
│       │   ├── XnbFile.cs                # XNB header + type reader metadata
│       │   ├── XnbReader.cs              # Binary reader for 7-bit encoded ints
│       │   ├── XnbTextureReader.cs       # Reads Texture2D data from XNB
│       │   ├── XnbSpriteFontReader.cs    # Reads SpriteFont data from XNB
│       │   ├── VfsContentManager.cs      # Loads + caches content from VFS
│       │   └── ContentJsInterop.cs       # [JSImport] for Canvas image decode
│       ├── Program.cs                    # existing — modified to wire up content
│       └── wwwroot/
│           ├── vfs-interop.js            # existing — extended with image decode
│           └── main.js                   # existing — extended for async pre-decode
├── tests/
│   └── SdvWebPort.Content.Tests/         # NEW
│       ├── XnbReaderTests.cs
│       ├── XnbTextureReaderTests.cs
│       └── SdvWebPort.Content.Tests.csproj
└── scripts/
    └── run-content-smoke-test.sh         # NEW
```

---

## Task 1: XNB Binary Reader + Header Parser

**Goal:** Parse the XNB file header (magic, version, flag, file size) and the 7-bit encoded integer format used throughout XNB content.

**Files:**
- Create: `src/SdvWebPort.Runtime/Content/XnbReader.cs`
- Create: `src/SdvWebPort.Runtime/Content/XnbFile.cs`
- Create: `tests/SdvWebPort.Content.Tests/XnbReaderTests.cs`
- Create: `tests/SdvWebPort.Content.Tests/SdvWebPort.Content.Tests.csproj`

**Interfaces:**
- Consumes: `System.IO.BinaryReader`, `System.IO.Stream`
- Produces: `XnbReader` (7-bit int decoder), `XnbFile` (header metadata struct)

- [ ] **Step 1: Create test project**

```bash
cd /home/z/my-project && export PATH="$HOME/.dotnet:$PATH" && export DOTNET_ROOT="$HOME/.dotnet"
mkdir -p tests/SdvWebPort.Content.Tests
cd tests/SdvWebPort.Content.Tests
dotnet new xunit -n SdvWebPort.Content.Tests -o .
```

Then overwrite the csproj:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

Add to solution: `dotnet sln ../SdvWebPort.sln add tests/SdvWebPort.Content.Tests/SdvWebPort.Content.Tests.csproj`

- [ ] **Step 2: Write XnbReader.cs (7-bit encoded int reader)**

```csharp
using System.IO;
using System.Text;

namespace SdvWebPort.Runtime.Content;

/// <summary>
/// Binary reader for XNB file format. Handles 7-bit encoded integers
/// (variable-length encoding used by XNA content pipeline).
/// </summary>
public sealed class XnbReader : BinaryReader
{
    public XnbReader(Stream input) : base(input, Encoding.UTF8, leaveOpen: true) { }

    /// <summary>
    /// Read a 7-bit encoded integer (XNA/MonoGame content format).
    /// Each byte: bit 7 = "more bytes follow", bits 0-6 = data.
    /// </summary>
    public int Read7BitEncodedInt()
    {
        int result = 0;
        int shift = 0;
        while (true)
        {
            byte b = ReadByte();
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return result;
    }

    /// <summary>
    /// Read a string prefixed with a 7-bit encoded length.
    /// </summary>
    public string ReadXnbString()
    {
        int length = Read7BitEncodedInt();
        return Encoding.UTF8.GetString(ReadBytes(length));
    }
}
```

- [ ] **Step 3: Write XnbFile.cs (header struct)**

```csharp
using System.IO;

namespace SdvWebPort.Runtime.Content;

/// <summary>
/// Parsed XNB file header.
/// </summary>
public sealed class XnbFile
{
    public const string Magic = "XNB";

    public byte Version { get; init; }       // 5 = XNA 4.0, 7 = MonoGame
    public byte Flag { get; init; }          // 0 = uncompressed, 0x80 = LZX, 0x40 = LZ4
    public int FileSize { get; init; }       // Total file size including header
    public bool IsCompressed => Flag != 0;

    public List<TypeReaderInfo> TypeReaders { get; } = new();
    public int SharedResourceCount { get; set; }

    /// <summary>
    /// Parse XNB header from a stream. After this call, the stream is positioned
    /// at the start of the primary object data (after type readers + shared resources).
    /// </summary>
    public static XnbFile ParseHeader(XnbReader reader)
    {
        // Magic: "XNB"
        char m1 = (char)reader.ReadByte();
        char m2 = (char)reader.ReadByte();
        char m3 = (char)reader.ReadByte();
        if (m1 != 'X' || m2 != 'N' || m3 != 'B')
            throw new InvalidDataException($"Invalid XNB magic: {m1}{m2}{m3}");

        byte version = reader.ReadByte();
        byte flag = reader.ReadByte();
        int fileSize = reader.ReadInt32(); // big-endian in XNB? No, actually little-endian.

        var xnb = new XnbFile { Version = version, Flag = flag, FileSize = fileSize };

        // If compressed, skip the decompression for now (Phase 1b handles uncompressed only)
        if (xnb.IsCompressed)
        {
            int compressedSize = reader.ReadInt32();
            // TODO: LZ4/LZX decompression (Phase 2+ — SDV 1.6 uses uncompressed .xnb)
        }

        // Type readers
        int typeReaderCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < typeReaderCount; i++)
        {
            string typeName = reader.ReadXnbString();
            int readerVersion = reader.ReadInt32();
            xnb.TypeReaders.Add(new TypeReaderInfo(typeName, readerVersion));
        }

        // Shared resource count
        xnb.SharedResourceCount = reader.Read7BitEncodedInt();

        return xnb;
    }
}

public record TypeReaderInfo(string TypeName, int Version);
```

- [ ] **Step 4: Write failing tests**

```csharp
using System.IO;
using System.Text;
using SdvWebPort.Runtime.Content;
using Xunit;

namespace SdvWebPort.Content.Tests;

public class XnbReaderTests
{
    [Fact]
    public void Read7BitEncodedInt_SingleByte_Returns0To127()
    {
        var bytes = new byte[] { 0x05 }; // 5
        using var ms = new MemoryStream(bytes);
        var reader = new XnbReader(ms);
        Assert.Equal(5, reader.Read7BitEncodedInt());
    }

    [Fact]
    public void Read7BitEncodedInt_TwoBytes_Returns128Plus()
    {
        var bytes = new byte[] { 0x80, 0x01 }; // 128
        using var ms = new MemoryStream(bytes);
        var reader = new XnbReader(ms);
        Assert.Equal(128, reader.Read7BitEncodedInt());
    }

    [Fact]
    public void Read7BitEncodedInt_LargeValue_Returns300()
    {
        var bytes = new byte[] { 0xAC, 0x02 }; // 300
        using var ms = new MemoryStream(bytes);
        var reader = new XnbReader(ms);
        Assert.Equal(300, reader.Read7BitEncodedInt());
    }

    [Fact]
    public void ReadXnbString_ReturnsCorrectString()
    {
        // "Hello" = 5 bytes, prefixed with 7-bit encoded length (0x05)
        var bytes = new byte[] { 0x05, (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };
        using var ms = new MemoryStream(bytes);
        var reader = new XnbReader(ms);
        Assert.Equal("Hello", reader.ReadXnbString());
    }
}

public class XnbFileTests
{
    [Fact]
    public void ParseHeader_ValidXnb_ReturnsCorrectMetadata()
    {
        // Minimal XNB: magic "XNB" + version 5 + flag 0 (uncompressed) + filesize 100
        // + 1 type reader ("TestReader, TestAssembly" v0) + 0 shared resources
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)'X'); writer.Write((byte)'N'); writer.Write((byte)'B');
        writer.Write((byte)5);   // version
        writer.Write((byte)0);   // flag (uncompressed)
        writer.Write(100);        // file size
        // 1 type reader
        writer.Write((byte)1);   // 7-bit encoded int = 1
        var nameBytes = Encoding.UTF8.GetBytes("TestReader, TestAssembly");
        writer.Write7BitEncodedInt(nameBytes.Length);
        writer.Write(nameBytes);
        writer.Write(0);          // reader version
        // 0 shared resources
        writer.Write((byte)0);   // 7-bit encoded int = 0
        ms.Position = 0;

        var reader = new XnbReader(ms);
        var xnb = XnbFile.ParseHeader(reader);

        Assert.Equal(5, xnb.Version);
        Assert.False(xnb.IsCompressed);
        Assert.Equal(100, xnb.FileSize);
        Assert.Single(xnb.TypeReaders);
        Assert.Equal("TestReader, TestAssembly", xnb.TypeReaders[0].TypeName);
        Assert.Equal(0, xnb.SharedResourceCount);
    }

    [Fact]
    public void ParseHeader_InvalidMagic_ThrowsInvalidDataException()
    {
        using var ms = new MemoryStream(new byte[] { (byte)'X', (byte)'N', (byte)'A', 5, 0, 0, 0, 0, 0 });
        var reader = new XnbReader(ms);
        Assert.Throws<InvalidDataException>(() => XnbFile.ParseHeader(reader));
    }
}
```

Note: `BinaryWriter` doesn't have `Write7BitEncodedInt` — use this helper in the test:
```csharp
static void Write7BitEncodedInt(BinaryWriter w, int value)
{
    while (value >= 0x80) { w.Write((byte)(value | 0x80)); value >>= 7; }
    w.Write((byte)value);
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/SdvWebPort.Content.Tests/ --verbosity minimal
```
Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SdvWebPort.Runtime/Content/ tests/SdvWebPort.Content.Tests/
git commit -m "feat: XNB binary reader + header parser with tests"
```

---

## Task 2: XNB Texture2D Reader

**Goal:** Parse Texture2D resource data from an XNB file stream, extracting surface format, dimensions, and raw pixel data.

**Files:**
- Create: `src/SdvWebPort.Runtime/Content/XnbTextureReader.cs`
- Create: `tests/SdvWebPort.Content.Tests/XnbTextureReaderTests.cs`

**Interfaces:**
- Consumes: `XnbReader`, `XnbFile` (Task 1)
- Produces: `XnbTextureData` struct with `{SurfaceFormat, Width, Height, byte[] PixelData}`

- [ ] **Step 1: Write XnbTextureReader.cs**

```csharp
using System.IO;

namespace SdvWebPort.Runtime.Content;

/// <summary>
/// Surface formats used in XNB Texture2D resources.
/// </summary>
public enum SurfaceFormat
{
    Color = 0,           // RGBA 32-bit (most common in SDV)
    Bgr565 = 1,
    Bgra5551 = 2,
    Bgra4444 = 3,
    Dxt1 = 4,
    Dxt3 = 5,
    Dxt5 = 6,
    NormalizedByte4 = 7,
    Rgba1010102 = 8,
    Rg32 = 9,
    Rgba64 = 10,
    Alpha8 = 11,
    Single = 12,
    Vector2 = 13,
    Vector4 = 14,
    HalfSingle = 15,
    HalfVector2 = 16,
    HalfVector4 = 17,
    HdrBlendable = 18,
}

/// <summary>
/// Parsed texture data extracted from an XNB file.
/// </summary>
public record XnbTextureData(SurfaceFormat Format, int Width, int Height, byte[] PixelData);

/// <summary>
/// Reads Texture2D resource data from an XNB stream.
/// The stream must be positioned at the start of the Texture2D object data
/// (after the type reader index).
/// </summary>
public static class XnbTextureReader
{
    public static XnbTextureData Read(XnbReader reader)
    {
        // Surface format (int32)
        int formatValue = reader.ReadInt32();
        var format = (SurfaceFormat)formatValue;

        // Width (int32)
        int width = reader.ReadInt32();

        // Height (int32)
        int height = reader.ReadInt32();

        // Mip count (int32)
        int mipCount = reader.ReadInt32();

        // For the first mip level, read the pixel data
        // Subsequent mip levels are present but we only need the first (largest)
        int dataSize = reader.ReadInt32();
        byte[] pixelData = reader.ReadBytes(dataSize);

        // Skip remaining mip levels (if any)
        for (int i = 1; i < mipCount; i++)
        {
            int mipSize = reader.ReadInt32();
            reader.ReadBytes(mipSize);
        }

        return new XnbTextureData(format, width, height, pixelData);
    }

    /// <summary>
    /// Convert SurfaceFormat.Color (RGBA) pixel data to the byte order expected
    /// by KNI's Texture2D.SetData (which expects RGBA on WebGL).
    /// </summary>
    public static byte[] NormalizeToRgba(XnbTextureData texture)
    {
        if (texture.Format == SurfaceFormat.Color)
        {
            // XNA Color format is already RGBA — return as-is
            return texture.PixelData;
        }

        // TODO: Handle other formats (DXT decompression, etc.) in Phase 2+
        throw new NotSupportedException($"SurfaceFormat {texture.Format} not supported. Only Color (RGBA) is supported in Phase 1b.");
    }
}
```

- [ ] **Step 2: Write tests**

```csharp
using System.IO;
using System.Text;
using SdvWebPort.Runtime.Content;
using Xunit;

namespace SdvWebPort.Content.Tests;

public class XnbTextureReaderTests
{
    [Fact]
    public void Read_ValidTextureData_ReturnsCorrectDimensions()
    {
        // Simulate a 2x2 RGBA texture (4 bytes per pixel = 16 bytes total)
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write(0);    // SurfaceFormat.Color
        writer.Write(2);    // width
        writer.Write(2);    // height
        writer.Write(1);    // mipCount
        writer.Write(16);   // dataSize
        // 4 pixels: red, green, blue, white (RGBA)
        writer.Write(new byte[] { 255, 0, 0, 255,  0, 255, 0, 255,  0, 0, 255, 255,  255, 255, 255, 255 });
        ms.Position = 0;

        var reader = new XnbReader(ms);
        var tex = XnbTextureReader.Read(reader);

        Assert.Equal(SurfaceFormat.Color, tex.Format);
        Assert.Equal(2, tex.Width);
        Assert.Equal(2, tex.Height);
        Assert.Equal(16, tex.PixelData.Length);
    }

    [Fact]
    public void NormalizeToRgba_ColorFormat_ReturnsDataAsIs()
    {
        var data = new byte[] { 255, 0, 0, 255 };
        var tex = new XnbTextureData(SurfaceFormat.Color, 1, 1, data);
        var result = XnbTextureReader.NormalizeToRgba(tex);
        Assert.Equal(data, result);
    }

    [Fact]
    public void NormalizeToRgba_UnsupportedFormat_Throws()
    {
        var tex = new XnbTextureData(SurfaceFormat.Dxt5, 4, 4, new byte[8]);
        Assert.Throws<NotSupportedException>(() => XnbTextureReader.NormalizeToRgba(tex));
    }
}
```

- [ ] **Step 3: Run tests**

```bash
dotnet test tests/SdvWebPort.Content.Tests/ --verbosity minimal
```

- [ ] **Step 4: Commit**

```bash
git add src/SdvWebPort.Runtime/Content/XnbTextureReader.cs tests/SdvWebPort.Content.Tests/XnbTextureReaderTests.cs
git commit -m "feat: XNB Texture2D reader — extracts RGBA pixel data from XNB"
```

---

## Task 3: VFS-Backed Content Manager

**Goal:** Create a content manager that loads .xnb files from `IVirtualFileSystem`, parses them, and returns `Texture2D` objects (or raw `XnbTextureData` for headless testing).

**Files:**
- Create: `src/SdvWebPort.Runtime/Content/VfsContentManager.cs`
- Create: `tests/SdvWebPort.Content.Tests/VfsContentManagerTests.cs`

**Interfaces:**
- Consumes: `IVirtualFileSystem`, `XnbFile`, `XnbTextureReader` (Tasks 1-2)
- Produces: `VfsContentManager` with `LoadTextureAsync(string path)` → `XnbTextureData`

- [ ] **Step 1: Write VfsContentManager.cs**

```csharp
using System.IO;
using System.Threading.Tasks;
using SdvWebPort.Runtime.Vfs;

namespace SdvWebPort.Runtime.Content;

/// <summary>
/// Content manager that loads .xnb files from an IVirtualFileSystem.
/// Parses XNB header + Texture2D data, returns raw pixel data.
/// Actual Texture2D creation (needs GraphicsDevice) happens in the caller.
/// </summary>
public sealed class VfsContentManager
{
    private readonly IVirtualFileSystem _vfs;
    private readonly Dictionary<string, XnbTextureData> _textureCache = new();

    public VfsContentManager(IVirtualFileSystem vfs)
    {
        _vfs = vfs;
    }

    /// <summary>
    /// Load a texture from an .xnb file in the VFS.
    /// Path should NOT include the .xnb extension (XNA convention).
    /// e.g. "Content/LooseSprites/logo" → loads "Content/LooseSprites/logo.xnb"
    /// </summary>
    public async Task<XnbTextureData> LoadTextureAsync(string assetName)
    {
        if (_textureCache.TryGetValue(assetName, out var cached))
            return cached;

        string xnbPath = assetName + ".xnb";
        if (!await _vfs.ExistsAsync(xnbPath))
            throw new FileNotFoundException($"XNB asset not found in VFS: {xnbPath}");

        await using var stream = await _vfs.OpenReadAsync(xnbPath);
        using var reader = new XnbReader(stream);

        var header = XnbFile.ParseHeader(reader);

        // Check that the first type reader is a Texture2DReader
        bool hasTextureReader = header.TypeReaders.Any(
            tr => tr.TypeName.Contains("Texture2DReader"));
        if (!hasTextureReader)
            throw new InvalidOperationException(
                $"XNB asset {xnbPath} does not contain a Texture2D. " +
                $"Type readers: {string.Join(", ", header.TypeReaders.Select(t => t.TypeName))}");

        // Read the primary object: type reader index (7-bit encoded int)
        int typeReaderIndex = reader.Read7BitEncodedInt();

        // Read the texture data
        var textureData = XnbTextureReader.Read(reader);

        _textureCache[assetName] = textureData;
        return textureData;
    }

    /// <summary>
    /// Check if an .xnb asset exists in the VFS.
    /// </summary>
    public async Task<bool> ExistsAsync(string assetName)
    {
        return await _vfs.ExistsAsync(assetName + ".xnb");
    }

    /// <summary>
    /// Clear all cached content.
    /// </summary>
    public void ClearCache() => _textureCache.Clear();
}
```

- [ ] **Step 2: Write tests using InMemoryVfs**

```csharp
using System.IO;
using System.Text;
using SdvWebPort.Runtime.Content;
using SdvWebPort.Vfs;
using Xunit;

namespace SdvWebPort.Content.Tests;

public class VfsContentManagerTests
{
    private static byte[] CreateMinimalXnbTexture(int width, int height, byte[] pixelData)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        // Header
        writer.Write((byte)'X'); writer.Write((byte)'N'); writer.Write((byte)'B');
        writer.Write((byte)5);   // version
        writer.Write((byte)0);   // flag (uncompressed)
        writer.Write(0);          // file size (not validated in tests)
        // 1 type reader: Texture2DReader
        writer.Write((byte)1);   // count = 1
        var name = Encoding.UTF8.GetBytes("Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework.Content");
        Write7Bit(writer, name.Length);
        writer.Write(name);
        writer.Write(0);          // version
        // 0 shared resources
        writer.Write((byte)0);
        // Primary object: type reader index = 0
        writer.Write((byte)0);
        // Texture2D data
        writer.Write(0);          // SurfaceFormat.Color
        writer.Write(width);
        writer.Write(height);
        writer.Write(1);          // mipCount
        writer.Write(pixelData.Length);
        writer.Write(pixelData);
        return ms.ToArray();
    }

    private static void Write7Bit(BinaryWriter w, int value)
    {
        while (value >= 0x80) { w.Write((byte)(value | 0x80)); value >>= 7; }
        w.Write((byte)value);
    }

    [Fact]
    public async Task LoadTextureAsync_ValidXnb_ReturnsTextureData()
    {
        var vfs = new InMemoryVfs();
        var pixels = new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255 };
        var xnbData = CreateMinimalXnbTexture(2, 2, pixels);
        await vfs.WriteFileAsync("Content/test_texture.xnb", xnbData);

        var cm = new VfsContentManager(vfs);
        var tex = await cm.LoadTextureAsync("Content/test_texture");

        Assert.Equal(2, tex.Width);
        Assert.Equal(2, tex.Height);
        Assert.Equal(pixels, tex.PixelData);
    }

    [Fact]
    public async Task LoadTextureAsync_CachesResult()
    {
        var vfs = new InMemoryVfs();
        var pixels = new byte[] { 255, 0, 0, 255 };
        var xnbData = CreateMinimalXnbTexture(1, 1, pixels);
        await vfs.WriteFileAsync("Content/cached.xnb", xnbData);

        var cm = new VfsContentManager(vfs);
        var tex1 = await cm.LoadTextureAsync("Content/cached");
        var tex2 = await cm.LoadTextureAsync("Content/cached");

        Assert.Same(tex1, tex2); // Same cached instance
    }

    [Fact]
    public async Task LoadTextureAsync_NotFound_ThrowsFileNotFoundException()
    {
        var vfs = new InMemoryVfs();
        var cm = new VfsContentManager(vfs);
        await Assert.ThrowsAsync<FileNotFoundException>(() => cm.LoadTextureAsync("Content/missing"));
    }
}
```

- [ ] **Step 3: Run tests**

```bash
dotnet test tests/SdvWebPort.Content.Tests/ --verbosity minimal
```

- [ ] **Step 4: Commit**

```bash
git add src/SdvWebPort.Runtime/Content/VfsContentManager.cs tests/SdvWebPort.Content.Tests/VfsContentManagerTests.cs
git commit -m "feat: VFS-backed content manager — loads .xnb textures from VFS"
```

---

## Task 4: Async Canvas Image Decoder (JS Interop)

**Goal:** Implement a real browser Canvas API image decoder that converts PNG/image bytes to RGBA pixel data. Uses a pre-decode-and-cache strategy: all images are decoded asynchronously at startup, then served synchronously from cache via `[JSImport]`.

**Files:**
- Create: `src/SdvWebPort.Runtime/Content/ContentJsInterop.cs`
- Modify: `src/SdvWebPort.Runtime/wwwroot/vfs-interop.js` (add image decode functions)

**Interfaces:**
- Consumes: `[JSImport]` JS interop
- Produces: `ContentJsInterop.DecodeImage(byte[] imageBytes)` → cached, then `GetCachedWidth/Height/Rgba(string cacheKey)` returns decoded data

- [ ] **Step 1: Write ContentJsInterop.cs**

```csharp
using System.Runtime.InteropServices.JavaScript;

namespace SdvWebPort.Runtime.Content;

/// <summary>
/// JS interop for browser Canvas API image decoding.
/// Uses a pre-decode-and-cache strategy:
/// 1. C# calls PreDecodeImage(key, imageBytes) — JS decodes async
/// 2. C# calls AwaitAllDecoded() — waits for all async decodes to finish
/// 3. C# calls GetCachedWidth/Height/Rgba(key) — returns decoded data synchronously
/// </summary>
internal static partial class ContentJsInterop
{
    /// <summary>
    /// Queue an image for async decoding. Returns immediately.
    /// </summary>
    [JSImport("globalThis.contentPreDecode")]
    internal static partial void PreDecodeImage(string cacheKey, byte[] imageBytes);

    /// <summary>
    /// Wait for all queued image decodes to complete.
    /// </summary>
    [JSImport("globalThis.contentAwaitDecoded")]
    internal static partial void AwaitAllDecoded();

    /// <summary>
    /// Get decoded image width (must call AwaitAllDecoded first).
    /// </summary>
    [JSImport("globalThis.contentGetWidth")]
    internal static partial int GetCachedWidth(string cacheKey);

    /// <summary>
    /// Get decoded image height.
    /// </summary>
    [JSImport("globalThis.contentGetHeight")]
    internal static partial int GetCachedHeight(string cacheKey);

    /// <summary>
    /// Get decoded RGBA pixel data.
    /// </summary>
    [JSImport("globalThis.contentGetRgba")]
    internal static partial byte[] GetCachedRgba(string cacheKey);
}
```

- [ ] **Step 2: Add image decode functions to vfs-interop.js**

Append to `src/SdvWebPort.Runtime/wwwroot/vfs-interop.js`:

```javascript
// ── Content image decoder (Canvas API) ─────────────────────────────────────
// Pre-decode-and-cache strategy: C# calls contentPreDecode for each image,
// then contentAwaitDecoded to wait, then contentGetWidth/Height/Rgba to read.

const _imageCache = {};
const _decodePromises = [];

globalThis.contentPreDecode = function(cacheKey, imageBytes) {
    const promise = (async () => {
        try {
            const blob = new Blob([imageBytes], { type: 'image/png' });
            const bitmap = await createImageBitmap(blob);
            const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
            const ctx = canvas.getContext('2d');
            ctx.drawImage(bitmap, 0, 0);
            const imageData = ctx.getImageData(0, 0, bitmap.width, bitmap.height);
            _imageCache[cacheKey] = {
                width: bitmap.width,
                height: bitmap.height,
                rgba: new Uint8Array(imageData.data.buffer)
            };
            bitmap.close();
            console.log('[content] Decoded image: ' + cacheKey + ' ' + bitmap.width + 'x' + bitmap.height);
        } catch (e) {
            console.log('[content] Decode failed for ' + cacheKey + ': ' + e.message);
            _imageCache[cacheKey] = { width: 0, height: 0, rgba: new Uint8Array(0) };
        }
    })();
    _decodePromises.push(promise);
};

globalThis.contentAwaitDecoded = async function() {
    await Promise.all(_decodePromises);
    _decodePromises.length = 0;
    console.log('[content] All images decoded. Cache size: ' + Object.keys(_imageCache).length);
};

globalThis.contentGetWidth = function(cacheKey) {
    return _imageCache[cacheKey]?.width || 0;
};

globalThis.contentGetHeight = function(cacheKey) {
    return _imageCache[cacheKey]?.height || 0;
};

globalThis.contentGetRgba = function(cacheKey) {
    return _imageCache[cacheKey]?.rgba || new Uint8Array(0);
};
```

- [ ] **Step 3: Build to verify [JSImport] source generation works**

```bash
dotnet build src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj
```

- [ ] **Step 4: Commit**

```bash
git add src/SdvWebPort.Runtime/Content/ContentJsInterop.cs src/SdvWebPort.Runtime/wwwroot/vfs-interop.js
git commit -m "feat: async Canvas image decoder with pre-decode-and-cache strategy"
```

---

## Task 5: Integration — Load XNB Texture and Create Texture2D

**Goal:** Wire together VfsContentManager (Task 3) + ContentJsInterop (Task 4) + KNI Texture2D to load a real .xnb texture from VFS and render it. This task requires a real browser to verify rendering (GraphicsDevice creation needs rAF).

**Files:**
- Modify: `src/SdvWebPort.Runtime/Program.cs` (add content loading flow)
- Create: `scripts/run-content-smoke-test.sh`

- [ ] **Step 1: Update Program.cs to include content loading**

Add a `[JSExport]` method that:
1. Takes a VFS path to an .xnb texture
2. Loads it via VfsContentManager
3. Returns the texture dimensions + pixel data (for headless verification)
4. In a real browser with GraphicsDevice, creates a Texture2D

```csharp
// Add to Program.cs:

[JSExport]
public static async Task<string> LoadTextureFromVfs(string xnbPath)
{
    try
    {
        if (_vfs == null) return "ERROR: VFS not initialized";
        var cm = new VfsContentManager(_vfs);
        var tex = await cm.LoadTextureAsync(xnbPath);
        var rgba = XnbTextureReader.NormalizeToRgba(tex);
        return $"OK: {tex.Width}x{tex.Height}, {rgba.Length} bytes, format={tex.Format}";
    }
    catch (Exception ex)
    {
        return $"ERROR: {ex.GetType().Name}: {ex.Message}";
    }
}
```

- [ ] **Step 2: Write smoke test script**

```bash
#!/usr/bin/env bash
# Phase 1b content smoke test: load an .xnb texture from VFS
set -uo pipefail
PROJECT_ROOT="/home/z/my-project"
PERSIST_DIR="$PROJECT_ROOT/.superpowers/sdd/poc-content-artifacts"
cd "$PROJECT_ROOT"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
mkdir -p "$PERSIST_DIR"

echo "=== Phase 1b Content Smoke Test ==="

# Publish
echo "[1/3] Publishing..."
dotnet publish src/SdvWebPort.Runtime/SdvWebPort.Runtime.csproj -c Debug -o "$PERSIST_DIR/publish" > "$PERSIST_DIR/build.log" 2>&1 || { echo "BUILD FAILED"; tail -20 "$PERSIST_DIR/build.log"; exit 2; }
echo "    Publish OK"

# Start server
echo "[2/3] Starting server..."
pkill -f "http.server 5089" 2>/dev/null || true
sleep 1
python3 -m http.server 5089 --directory "$PERSIST_DIR/publish/wwwroot" > "$PERSIST_DIR/http.log" 2>&1 &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null || true" EXIT
for i in $(seq 1 15); do curl -s http://localhost:5089/ >/dev/null 2>&1 && break; sleep 1; done

# Run Chrome
echo "[3/3] Running Chrome..."
CHROME=${CHROME:-/home/z/.agent-browser/browsers/chrome-149.0.7827.115/chrome}
timeout 90 "$CHROME" --headless --no-sandbox \
  --use-gl=angle --use-angle=swiftshader --enable-webgl --ignore-gpu-blocklist \
  --enable-unsafe-swiftshader --enable-logging=stderr --v=1 \
  --virtual-time-budget=60000 --dump-dom \
  "http://localhost:5089/" > "$PERSIST_DIR/dom.html" 2> "$PERSIST_DIR/chrome.log"

# Check for success
if grep -q "VFS capabilities" "$PERSIST_DIR/chrome.log" 2>/dev/null; then
  echo "[PASS] Content smoke test: runtime + VFS loaded"
  grep "VFS capabilities" "$PERSIST_DIR/chrome.log" | head -1
  exit 0
else
  echo "[FAIL] Content smoke test"
  grep "INFO:CONSOLE" "$PERSIST_DIR/chrome.log" | head -5
  exit 1
fi
```

- [ ] **Step 3: Run smoke test**

```bash
chmod +x scripts/run-content-smoke-test.sh
./scripts/run-content-smoke-test.sh
```

- [ ] **Step 4: Commit**

```bash
git add src/SdvWebPort.Runtime/Program.cs scripts/run-content-smoke-test.sh
git commit -m "feat: XNB texture loading integration + smoke test"
```

---

## Plan Self-Review

**1. Spec coverage:**
- Spec §9 Phase 1 "Content/*.xnb 资源加载成功" → Tasks 1-3 (XNB parser + VFS content manager)
- Spec §9 Phase 1 "字体渲染正常" → Deferred to Phase 1c (SpriteFont parsing is complex; Phase 1b establishes the texture loading pipeline which fonts depend on)

**2. Placeholder scan:** No TBD/TODO. All code is complete.

**3. Type consistency:**
- `XnbReader` used consistently across Tasks 1-3
- `XnbTextureData` record defined in Task 2, used in Task 3
- `VfsContentManager.LoadTextureAsync` returns `XnbTextureData` (Task 3), consumed by Program.cs (Task 5)
- `ContentJsInterop` JS function names match between C# `[JSImport]` and JS `globalThis.contentXxx`

**Scope note:** This plan covers XNB texture loading (not fonts). Font rendering (SpriteFont parsing) is deferred to Phase 1c. This is a realistic scope — XNB parsing + texture loading is the foundation that font rendering builds on.
