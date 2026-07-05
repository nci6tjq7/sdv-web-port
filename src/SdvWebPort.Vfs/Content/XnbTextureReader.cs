using System.IO;

namespace SdvWebPort.Vfs.Content;

public enum SurfaceFormat
{
    Color = 0, Bgr565 = 1, Bgra5551 = 2, Bgra4444 = 3,
    Dxt1 = 4, Dxt3 = 5, Dxt5 = 6, NormalizedByte4 = 7,
    Rgba1010102 = 8, Rg32 = 9, Rgba64 = 10, Alpha8 = 11,
    Single = 12, Vector2 = 13, Vector4 = 14,
    HalfSingle = 15, HalfVector2 = 16, HalfVector4 = 17, HdrBlendable = 18,
}

public record XnbTextureData(SurfaceFormat Format, int Width, int Height, byte[] PixelData);

public static class XnbTextureReader
{
    public static XnbTextureData Read(XnbReader reader)
    {
        int formatValue = reader.ReadInt32();
        var format = (SurfaceFormat)formatValue;
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        int mipCount = reader.ReadInt32();

        int dataSize = reader.ReadInt32();
        byte[] pixelData = reader.ReadBytes(dataSize);

        for (int i = 1; i < mipCount; i++)
        {
            int mipSize = reader.ReadInt32();
            reader.ReadBytes(mipSize);
        }

        return new XnbTextureData(format, width, height, pixelData);
    }

    public static byte[] NormalizeToRgba(XnbTextureData texture)
    {
        if (texture.Format == SurfaceFormat.Color)
            return texture.PixelData;
        throw new NotSupportedException(
            $"SurfaceFormat {texture.Format} not supported. Only Color (RGBA) in Phase 1b.");
    }
}
