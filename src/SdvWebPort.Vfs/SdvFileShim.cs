using System;
using System.IO;
using System.Linq;

namespace SdvWebPort.Vfs;

/// <summary>
/// Static shim that provides System.IO.File-equivalent signatures,
/// routing calls to the active IVirtualFileSystem. Used by the Cecil
/// rewriter (Phase 2.75) to redirect SDV's File.OpenRead/etc. calls.
///
/// The rewriter replaces IL calls like:
///   call System.IO.File::OpenRead(string)
/// with:
///   call SdvWebPort.Vfs.SdvFileShim::OpenRead(string)
///
/// The shim must be initialized with an IVirtualFileSystem BEFORE any
/// redirected code runs. SdvBlazor sets this up in Home.razor.cs
/// after the user uploads their GOG files.
/// </summary>
public static class SdvFileShim
{
    private static IVirtualFileSystem? _vfs;
    private static readonly object _lock = new();

    /// <summary>
    /// Set the VFS that SdvFileShim will route calls to. Must be called
    /// BEFORE any redirected SDV code executes.
    /// </summary>
    public static void SetVfs(IVirtualFileSystem vfs)
    {
        lock (_lock)
        {
            _vfs = vfs;
            Console.WriteLine($"[SdvFileShim] VFS set: {vfs.GetType().Name}");
        }
    }

    private static IVirtualFileSystem RequireVfs()
    {
        var vfs = _vfs;
        if (vfs == null)
            throw new InvalidOperationException("SdvFileShim.SetVfs() must be called before any redirected file operation.");
        return vfs;
    }

    // --- System.IO.File equivalents ---

    /// <summary>Equivalent to System.IO.File.OpenRead(string path)</summary>
    public static Stream OpenRead(string path)
    {
        Console.WriteLine($"[SdvFileShim] OpenRead: {path}");
        return RequireVfs().OpenRead(path);
    }

    /// <summary>Equivalent to System.IO.File.Exists(string path)</summary>
    public static bool Exists(string path)
    {
        var result = RequireVfs().Exists(path);
        Console.WriteLine($"[SdvFileShim] Exists({path}) = {result}");
        return result;
    }

    /// <summary>Equivalent to System.IO.File.ReadAllBytes(string path)</summary>
    public static byte[] ReadAllBytes(string path)
    {
        Console.WriteLine($"[SdvFileShim] ReadAllBytes: {path}");
        using var stream = RequireVfs().OpenRead(path);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Equivalent to System.IO.File.ReadAllText(string path)</summary>
    public static string ReadAllText(string path)
    {
        Console.WriteLine($"[SdvFileShim] ReadAllText: {path}");
        using var stream = RequireVfs().OpenRead(path);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // --- System.IO.Directory equivalents ---

    /// <summary>Equivalent to System.IO.Directory.GetFiles(string path, string searchPattern)</summary>
    public static string[] GetFiles(string path, string searchPattern)
    {
        Console.WriteLine($"[SdvFileShim] GetFiles({path}, {searchPattern})");
        return RequireVfs().EnumerateFiles(path, searchPattern).ToArray();
    }

    /// <summary>Equivalent to System.IO.Directory.GetFiles(string path)</summary>
    public static string[] GetFiles(string path)
    {
        return GetFiles(path, "*");
    }

    /// <summary>Equivalent to System.IO.Directory.Exists(string path)</summary>
    public static bool DirectoryExists(string path)
    {
        // VFS doesn't have a direct DirectoryExists, but Exists on a file
        // returning false doesn't mean the directory doesn't exist.
        // For now, return true if EnumerateFiles returns anything.
        try
        {
            var files = RequireVfs().EnumerateFiles(path, "*").ToArray();
            return true; // if no exception, directory exists
        }
        catch
        {
            return false;
        }
    }
}
