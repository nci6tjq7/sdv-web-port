using System;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

namespace SdvWebPort.Runtime.Vfs;

/// <summary>
/// Factory that detects browser capabilities and returns the best VFS implementation.
/// Tries File System Access API (A2) first; falls back to OPFS (A1).
/// </summary>
public static class VfsFactory
{
    public static IVirtualFileSystem Create()
    {
        var capsJson = VfsJsInterop.VfsGetCapabilities();
        Console.WriteLine($"[VfsFactory] Browser capabilities: {capsJson}");

        var caps = JsonDocument.Parse(capsJson);
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
}

internal static partial class VfsJsInterop
{
    [JSImport("globalThis.vfsGetCapabilities")]
    internal static partial string VfsGetCapabilities();
}
