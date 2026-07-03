using System.IO;
using System.Text;
using System.Threading.Tasks;
using SdvWebPort.Vfs;
using Xunit;

namespace SdvWebPort.Vfs.Tests;

public class InMemoryVfsTests
{
    [Fact]
    public async Task WriteFile_ThenExistsAsync_ReturnsTrue()
    {
        var vfs = new InMemoryVfs();
        await vfs.WriteFileAsync("/foo/bar.txt", Encoding.UTF8.GetBytes("hello"));

        var exists = await vfs.ExistsAsync("/foo/bar.txt");

        Assert.True(exists);
    }

    [Fact]
    public async Task WriteFile_ThenOpenReadAsync_ReturnsSameBytes()
    {
        var vfs = new InMemoryVfs();
        var expected = Encoding.UTF8.GetBytes("test content");
        await vfs.WriteFileAsync("/x/y.bin", expected);

        await using var stream = await vfs.OpenReadAsync("/x/y.bin");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        Assert.Equal(expected, ms.ToArray());
    }

    [Fact]
    public async Task ExistsAsync_NonExistentFile_ReturnsFalse()
    {
        var vfs = new InMemoryVfs();
        var exists = await vfs.ExistsAsync("/nope.txt");
        Assert.False(exists);
    }

    [Fact]
    public async Task EnumerateFilesAsync_ReturnsMatchingPaths()
    {
        var vfs = new InMemoryVfs();
        await vfs.WriteFileAsync("/a/1.txt", new byte[]{1});
        await vfs.WriteFileAsync("/a/2.txt", new byte[]{2});
        await vfs.WriteFileAsync("/a/3.log", new byte[]{3});

        var txtFiles = await vfs.EnumerateFilesAsync("/a", "*.txt").ToListAsync();

        Assert.Equal(2, txtFiles.Count);
        Assert.Contains("/a/1.txt", txtFiles);
        Assert.Contains("/a/2.txt", txtFiles);
    }

    [Fact]
    public async Task GetFileSizeAsync_ReturnsCorrectSize()
    {
        var vfs = new InMemoryVfs();
        var data = new byte[1024];
        await vfs.WriteFileAsync("/big.bin", data);

        var size = await vfs.GetFileSizeAsync("/big.bin");

        Assert.Equal(1024, size);
    }
}
