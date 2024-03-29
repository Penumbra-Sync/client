using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok;
using Lumina;
using Lumina.Data.Files;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace MareSynchronos.Services;

public sealed class XivDataAnalyzer
{
    private readonly ILogger<XivDataAnalyzer> _logger;
    private readonly FileCacheManager _fileCacheManager;
    private readonly XivDataStorageService _configService;
    private readonly GameData _luminaGameData;

    public XivDataAnalyzer(ILogger<XivDataAnalyzer> logger, FileCacheManager fileCacheManager,
        XivDataStorageService configService, IDataManager gameData)
    {
        _logger = logger;
        _fileCacheManager = fileCacheManager;
        _configService = configService;
        _luminaGameData = new GameData(gameData.GameData.DataPath.FullName);
    }

    public unsafe List<ushort>? GetSkeletonBoneIndices(nint charaPtr)
    {
        if (charaPtr == nint.Zero) return null;
        var chara = (CharacterBase*)(((Character*)charaPtr)->GameObject.DrawObject);
        var resHandles = chara->Skeleton->SkeletonResourceHandles;
        int i = -1;
        uint maxBones = 0;
        List<ushort> outputIndices = new();
        while (*(resHandles + ++i) != null)
        {
            var handle = *(resHandles + i);
            var curBones = handle->BoneCount;
            List<ushort> indices = new();
            for (ushort boneIdx = 0; boneIdx < curBones; boneIdx++)
            {
                var boneName = handle->HavokSkeleton->Bones[boneIdx].Name.String;
                if (boneName == null) continue;
                indices.Add(boneIdx);
            }
            if (curBones > maxBones)
            {
                maxBones = curBones;
                outputIndices = indices;
            }
        }

        return outputIndices;
    }

    public unsafe List<List<ushort>>? GetBoneIndicesFromPap(string hash)
    {
        if (_configService.Current.BoneDictionary.TryGetValue(hash, out var bones)) return bones;

        var cacheEntity = _fileCacheManager.GetFileCacheByHash(hash);
        if (cacheEntity == null) return null;

        using BinaryReader reader = new BinaryReader(File.Open(cacheEntity.ResolvedFilepath, FileMode.Open, FileAccess.Read, FileShare.Read));

        // most of this shit is from vfxeditor, surely nothing will change in the pap format :copium:
        reader.ReadInt32(); // ignore
        reader.ReadInt32(); // ignore
        reader.ReadInt16(); // read 2 (num animations)
        reader.ReadInt16(); // read 2 (modelid)
        var type = reader.ReadByte();// read 1 (type)
        if (type != 0) return null; // it's not human, just ignore it, whatever

        reader.ReadByte(); // read 1 (variant)
        reader.ReadInt32(); // ignore
        var havokPosition = reader.ReadInt32();
        var footerPosition = reader.ReadInt32();
        var havokDataSize = footerPosition - havokPosition;
        reader.BaseStream.Position = havokPosition;
        var havokData = reader.ReadBytes(havokDataSize);
        if (havokData.Length <= 8) return null; // no havok data

        var output = new List<List<ushort>>();
        var tempHavokDataPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()) + ".hkx";
        var tempHavokDataPathAnsi = Marshal.StringToHGlobalAnsi(tempHavokDataPath);

        try
        {
            File.WriteAllBytes(tempHavokDataPath, havokData);

            var loadoptions = stackalloc hkSerializeUtil.LoadOptions[1];
            loadoptions->TypeInfoRegistry = hkBuiltinTypeRegistry.Instance()->GetTypeInfoRegistry();
            loadoptions->ClassNameRegistry = hkBuiltinTypeRegistry.Instance()->GetClassNameRegistry();
            loadoptions->Flags = new hkFlags<hkSerializeUtil.LoadOptionBits, int>
            {
                Storage = (int)(hkSerializeUtil.LoadOptionBits.Default)
            };

            var resource = hkSerializeUtil.LoadFromFile((byte*)tempHavokDataPathAnsi, null, loadoptions);
            if (resource == null)
            {
                throw new InvalidOperationException("Resource was null after loading");
            }

            var rootLevelName = @"hkRootLevelContainer"u8;
            fixed (byte* n1 = rootLevelName)
            {
                var container = (hkRootLevelContainer*)resource->GetContentsPointer(n1, hkBuiltinTypeRegistry.Instance()->GetTypeInfoRegistry());
                var animationName = @"hkaAnimationContainer"u8;
                fixed (byte* n2 = animationName)
                {
                    var animContainer = (hkaAnimationContainer*)container->findObjectByName(n2, null);
                    for (int i = 0; i < animContainer->Bindings.Length; i++)
                    {
                        var boneTransform = animContainer->Bindings[i].ptr->TransformTrackToBoneIndices;
                        List<ushort> boneIndices = [];
                        for (int boneIdx = 0; boneIdx < boneTransform.Length; boneIdx++)
                        {
                            boneIndices.Add((ushort)boneTransform[boneIdx]);
                        }

                        output.Add(boneIndices);
                    }

                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load havok file in {path}", tempHavokDataPath);
        }
        finally
        {
            Marshal.FreeHGlobal(tempHavokDataPathAnsi);
            File.Delete(tempHavokDataPath);
        }

        _configService.Current.BoneDictionary[hash] = output;
        _configService.Save();
        return output;
    }

    public Task<long> GetTrianglesFromGamePath(string gamePath)
    {
        if (_configService.Current.TriangleDictionary.TryGetValue(gamePath, out var cachedTris))
            return Task.FromResult(cachedTris);

        _logger.LogInformation("Detected Model File {path}, calculating Tris", gamePath);
        var file = _luminaGameData.GetFile<MdlFile>(gamePath);
        if (file == null)
            return Task.FromResult((long)0);

        if (file.FileHeader.LodCount <= 0)
            return Task.FromResult((long)0);
        var meshIdx = file.Lods[0].MeshIndex;
        var meshCnt = file.Lods[0].MeshCount;
        var tris = file.Meshes.Skip(meshIdx).Take(meshCnt).Sum(p => p.IndexCount) / 3;

        _logger.LogInformation("{filePath} => {tris} triangles", gamePath, tris);
        _configService.Current.TriangleDictionary[gamePath] = tris;
        _configService.Save();
        return Task.FromResult(tris);
    }

    public Task<long> GetTrianglesByHash(string hash)
    {
        if (_configService.Current.TriangleDictionary.TryGetValue(hash, out var cachedTris))
            return Task.FromResult(cachedTris);

        var path = _fileCacheManager.GetFileCacheByHash(hash);
        if (path == null || !path.ResolvedFilepath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult((long)0);

        var filePath = path.ResolvedFilepath;

        _logger.LogInformation("Detected Model File {path}, calculating Tris", filePath);
        var file = _luminaGameData.GetFileFromDisk<MdlFile>(filePath);
        if (file.FileHeader.LodCount <= 0)
            return Task.FromResult((long)0);
        var meshIdx = file.Lods[0].MeshIndex;
        var meshCnt = file.Lods[0].MeshCount;
        var tris = file.Meshes.Skip(meshIdx).Take(meshCnt).Sum(p => p.IndexCount) / 3;

        _logger.LogInformation("{filePath} => {tris} triangles", filePath, tris);
        _configService.Current.TriangleDictionary[hash] = tris;
        _configService.Save();
        return Task.FromResult(tris);
    }
}
