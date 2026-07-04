using System.IO;
using System.Text;

namespace SdvWebPort.Vfs.Content;

/// <summary>
/// Binary reader for XNB file format. Handles 7-bit encoded integers
/// (variable-length encoding used by XNA content pipeline).
/// </summary>
public sealed class XnbReader : BinaryReader
{
    public XnbReader(Stream input) : base(input, Encoding.UTF8, leaveOpen: true) { }

    /// <summary>
    /// Read a 7-bit encoded integer (XNA/MonoGame content format).
    /// </summary>
    public new int Read7BitEncodedInt()
    {
        int result = 0;
        int shift = 0;
        while (true)
        {
            byte b = ReadByte();
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return result;
    }

    /// <summary>
    /// Read a string prefixed with a 7-bit encoded length.
    /// </summary>
    public string ReadXnbString()
    {
        int length = Read7BitEncodedInt();
        return Encoding.UTF8.GetString(ReadBytes(length));
    }
}
