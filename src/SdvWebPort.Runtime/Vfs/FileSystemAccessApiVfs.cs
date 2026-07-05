using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

namespace SdvWebPort.Runtime.Vfs;

/// <summary>
/// VFS implementation using the File System Access API (A2 path — zero-copy).
/// Requires the user to have called vfsPickDirectory() first (via UI button).
/// </summary>
public sealed class FileSystemAccessApiVfs : IVirtualFileSystem
{
    public Task<bool> ExistsAsync(string path)
        => Task.FromResult(FsaJsInterop.VfsFsaExists(Normalize(path)));

    public Task<Stream> OpenReadAsync(string path)
    {
        var bytes = FsaJsInterop.VfsFsaReadFile(Normalize(path));
        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task<long> GetFileSizeAsync(string path)
        => Task.FromResult((long)FsaJsInterop.VfsFsaGetFileSize(Normalize(path)));

    public async IAsyncEnumerable<string> EnumerateFilesAsync(
        string directory, string pattern,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var files = FsaJsInterop.VfsFsaEnumerateFiles(Normalize(directory), pattern);
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
        => throw new NotSupportedException("FileSystemAccessApiVfs is read-only");

    public Task DeleteFileAsync(string path)
        => throw new NotSupportedException("FileSystemAccessApiVfs is read-only");

    public Task CreateDirectoryAsync(string path)
        => throw new NotSupportedException("FileSystemAccessApiVfs is read-only");

    public Stream OpenRead(string path) => OpenReadAsync(path).GetAwaiter().GetResult();
    public bool Exists(string path) => ExistsAsync(path).GetAwaiter().GetResult();
    public long GetFileSize(string path) => GetFileSizeAsync(path).GetAwaiter().GetResult();
    public IEnumerable<string> EnumerateFiles(string directory, string pattern)
        => FsaJsInterop.VfsFsaEnumerateFiles(Normalize(directory), pattern);

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}

internal static partial class FsaJsInterop
{
    [JSImport("globalThis.vfsFsaExists")]
    internal static partial bool VfsFsaExists(string path);

    [JSImport("globalThis.vfsFsaReadFile")]
    internal static partial byte[] VfsFsaReadFile(string path);

    [JSImport("globalThis.vfsFsaGetFileSize")]
    internal static partial int VfsFsaGetFileSize(string path);

    [JSImport("globalThis.vfsFsaEnumerateFiles")]
    internal static partial string[] VfsFsaEnumerateFiles(string dirPath, string pattern);
}
