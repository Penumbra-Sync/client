using System.Collections.Generic;
using System.IO;

namespace MareSynchronos.Export;

public record MareCharaFileHeader(byte Version, MareCharaFileData CharaFileData)
{
    public static readonly byte CurrentVersion = 1;

    public byte Version { get; set; } = Version;
    public MareCharaFileData CharaFileData { get; set; } = CharaFileData;

    public void WriteToStream(Stream stream)
    {
        using var writer = new BinaryWriter(stream);

        writer.Write('M');
        writer.Write('C');
        writer.Write('D');
        writer.Write('F');
        writer.Write(Version);
        var charaFileDataArray = CharaFileData.ToByteArray();
        writer.Write(charaFileDataArray.Length);
        writer.Write(charaFileDataArray);
    }

    public byte[] ToArray()
    {
        using var stream = new MemoryStream();

        WriteToStream(stream);

        return stream.ToArray();
    }

    public static MareCharaFileData? HeaderFromStream(Stream stream)
    {
        using var reader = new BinaryReader(stream);

        var chars = new string(reader.ReadChars(4));
        if (!string.Equals(chars, "MCDF", System.StringComparison.Ordinal)) throw new System.Exception("Not a Mare Chara File");

        var version = reader.ReadByte();
        if (version == 1)
        {
            var dataLength = reader.ReadInt32();
            return MareCharaFileData.FromByteArray(reader.ReadBytes(dataLength));
        }

        return null;
    }

    public static MareCharaFileHeader? FromStream(Stream stream)
    {
        using var reader = new BinaryReader(stream);

        var chars = new string(reader.ReadChars(4));
        if (!string.Equals(chars, "MCDF", System.StringComparison.Ordinal)) throw new System.Exception("Not a Mare Chara File");

        MareCharaFileHeader? decoded = null;

        var version = reader.ReadByte();
        if (version == 1)
        {
            var dataLength = reader.ReadInt32();

            decoded = new(version, MareCharaFileData.FromByteArray(reader.ReadBytes(dataLength)));
        }

        return decoded;
    }
}
