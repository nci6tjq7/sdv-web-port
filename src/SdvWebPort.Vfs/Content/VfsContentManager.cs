using System.IO;
using System.Threading.Tasks;

namespace SdvWebPort.Vfs.Content;

public sealed class VfsContentManager
{
    private readonly IVirtualFileSystem _vfs;
    private readonly Dictionary<string, XnbTextureData> _cache = new();

    public VfsContentManager(IVirtualFileSystem vfs) => _vfs = vfs;

    public async Task<XnbTextureData> LoadTextureAsync(string assetName)
    {
        if (_cache.TryGetValue(assetName, out var cached)) return cached;

        string xnbPath = assetName + ".xnb";
        if (!await _vfs.ExistsAsync(xnbPath))
            throw new FileNotFoundException($"XNB not found: {xnbPath}");

        await using var stream = await _vfs.OpenReadAsync(xnbPath);
        using var reader = new XnbReader(stream);
        var header = XnbFile.ParseHeader(reader);

        bool hasTextureReader = header.TypeReaders.Any(
            tr => tr.TypeName.Contains("Texture2DReader"));
        if (!hasTextureReader)
            throw new InvalidOperationException(
                $"XNB {xnbPath} has no Texture2D. Readers: {string.Join(", ", header.TypeReaders.Select(t => t.TypeName))}");

        reader.Read7BitEncodedInt(); // type reader index
        var tex = XnbTextureReader.Read(reader);
        _cache[assetName] = tex;
        return tex;
    }

    public async Task<bool> ExistsAsync(string assetName) =>
        await _vfs.ExistsAsync(assetName + ".xnb");

    public void ClearCache() => _cache.Clear();
}
