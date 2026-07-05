using System.IO;
using System.Text;
using SdvWebPort.Vfs;
using SdvWebPort.Vfs.Content;
using Xunit;

namespace SdvWebPort.Content.Tests;

public class VfsContentManagerTests
{
    private static void Write7Bit(BinaryWriter w, int value)
    {
        while (value >= 0x80) { w.Write((byte)(value | 0x80)); value >>= 7; }
        w.Write((byte)value);
    }

    private static byte[] CreateMinimalXnbTexture(int width, int height, byte[] pixelData)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)'X'); writer.Write((byte)'N'); writer.Write((byte)'B');
        writer.Write((byte)5); writer.Write((byte)0); writer.Write(0);
        writer.Write((byte)1);
        var name = Encoding.UTF8.GetBytes("Microsoft.Xna.Framework.Content.Texture2DReader, MonoGame.Framework");
        Write7Bit(writer, name.Length); writer.Write(name);
        writer.Write(0);
        writer.Write((byte)0);
        writer.Write((byte)0); // type reader index = 0
        writer.Write(0);  // SurfaceFormat.Color
        writer.Write(width);
        writer.Write(height);
        writer.Write(1);  // mipCount
        writer.Write(pixelData.Length);
        writer.Write(pixelData);
        return ms.ToArray();
    }

    [Fact]
    public async Task LoadTextureAsync_ValidXnb_ReturnsTextureData()
    {
        var vfs = new InMemoryVfs();
        var pixels = new byte[] { 255,0,0,255, 0,255,0,255, 0,0,255,255, 255,255,255,255 };
        var xnbData = CreateMinimalXnbTexture(2, 2, pixels);
        await vfs.WriteFileAsync("Content/test.xnb", xnbData);

        var cm = new VfsContentManager(vfs);
        var tex = await cm.LoadTextureAsync("Content/test");
        Assert.Equal(2, tex.Width);
        Assert.Equal(2, tex.Height);
        Assert.Equal(pixels, tex.PixelData);
    }

    [Fact]
    public async Task LoadTextureAsync_CachesResult()
    {
        var vfs = new InMemoryVfs();
        var xnbData = CreateMinimalXnbTexture(1, 1, new byte[] { 255, 0, 0, 255 });
        await vfs.WriteFileAsync("Content/cached.xnb", xnbData);
        var cm = new VfsContentManager(vfs);
        var t1 = await cm.LoadTextureAsync("Content/cached");
        var t2 = await cm.LoadTextureAsync("Content/cached");
        Assert.Same(t1, t2);
    }

    [Fact]
    public async Task LoadTextureAsync_NotFound_Throws()
    {
        var vfs = new InMemoryVfs();
        var cm = new VfsContentManager(vfs);
        await Assert.ThrowsAsync<FileNotFoundException>(() => cm.LoadTextureAsync("Content/missing"));
    }
}
