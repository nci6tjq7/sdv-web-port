using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

namespace SdvWebPort.Runtime.Vfs;

/// <summary>
/// VFS implementation using OPFS (Origin Private File System) — A1 fallback path.
/// </summary>
public sealed class OpfsVfs : IVirtualFileSystem
{
    public Task<bool> ExistsAsync(string path)
        => Task.FromResult(OpfsJsInterop.VfsOpfsExists(Normalize(path)));

    public Task<Stream> OpenReadAsync(string path)
    {
        var bytes = OpfsJsInterop.VfsOpfsReadFile(Normalize(path));
        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task<long> GetFileSizeAsync(string path)
        => Task.FromResult((long)OpfsJsInterop.VfsOpfsGetFileSize(Normalize(path)));

    public async IAsyncEnumerable<string> EnumerateFilesAsync(
        string directory, string pattern,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var files = OpfsJsInterop.VfsOpfsEnumerateFiles(Normalize(directory), pattern);
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
        OpfsJsInterop.VfsOpfsWriteFile(Normalize(path), contents);
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path)
    {
        OpfsJsInterop.VfsOpfsDeleteFile(Normalize(path));
        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string path) => Task.CompletedTask;

    public Stream OpenRead(string path) => OpenReadAsync(path).GetAwaiter().GetResult();
    public bool Exists(string path) => ExistsAsync(path).GetAwaiter().GetResult();
    public long GetFileSize(string path) => GetFileSizeAsync(path).GetAwaiter().GetResult();
    public IEnumerable<string> EnumerateFiles(string directory, string pattern)
        => OpfsJsInterop.VfsOpfsEnumerateFiles(Normalize(directory), pattern);

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}

internal static partial class OpfsJsInterop
{
    [JSImport("globalThis.vfsOpfsExists")]
    internal static partial bool VfsOpfsExists(string path);

    [JSImport("globalThis.vfsOpfsReadFile")]
    internal static partial byte[] VfsOpfsReadFile(string path);

    [JSImport("globalThis.vfsOpfsGetFileSize")]
    internal static partial int VfsOpfsGetFileSize(string path);

    [JSImport("globalThis.vfsOpfsEnumerateFiles")]
    internal static partial string[] VfsOpfsEnumerateFiles(string dirPath, string pattern);

    [JSImport("globalThis.vfsOpfsWriteFile")]
    internal static partial bool VfsOpfsWriteFile(string path, byte[] contents);

    [JSImport("globalThis.vfsOpfsDeleteFile")]
    internal static partial bool VfsOpfsDeleteFile(string path);
}
