using System.IO;
using System.Text;
using System.Threading.Tasks;
using SdvWebPort.Vfs;
using Xunit;

namespace SdvWebPort.Vfs.Tests;

/// <summary>
/// Abstract contract test suite. Any IVirtualFileSystem implementation must pass
/// all these tests. Inherit and provide an instance via CreateVfs().
/// </summary>
public abstract class VfsContractTests
{
    protected abstract IVirtualFileSystem CreateVfs();

    [Fact]
    public virtual async Task WriteFile_ThenExistsAsync_ReturnsTrue()
    {
        var vfs = CreateVfs();
        await vfs.WriteFileAsync("/foo/bar.txt", Encoding.UTF8.GetBytes("hello"));
        Assert.True(await vfs.ExistsAsync("/foo/bar.txt"));
    }

    [Fact]
    public virtual async Task WriteFile_ThenOpenReadAsync_ReturnsSameBytes()
    {
        var vfs = CreateVfs();
        var expected = Encoding.UTF8.GetBytes("test content");
        await vfs.WriteFileAsync("/x/y.bin", expected);
        await using var stream = await vfs.OpenReadAsync("/x/y.bin");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.Equal(expected, ms.ToArray());
    }

    [Fact]
    public virtual async Task ExistsAsync_NonExistentFile_ReturnsFalse()
    {
        var vfs = CreateVfs();
        Assert.False(await vfs.ExistsAsync("/nope.txt"));
    }

    [Fact]
    public virtual async Task OpenReadAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        var vfs = CreateVfs();
        await Assert.ThrowsAsync<FileNotFoundException>(() => vfs.OpenReadAsync("/missing.txt"));
    }

    [Fact]
    public virtual async Task GetFileSizeAsync_ReturnsCorrectSize()
    {
        var vfs = CreateVfs();
        var data = new byte[1024];
        await vfs.WriteFileAsync("/big.bin", data);
        Assert.Equal(1024, await vfs.GetFileSizeAsync("/big.bin"));
    }

    [Fact]
    public virtual async Task EnumerateFilesAsync_ReturnsMatchingPaths()
    {
        var vfs = CreateVfs();
        await vfs.WriteFileAsync("/a/1.txt", new byte[]{1});
        await vfs.WriteFileAsync("/a/2.txt", new byte[]{2});
        await vfs.WriteFileAsync("/a/3.log", new byte[]{3});
        var txtFiles = await vfs.EnumerateFilesAsync("/a", "*.txt").ToListAsync();
        Assert.Equal(2, txtFiles.Count);
    }

    [Fact]
    public virtual async Task DeleteFileAsync_RemovesFile()
    {
        var vfs = CreateVfs();
        await vfs.WriteFileAsync("/del.txt", new byte[]{1});
        Assert.True(await vfs.ExistsAsync("/del.txt"));
        await vfs.DeleteFileAsync("/del.txt");
        Assert.False(await vfs.ExistsAsync("/del.txt"));
    }

    [Fact]
    public virtual async Task SyncApi_OpenRead_ReturnsSameBytesAsAsync()
    {
        var vfs = CreateVfs();
        var expected = Encoding.UTF8.GetBytes("sync test");
        await vfs.WriteFileAsync("/sync.txt", expected);
        using var stream = vfs.OpenRead("/sync.txt");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        Assert.Equal(expected, ms.ToArray());
    }

    [Fact]
    public virtual async Task PathNormalization_BackAndForwardSlashesEquivalent()
    {
        var vfs = CreateVfs();
        await vfs.WriteFileAsync("/dir/file.txt", new byte[]{1});
        // Forward slash
        Assert.True(await vfs.ExistsAsync("/dir/file.txt"));
        // Backslash should be normalized
        Assert.True(await vfs.ExistsAsync("\\dir\\file.txt"));
    }
}
