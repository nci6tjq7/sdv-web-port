using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SdvWebPort.Vfs;

/// <summary>
/// HTTP-backed VFS that fetches files from a web server.
/// Used for loading SDV's Content directory (544MB of XNB files) from /deps/content/.
/// </summary>
public class HttpVfs : IVirtualFileSystem
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public HttpVfs(HttpClient http, string baseUrl)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/') + "/deps/content/";
    }

    // --- Sync API (used by SdvFileShim) ---
    public Stream OpenRead(string path)
    {
        var normalized = NormalizePath(path);
        var url = _baseUrl + normalized;
        Console.WriteLine($"[HttpVfs] OpenRead: {path} → {url}");
        try
        {
            var bytes = _http.GetByteArrayAsync(url).GetAwaiter().GetResult();
            return new MemoryStream(bytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HttpVfs] FAILED: {ex.Message}");
            throw new FileNotFoundException($"VFS file not found: {path}", ex);
        }
    }

    public bool Exists(string path) => true;

    public long GetFileSize(string path)
    {
        using var stream = OpenRead(path);
        return stream.Length;
    }

    public IEnumerable<string> EnumerateFiles(string directory, string pattern) => Array.Empty<string>();

    // --- Async API (not used by SDV game code, but required by interface) ---
    public Task<Stream> OpenReadAsync(string path) => Task.FromResult(OpenRead(path));
    public Task<bool> ExistsAsync(string path) => Task.FromResult(Exists(path));
    public Task<long> GetFileSizeAsync(string path) => Task.FromResult(GetFileSize(path));
    public IAsyncEnumerable<string> EnumerateFilesAsync(string directory, string pattern)
        => EnumerateFiles(directory, pattern).ToAsyncEnumerable();
    public IAsyncEnumerable<string> EnumerateDirectoriesAsync(string directory)
        => Array.Empty<string>().ToAsyncEnumerable();
    public Task WriteFileAsync(string path, byte[] contents) => throw new NotSupportedException();
    public Task DeleteFileAsync(string path) => throw new NotSupportedException();
    public Task CreateDirectoryAsync(string path) => throw new NotSupportedException();

    private static string NormalizePath(string path)
    {
        // Replace backslashes with forward slashes
        var normalized = path.Replace('\\', '/');
        // Remove leading "Content/" if present
        if (normalized.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("Content/".Length);
        // Remove leading slash
        normalized = normalized.TrimStart('/');
        return normalized;
    }
}
