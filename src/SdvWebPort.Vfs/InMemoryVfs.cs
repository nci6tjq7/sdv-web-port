using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SdvWebPort.Vfs;

/// <summary>
/// In-memory VFS for testing. Not for production use.
/// </summary>
public sealed class InMemoryVfs : IVirtualFileSystem
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();

    public Task<bool> ExistsAsync(string path)
        => Task.FromResult(_files.ContainsKey(Normalize(path)));

    public Task<Stream> OpenReadAsync(string path)
    {
        var bytes = _files.TryGetValue(Normalize(path), out var b)
            ? b
            : throw new FileNotFoundException($"VFS file not found: {path}");
        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task<long> GetFileSizeAsync(string path)
    {
        if (_files.TryGetValue(Normalize(path), out var b))
            return Task.FromResult((long)b.Length);
        throw new FileNotFoundException($"VFS file not found: {path}");
    }

    public async IAsyncEnumerable<string> EnumerateFilesAsync(string directory, string pattern)
    {
        var prefix = Normalize(directory).TrimEnd('/') + "/";
        foreach (var kvp in _files)
        {
            if (!kvp.Key.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
            var filename = kvp.Key[prefix.Length..];
            if (!filename.Contains('/'))  // direct child only
            {
                if (MatchesPattern(filename, pattern))
                    yield return kvp.Key;
            }
        }
        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> EnumerateDirectoriesAsync(string directory)
    {
        var prefix = Normalize(directory).TrimEnd('/') + "/";
        var seen = new HashSet<string>();
        foreach (var kvp in _files)
        {
            if (!kvp.Key.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
            var rest = kvp.Key[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash > 0)
            {
                var dir = prefix + rest[..slash];
                if (seen.Add(dir)) yield return dir;
            }
        }
        await Task.CompletedTask;
    }

    public Task WriteFileAsync(string path, byte[] contents)
    {
        _files[Normalize(path)] = contents;
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path)
    {
        _files.TryRemove(Normalize(path), out _);
        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string path) => Task.CompletedTask; // No-op for in-memory

    // Sync API: bridge to async via .GetAwaiter().GetResult()
    // Note: this works in test/host environments; WASM needs SyncWorkerHandler (Phase 1+)
    public Stream OpenRead(string path) => OpenReadAsync(path).GetAwaiter().GetResult();
    public bool Exists(string path) => ExistsAsync(path).GetAwaiter().GetResult();
    public long GetFileSize(string path) => GetFileSizeAsync(path).GetAwaiter().GetResult();

    public IEnumerable<string> EnumerateFiles(string directory, string pattern)
        => EnumerateFilesAsync(directory, pattern).ToListAsync().GetAwaiter().GetResult();

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimEnd('/');

    private static bool MatchesPattern(string filename, string pattern)
    {
        if (pattern == "*.*" || pattern == "*") return true;
        // Convert simple glob to regex: *.txt -> ^.*\.txt$
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(filename, regex);
    }
}
