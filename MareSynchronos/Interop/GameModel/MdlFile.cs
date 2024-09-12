using Lumina.Data;
using Lumina.Extensions;
using System.Text;
using static Lumina.Data.Parsing.MdlStructs;

namespace MareSynchronos.Interop.GameModel;

#pragma warning disable S1104 // Fields should not have public accessibility

// This code is completely and shamelessly borrowed from Penumbra to load V5 and V6 model files.
// Original Source: https://github.com/Ottermandias/Penumbra.GameData/blob/main/Files/MdlFile.cs
public class MdlFile
{
    public const int V5 = 0x01000005;
    public const int V6 = 0x01000006;
    public const uint NumVertices = 17;
    public const uint FileHeaderSize = 0x44;

    // Raw data to write back.
    public uint Version = 0x01000005;
    public float Radius;
    public float ModelClipOutDistance;
    public float ShadowClipOutDistance;
    public byte BgChangeMaterialIndex;
    public byte BgCrestChangeMaterialIndex;
    public ushort CullingGridCount;
    public byte Flags3;
    public byte Unknown6;
    public ushort Unknown8;
    public ushort Unknown9;

    // Offsets are stored relative to RuntimeSize instead of file start.
    public uint[] VertexOffset = [0, 0, 0];
    public uint[] IndexOffset = [0, 0, 0];

    public uint[] VertexBufferSize = [0, 0, 0];
    public uint[] IndexBufferSize = [0, 0, 0];
    public byte LodCount;
    public bool EnableIndexBufferStreaming;
    public bool EnableEdgeGeometry;

    public ModelFlags1 Flags1;
    public ModelFlags2 Flags2;

    public VertexDeclarationStruct[] VertexDeclarations = [];
    public ElementIdStruct[] ElementIds = [];
    public MeshStruct[] Meshes = [];
    public BoundingBoxStruct[] BoneBoundingBoxes = [];
    public LodStruct[] Lods = [];
    public ExtraLodStruct[] ExtraLods = [];

    public MdlFile(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var r = new LuminaBinaryReader(stream);

        var header = LoadModelFileHeader(r);
        LodCount = header.LodCount;
        VertexBufferSize = header.VertexBufferSize;
        IndexBufferSize = header.IndexBufferSize;
        VertexOffset = header.VertexOffset;
        IndexOffset = header.IndexOffset;

        var dataOffset = FileHeaderSize + header.RuntimeSize + header.StackSize;
        for (var i = 0; i < LodCount; ++i)
        {
            VertexOffset[i] -= dataOffset;
            IndexOffset[i] -= dataOffset;
        }

        VertexDeclarations = new VertexDeclarationStruct[header.VertexDeclarationCount];
        for (var i = 0; i < header.VertexDeclarationCount; ++i)
            VertexDeclarations[i] = VertexDeclarationStruct.Read(r);

        _ = LoadStrings(r);

        var modelHeader = LoadModelHeader(r);
        ElementIds = new ElementIdStruct[modelHeader.ElementIdCount];
        for (var i = 0; i < modelHeader.ElementIdCount; i++)
            ElementIds[i] = ElementIdStruct.Read(r);

        Lods = new LodStruct[3];
        for (var i = 0; i < 3; i++)
        {
            var lod = r.ReadStructure<LodStruct>();
            if (i < LodCount)
            {
                lod.VertexDataOffset -= dataOffset;
                lod.IndexDataOffset -= dataOffset;
            }

            Lods[i] = lod;
        }

        ExtraLods = (modelHeader.Flags2 & ModelFlags2.ExtraLodEnabled) != 0
            ? r.ReadStructuresAsArray<ExtraLodStruct>(3)
            : [];

        Meshes = new MeshStruct[modelHeader.MeshCount];
        for (var i = 0; i < modelHeader.MeshCount; i++)
            Meshes[i] = MeshStruct.Read(r);
    }

    private ModelFileHeader LoadModelFileHeader(LuminaBinaryReader r)
    {
        var header = ModelFileHeader.Read(r);
        Version = header.Version;
        EnableIndexBufferStreaming = header.EnableIndexBufferStreaming;
        EnableEdgeGeometry = header.EnableEdgeGeometry;
        return header;
    }

