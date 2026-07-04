using System.IO;
using SdvWebPort.Vfs.Content;

namespace SdvWebPort.Runtime.Content;

/// <summary>
/// XNB content decompressor using KNI's LzxDecoderStream and Lz4DecoderStream.
/// </summary>
public sealed class KniContentDecompressor : IContentDecompressor
{
    public byte[] DecompressLzx(byte[] compressedData, int decompressedSize)
    {
        // Use reflection to avoid compile-time dependency on KNI Content internals
        var asm = typeof(Microsoft.Xna.Framework.Game).Assembly;
        // LzxDecoderStream is in a different assembly
        var contentAsm = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Xna.Framework.Content");
        var lzxType = contentAsm.GetType("Microsoft.Xna.Framework.Content.LzxDecoderStream");

        using var cs = new MemoryStream(compressedData);
        var lzx = Activator.CreateInstance(lzxType, cs, decompressedSize, compressedData.Length)!;
        using var ds = new MemoryStream();
        byte[] buf = new byte[8192];
        while (true)
        {
            int r = ((Stream)lzx).Read(buf, 0, buf.Length);
            if (r <= 0) break;
            ds.Write(buf, 0, r);
        }
        return ds.ToArray();
    }

    public byte[] DecompressLz4(byte[] compressedData, int decompressedSize)
    {
        var contentAsm = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Xna.Framework.Content");
        var lz4Type = contentAsm.GetType("Microsoft.Xna.Framework.Content.Lz4DecoderStream");

        using var cs = new MemoryStream(compressedData);
        var lz4 = Activator.CreateInstance(lz4Type, cs, (long)compressedData.Length)!;
        using var ds = new MemoryStream();
        byte[] buf = new byte[8192];
        while (true)
        {
            int r = ((Stream)lz4).Read(buf, 0, buf.Length);
            if (r <= 0) break;
            ds.Write(buf, 0, r);
        }
        return ds.ToArray();
    }
}
