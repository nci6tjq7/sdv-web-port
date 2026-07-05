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
