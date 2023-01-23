namespace MareSynchronos.Export;

using Dalamud.Game.ClientState.Objects.Types;
using LZ4;
using MareSynchronos.API;
using MareSynchronos.FileCache;
using MareSynchronos.Managers;
using MareSynchronos.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

public class MareCharaFileManager
{
    private readonly FileCacheManager _manager;
    private readonly IpcManager _ipcManager;
    private readonly DalamudUtil _dalamudUtil;
    private readonly MareCharaFileDataFactory _factory;
    public bool CurrentlyWorking { get; private set; } = false;

    public MareCharaFileManager(FileCacheManager manager, IpcManager ipcManager, DalamudUtil dalamudUtil)
    {
        _factory = new(manager);
        _manager = manager;
        _ipcManager = ipcManager;
        this._dalamudUtil = dalamudUtil;
    }

    public void LoadMareCharaFile(string filePath, GameObject charaTarget)
    {
        CurrentlyWorking = true;
        try
        {
            using var unwrapped = new MemoryStream(LZ4Codec.Unwrap(File.ReadAllBytes(filePath)));
            var charaFile = MareCharaFile.FromStream(unwrapped);
            Logger.Debug("Read Mare Chara File");
            Logger.Debug("Version: " + charaFile.Version);
            Logger.Debug("Applying to " + charaTarget.Name.TextValue);
            var extractedFiles = ExtractFilesFromCharaFile(charaFile);
            _ipcManager.ToggleGposeQueueMode();
            _ipcManager.PenumbraRemoveTemporaryCollection(charaTarget.Name.TextValue);
            _ipcManager.PenumbraSetTemporaryMods(charaTarget.Name.TextValue, extractedFiles, charaFile.CharaFileData.ManipulationData);
            System.Threading.Thread.Sleep(2000);
            _ipcManager.GlamourerApplyAll(charaFile.CharaFileData.GlamourerData, charaTarget.Address);
            _ipcManager.PenumbraRemoveTemporaryCollection(charaTarget.Name.TextValue);
            _ipcManager.ToggleGposeQueueMode();
            Logger.Debug("Clearing local files");
            foreach (var file in extractedFiles)
            {
                File.Delete(file.Value);
            }
            charaFile = null;
        }
        catch { throw; }
        finally { CurrentlyWorking = false; }
    }

    private Dictionary<string, string> ExtractFilesFromCharaFile(MareCharaFile charaFile)
    {
        Dictionary<string, string> gamePathToFilePath = new(StringComparer.Ordinal);
        for (int i = 0; i < charaFile.CharaFileData.Files.Count; i++)
        {
            var fileName = Path.GetTempFileName();
            File.WriteAllBytes(fileName, charaFile.FileData[i]);
            foreach (var path in charaFile.CharaFileData.Files[i].GamePaths)
            {
                gamePathToFilePath[path] = fileName;
                Logger.Verbose(path + " => " + fileName);
            }
        }

        return gamePathToFilePath;
    }

    public void SaveMareCharaFile(CharacterCacheDto? dto, string description, string filePath)
    {
        CurrentlyWorking = true;
        try
        {
            if (dto == null) return;
            MareCharaFileData mareCharaFileData = _factory.Create(description, dto);
            MareCharaFile output = new(MareCharaFile.CurrentVersion, mareCharaFileData);

            if (dto.FileReplacements.TryGetValue(API.ObjectKind.Player, out var replacement))
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