    private ModelHeader LoadModelHeader(BinaryReader r)
    {
        var modelHeader = r.ReadStructure<ModelHeader>();
        Radius = modelHeader.Radius;
        Flags1 = modelHeader.Flags1;
        Flags2 = modelHeader.Flags2;
        ModelClipOutDistance = modelHeader.ModelClipOutDistance;
        ShadowClipOutDistance = modelHeader.ShadowClipOutDistance;
        CullingGridCount = modelHeader.CullingGridCount;
        Flags3 = modelHeader.Flags3;
        Unknown6 = modelHeader.Unknown6;
        Unknown8 = modelHeader.Unknown8;
        Unknown9 = modelHeader.Unknown9;
        BgChangeMaterialIndex = modelHeader.BGChangeMaterialIndex;
        BgCrestChangeMaterialIndex = modelHeader.BGCrestChangeMaterialIndex;

        return modelHeader;
    }

    private static (uint[], string[]) LoadStrings(BinaryReader r)
    {
        var stringCount = r.ReadUInt16();
        r.ReadUInt16();
        var stringSize = (int)r.ReadUInt32();
        var stringData = r.ReadBytes(stringSize);
        var start = 0;
        var strings = new string[stringCount];
        var offsets = new uint[stringCount];
        for (var i = 0; i < stringCount; ++i)
        {
            var span = stringData.AsSpan(start);
            var idx = span.IndexOf((byte)'\0');
            strings[i] = Encoding.UTF8.GetString(span[..idx]);
            offsets[i] = (uint)start;
            start = start + idx + 1;
        }

        return (offsets, strings);
    }

    public unsafe struct ModelHeader
    {
        // MeshHeader
        public float Radius;
        public ushort MeshCount;
        public ushort AttributeCount;
        public ushort SubmeshCount;
        public ushort MaterialCount;
        public ushort BoneCount;
        public ushort BoneTableCount;
        public ushort ShapeCount;
        public ushort ShapeMeshCount;
        public ushort ShapeValueCount;
        public byte LodCount;
        public ModelFlags1 Flags1;
        public ushort ElementIdCount;
        public byte TerrainShadowMeshCount;
        public ModelFlags2 Flags2;
        public float ModelClipOutDistance;
        public float ShadowClipOutDistance;
        public ushort CullingGridCount;
        public ushort TerrainShadowSubmeshCount;
        public byte Flags3;
        public byte BGChangeMaterialIndex;
        public byte BGCrestChangeMaterialIndex;
        public byte Unknown6;
        public ushort BoneTableArrayCountTotal;
        public ushort Unknown8;
        public ushort Unknown9;
        private fixed byte _padding[6];
    }

    public struct ShapeStruct
    {
        public uint StringOffset;
        public ushort[] ShapeMeshStartIndex;
        public ushort[] ShapeMeshCount;

        public static ShapeStruct Read(LuminaBinaryReader br)
        {
            ShapeStruct ret = new ShapeStruct();
            ret.StringOffset = br.ReadUInt32();
            ret.ShapeMeshStartIndex = br.ReadUInt16Array(3);
            ret.ShapeMeshCount = br.ReadUInt16Array(3);
            return ret;
        }
    }

    [Flags]
    public enum ModelFlags1 : byte
    {
        DustOcclusionEnabled = 0x80,
        SnowOcclusionEnabled = 0x40,
        RainOcclusionEnabled = 0x20,
        Unknown1 = 0x10,
        LightingReflectionEnabled = 0x08,
        WavingAnimationDisabled = 0x04,
        LightShadowDisabled = 0x02,
        ShadowDisabled = 0x01,
    }

    [Flags]
    public enum ModelFlags2 : byte
    {
        Unknown2 = 0x80,
        BgUvScrollEnabled = 0x40,
        EnableForceNonResident = 0x20,
        ExtraLodEnabled = 0x10,
        ShadowMaskEnabled = 0x08,
        ForceLodRangeEnabled = 0x04,
        EdgeGeometryEnabled = 0x02,
        Unknown3 = 0x01
    }

    public struct VertexDeclarationStruct
    {
        // There are always 17, but stop when stream = -1
        public VertexElement[] VertexElements;

        public static VertexDeclarationStruct Read(LuminaBinaryReader br)
        {
            VertexDeclarationStruct ret = new VertexDeclarationStruct();

            var elems = new List<VertexElement>();

            // Read the vertex elements that we need
            var thisElem = br.ReadStructure<VertexElement>();
            do
            {
                elems.Add(thisElem);
                thisElem = br.ReadStructure<VertexElement>();
            } while (thisElem.Stream != 255);

            // Skip the number of bytes that we don't need to read
            // We skip elems.Count * 9 because we had to read the invalid element
            int toSeek = 17 * 8 - (elems.Count + 1) * 8;
            br.Seek(br.BaseStream.Position + toSeek);

            ret.VertexElements = elems.ToArray();

            return ret;
        }
    }
}
#pragma warning restore S1104 // Fields should not have public accessibility