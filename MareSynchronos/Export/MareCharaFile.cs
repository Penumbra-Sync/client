using System.Collections.Generic;
using System.IO;

namespace MareSynchronos.Export;

public record MareCharaFile
{
    public static readonly byte CurrentVersion = 1;
    public byte Version { get; set; }
    public MareCharaFileData? CharaFileData { get; set; }
    public List<byte[]> FileData { get; set; } = new();

    public void WriteToStream(Stream stream)
    {
        using var writer = new BinaryWriter(stream);

        writer.Write('M');
        writer.Write('C');
        writer.Write('F');
        writer.Write('D');
        writer.Write(Version);
        var charaFileDataArray = CharaFileData.ToByteArray();
        writer.Write(charaFileDataArray.Length);
        writer.Write(charaFileDataArray);
        foreach (var fileData in FileData)
        {
            writer.Write(fileData);
        }
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
        if (!string.Equals(chars, "MCFD", System.StringComparison.Ordinal)) throw new System.Exception("Not a Mare Chara File");

        var version = reader.ReadByte();
        if (version == 1)
        {
            var dataLength = reader.ReadInt32();
            return MareCharaFileData.FromByteArray(reader.ReadBytes(dataLength));
        }

        return null;
    }

    public static MareCharaFile FromStream(Stream stream)
    {
        using var reader = new BinaryReader(stream);

        var chars = new string(reader.ReadChars(4));
        if (!string.Equals(chars, "MCFD", System.StringComparison.Ordinal)) throw new System.Exception("Not a Mare Chara File");

        MareCharaFile decoded = new();
        decoded.Version = reader.ReadByte();
        if (decoded.Version == 1)
        {
            var dataLength = reader.ReadInt32();
            decoded.CharaFileData = MareCharaFileData.FromByteArray(reader.ReadBytes(dataLength));

            foreach (var file in decoded.CharaFileData.Files)
            {
                List<byte> data = new List<byte>();
                var length = file.Length;
                int bytesToRead = 0;
                while (length > 0)
                {
                    if (length > int.MaxValue) bytesToRead = int.MaxValue;
                    else bytesToRead = (int)length;
                    length -= bytesToRead;
                    data.AddRange(reader.ReadBytes(bytesToRead));
                }

                decoded.FileData.Add(data.ToArray());
            }
        }

        return decoded;
    }
}
