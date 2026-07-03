using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SdvWebPort.Runtime.Vfs;

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
    IAsyncEnumerable<string> EnumerateFilesAsync(string directory, string pattern, CancellationToken ct = default);
    IAsyncEnumerable<string> EnumerateDirectoriesAsync(string directory, CancellationToken ct = default);
    Task WriteFileAsync(string path, byte[] contents);
    Task DeleteFileAsync(string path);
    Task CreateDirectoryAsync(string path);

    // Sync API (for SDV game code that uses synchronous File.OpenRead).
    Stream OpenRead(string path);
    bool Exists(string path);
    long GetFileSize(string path);
    IEnumerable<string> EnumerateFiles(string directory, string pattern);
}
