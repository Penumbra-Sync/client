namespace MareSynchronos.Export;
using LZ4;
using MareSynchronos.API;
using MareSynchronos.FileCache;
using MareSynchronos.Utils;
using Newtonsoft.Json;
using System.IO;

public class MareCharaFileManager
{
    private readonly FileCacheManager _manager;
    private readonly MareCharaFileDataFactory _factory;
    public bool CurrentlyWorking { get; private set; } = false;

    public MareCharaFileManager(FileCacheManager manager)
    {
        _factory = new(manager);
        _manager = manager;
    }

    public MareCharaFile LoadMareCharaFile(string filePath)
    {
        CurrentlyWorking = true;
        try
        {
            using var unwrapped = new MemoryStream(LZ4Codec.Unwrap(File.ReadAllBytes(filePath)));
            var charaFile = MareCharaFile.FromStream(unwrapped);
            Logger.Debug("Read Mare Chara File");
            Logger.Debug("Version: " + charaFile.Version);
            Logger.Debug("CharaData: " + JsonConvert.SerializeObject(charaFile.CharaFileData, Formatting.Indented));
            return charaFile;
        }
        catch { throw; }
        finally { CurrentlyWorking = false; }
    }

    public void SaveMareCharaFile(CharacterCacheDto? dto, string description, string filePath)
    {
        CurrentlyWorking = true;
        try
        {
            if (dto == null) return;
            MareCharaFileData fileData = _factory.Create(description, dto);
            MareCharaFile output = new()
            {
                CharaFileData = fileData,
                Version = MareCharaFile.CurrentVersion
            };

            if (dto.FileReplacements.TryGetValue(ObjectKind.Player, out var replacement))
            {
                foreach (var item in replacement)
                {
                    var file = _manager.GetFileCacheByHash(item.Hash);
                    if (file != null)
                    {
                        output.FileData.Add(File.ReadAllBytes(file.ResolvedFilepath));
                    }
                }
            }

            File.WriteAllBytes(filePath, LZ4Codec.WrapHC(output.ToArray()));
        }
        catch { throw; }
        finally { CurrentlyWorking = false; }
    }
}
