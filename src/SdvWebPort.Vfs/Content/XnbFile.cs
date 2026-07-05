using System.IO;

namespace SdvWebPort.Vfs.Content;

public sealed class XnbFile
{
    public byte Version { get; init; }
    public byte Flag { get; init; }
    public int FileSize { get; init; }
    public bool IsCompressed => Flag != 0;
    public List<TypeReaderInfo> TypeReaders { get; } = new();
    public int SharedResourceCount { get; set; }

    public static XnbFile ParseHeader(XnbReader reader)
    {
        char m1 = (char)reader.ReadByte();
        char m2 = (char)reader.ReadByte();
        char m3 = (char)reader.ReadByte();
        if (m1 != 'X' || m2 != 'N' || m3 != 'B')
            throw new InvalidDataException($"Invalid XNB magic: {m1}{m2}{m3}");

        byte version = reader.ReadByte();
        byte flag = reader.ReadByte();
        int fileSize = reader.ReadInt32();

        var xnb = new XnbFile { Version = version, Flag = flag, FileSize = fileSize };

        if (xnb.IsCompressed)
        {
            int compressedSize = reader.ReadInt32();
            // Phase 1b: uncompressed only. SDV 1.6 uses uncompressed .xnb.
        }

        int typeReaderCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < typeReaderCount; i++)
        {
            string typeName = reader.ReadXnbString();
            int readerVersion = reader.ReadInt32();
            xnb.TypeReaders.Add(new TypeReaderInfo(typeName, readerVersion));
        }

        xnb.SharedResourceCount = reader.Read7BitEncodedInt();
        return xnb;
    }
}

public record TypeReaderInfo(string TypeName, int Version);
