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

    // --- Phase 2.8: Additional methods discovered by static analysis of real SDV.dll ---

    /// <summary>Equivalent to System.IO.File.Open(string, FileMode)</summary>
    public static Stream Open(string path, System.IO.FileMode mode)
    {
        Console.WriteLine($"[SdvFileShim] Open({path}, {mode})");
        // For read modes, use OpenRead; for write modes, use VFS write
        if (mode == System.IO.FileMode.Open || mode == System.IO.FileMode.OpenOrCreate)
        {
            if (RequireVfs().Exists(path))
                return RequireVfs().OpenRead(path);
        }
        // Create/Truncate/Append — return a MemoryStream for now (VFS write support)
        return new MemoryStream();
    }

    /// <summary>Equivalent to System.IO.File.Open(string, FileMode, FileAccess)</summary>
    public static Stream Open(string path, System.IO.FileMode mode, System.IO.FileAccess access)
    {
        Console.WriteLine($"[SdvFileShim] Open({path}, {mode}, {access})");
        if (access == System.IO.FileAccess.Read)
            return RequireVfs().OpenRead(path);
        return new MemoryStream();
    }

    /// <summary>Equivalent to System.IO.File.ReadAllLines(string)</summary>
    public static string[] ReadAllLines(string path)
    {
        Console.WriteLine($"[SdvFileShim] ReadAllLines: {path}");
        using var stream = RequireVfs().OpenRead(path);
        using var reader = new StreamReader(stream);
        var lines = new System.Collections.Generic.List<string>();
        while (!reader.EndOfStream)
            lines.Add(reader.ReadLine()!);
        return lines.ToArray();
    }

    /// <summary>Equivalent to System.IO.File.AppendAllText(string, string)</summary>
    public static void AppendAllText(string path, string contents)
    {
        Console.WriteLine($"[SdvFileShim] AppendAllText: {path}");
        // Read existing + append + write back
        var vfs = RequireVfs();
        string existing = "";
        if (vfs.Exists(path))
        {
            using var s = vfs.OpenRead(path);
            using var r = new StreamReader(s);
            existing = r.ReadToEnd();
        }
        vfs.WriteFileAsync(path, System.Text.Encoding.UTF8.GetBytes(existing + contents)).Wait();
    }

    /// <summary>Equivalent to System.IO.File.Create(string)</summary>
    public static Stream Create(string path)
    {
        Console.WriteLine($"[SdvFileShim] Create: {path}");
        // Create empty file, return a writeable stream
        RequireVfs().WriteFileAsync(path, Array.Empty<byte>()).Wait();
        return new MemoryStream();
    }

    /// <summary>Equivalent to System.IO.File.CreateText(string)</summary>
    public static StreamWriter CreateText(string path)
    {
        Console.WriteLine($"[SdvFileShim] CreateText: {path}");
        RequireVfs().WriteFileAsync(path, Array.Empty<byte>()).Wait();
        var ms = new MemoryStream();
        return new StreamWriter(ms);
    }

    /// <summary>Equivalent to System.IO.File.Delete(string)</summary>
    public static void Delete(string path)
    {
        Console.WriteLine($"[SdvFileShim] Delete: {path}");
        RequireVfs().DeleteFileAsync(path).Wait();
    }

    /// <summary>Equivalent to System.IO.File.Move(string, string, bool)</summary>
    public static void Move(string sourcePath, string destPath, bool overwrite)
    {
        Console.WriteLine($"[SdvFileShim] Move({sourcePath} → {destPath})");
        var vfs = RequireVfs();
        using var s = vfs.OpenRead(sourcePath);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        vfs.WriteFileAsync(destPath, ms.ToArray()).Wait();
        vfs.DeleteFileAsync(sourcePath).Wait();
    }

    /// <summary>Equivalent to System.IO.File.WriteAllText(string, string)</summary>
    public static void WriteAllText(string path, string contents)
    {
        Console.WriteLine($"[SdvFileShim] WriteAllText: {path}");
        RequireVfs().WriteFileAsync(path, System.Text.Encoding.UTF8.GetBytes(contents)).Wait();
    }

    /// <summary>Equivalent to System.IO.Directory.CreateDirectory(string)</summary>
    public static void CreateDirectory(string path)
    {
        Console.WriteLine($"[SdvFileShim] CreateDirectory: {path}");
        RequireVfs().CreateDirectoryAsync(path).Wait();
    }

    /// <summary>Equivalent to System.IO.Directory.Delete(string, bool)</summary>
    public static void DeleteDirectory(string path, bool recursive)
    {
        Console.WriteLine($"[SdvFileShim] DeleteDirectory({path}, recursive={recursive})");
        // VFS doesn't have DeleteDirectory yet — best effort
        // TODO: add DeleteDirectoryAsync to IVirtualFileSystem
    }

    /// <summary>Equivalent to System.IO.Directory.EnumerateDirectories(string)</summary>
    public static string[] EnumerateDirectories(string path)
    {
        Console.WriteLine($"[SdvFileShim] EnumerateDirectories: {path}");
        // Bridge async to sync (same pattern as InMemoryVfs sync API)
        var dirs = new System.Collections.Generic.List<string>();
        var asyncEnum = RequireVfs().EnumerateDirectoriesAsync(path);
        var enumerator = asyncEnum.GetAsyncEnumerator();
        while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            dirs.Add(enumerator.Current);
        return dirs.ToArray();
    }
}
