using System.IO;
using System.Text;
using SdvWebPort.Vfs.Content;
using Xunit;

namespace SdvWebPort.Content.Tests;

public class XnbReaderTests
{
    [Fact]
    public void Read7BitEncodedInt_SingleByte_Returns0To127()
    {
        using var ms = new MemoryStream(new byte[] { 0x05 });
        var reader = new XnbReader(ms);
        Assert.Equal(5, reader.Read7BitEncodedInt());
    }

    [Fact]
    public void Read7BitEncodedInt_TwoBytes_Returns128()
    {
        using var ms = new MemoryStream(new byte[] { 0x80, 0x01 });
        var reader = new XnbReader(ms);
        Assert.Equal(128, reader.Read7BitEncodedInt());
    }

    [Fact]
    public void Read7BitEncodedInt_LargeValue_Returns300()
    {
        using var ms = new MemoryStream(new byte[] { 0xAC, 0x02 });
        var reader = new XnbReader(ms);
        Assert.Equal(300, reader.Read7BitEncodedInt());
    }

    [Fact]
    public void ReadXnbString_ReturnsCorrectString()
    {
        var bytes = new byte[] { 0x05, (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };
        using var ms = new MemoryStream(bytes);
        var reader = new XnbReader(ms);
        Assert.Equal("Hello", reader.ReadXnbString());
    }
}

public class XnbFileTests
{
    private static void Write7Bit(BinaryWriter w, int value)
    {
        while (value >= 0x80) { w.Write((byte)(value | 0x80)); value >>= 7; }
        w.Write((byte)value);
    }

    [Fact]
    public void ParseHeader_ValidXnb_ReturnsCorrectMetadata()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)'X'); writer.Write((byte)'N'); writer.Write((byte)'B');
            writer.Write((byte)5); writer.Write((byte)0); writer.Write(100);
            writer.Write((byte)1);
            var name = Encoding.UTF8.GetBytes("TestReader, TestAssembly");
            Write7Bit(writer, name.Length); writer.Write(name);
            writer.Write(0);
            writer.Write((byte)0);
        }
        ms.Position = 0;
        var reader = new XnbReader(ms);
        var xnb = XnbFile.ParseHeader(reader);

        Assert.Equal(5, xnb.Version);
        Assert.False(xnb.IsCompressed);
        Assert.Equal(100, xnb.FileSize);
        Assert.Single(xnb.TypeReaders);
        Assert.Equal("TestReader, TestAssembly", xnb.TypeReaders[0].TypeName);
        Assert.Equal(0, xnb.SharedResourceCount);
    }

    [Fact]
    public void ParseHeader_InvalidMagic_Throws()
    {
        using var ms = new MemoryStream(new byte[] { (byte)'X', (byte)'N', (byte)'A', 5, 0, 0, 0, 0, 0 });
        var reader = new XnbReader(ms);
        Assert.Throws<InvalidDataException>(() => XnbFile.ParseHeader(reader));
    }
}
