using System.IO;
using System.Text;
using SdvWebPort.Vfs.Content;
using Xunit;

namespace SdvWebPort.Content.Tests;

public class XnbTextureReaderTests
{
    [Fact]
    public void Read_ValidTextureData_ReturnsCorrectDimensions()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write(0);  // SurfaceFormat.Color
        writer.Write(2);  // width
        writer.Write(2);  // height
        writer.Write(1);  // mipCount
        writer.Write(16); // dataSize
        writer.Write(new byte[] { 255,0,0,255, 0,255,0,255, 0,0,255,255, 255,255,255,255 });
        ms.Position = 0;
        var reader = new XnbReader(ms);
        var tex = XnbTextureReader.Read(reader);
        Assert.Equal(SurfaceFormat.Color, tex.Format);
        Assert.Equal(2, tex.Width);
        Assert.Equal(2, tex.Height);
        Assert.Equal(16, tex.PixelData.Length);
    }

    [Fact]
    public void NormalizeToRgba_ColorFormat_ReturnsDataAsIs()
    {
        var data = new byte[] { 255, 0, 0, 255 };
        var tex = new XnbTextureData(SurfaceFormat.Color, 1, 1, data);
        Assert.Equal(data, XnbTextureReader.NormalizeToRgba(tex));
    }

    [Fact]
    public void NormalizeToRgba_UnsupportedFormat_Throws()
    {
        var tex = new XnbTextureData(SurfaceFormat.Dxt5, 4, 4, new byte[8]);
        Assert.Throws<NotSupportedException>(() => XnbTextureReader.NormalizeToRgba(tex));
    }
}
