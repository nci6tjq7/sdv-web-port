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

    /// <summary>
    /// Equivalent to Microsoft.Xna.Framework.TitleContainer.OpenStream(string name).
    /// KNI's ContentManager.OpenStream calls this. We redirect to VFS.
    /// The 'name' parameter is relative to Content root (e.g., "Data\BigCraftables").
    /// We prepend "Content/" to form the full VFS path.
    /// </summary>
    public static Stream TitleContainerOpenStream(string name)
    {
        // Normalize: Content/Data/BigCraftables.xnb
        var path = "Content/" + name.Replace('\\', '/');
        if (!path.EndsWith(".xnb"))
            path += ".xnb";
        Console.WriteLine($"[SdvFileShim] TitleContainerOpenStream: {name} → {path}");
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
    /// <remarks>
    /// NOTE: This is a heuristic. We check if the directory has any files OR
    /// if EnumerateFiles throws. This is correct for throwing VFS implementations
    /// (e.g., a future real VFS that throws DirectoryNotFoundException for
    /// nonexistent dirs). For InMemoryVfs (which never throws), this returns
    /// true if the directory has files, false otherwise — which is WRONG for
    /// empty-but-existing directories.
    /// TODO (Phase 2.8): Add DirectoryExists to IVirtualFileSystem for correctness.
    /// Real SDV gates on Directory.Exists (e.g., save directory checks).
    /// </remarks>
    public static bool DirectoryExists(string path)
    {
        try
        {
            var files = RequireVfs().EnumerateFiles(path, "*").ToArray();
            return files.Length > 0; // directory exists if it has files
        }
        catch
        {
            return false; // directory doesn't exist (VFS threw)
        }
    }
}
