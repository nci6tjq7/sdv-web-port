namespace SdvWebPort.Vfs.Content;

/// <summary>
/// Interface for decompressing XNB content.
/// Implemented in the Runtime project using KNI's LzxDecoderStream.
/// </summary>
public interface IContentDecompressor
{
    /// <summary>
    /// Decompress LZX-compressed XNB data.
    /// </summary>
    byte[] DecompressLzx(byte[] compressedData, int decompressedSize);

    /// <summary>
    /// Decompress LZ4-compressed XNB data.
    /// </summary>
    byte[] DecompressLz4(byte[] compressedData, int decompressedSize);
}
