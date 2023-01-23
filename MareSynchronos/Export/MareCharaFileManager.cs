using System.Linq;

namespace MareSynchronos.Export;

using Dalamud.Game.ClientState.Objects.Types;
using LZ4;
using API;
using FileCache;
using Managers;
using Utils;
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
            using var unwrapped = File.OpenRead(filePath);
            using var lz4Stream = new LZ4Stream(unwrapped, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
            var charaFile = MareCharaFileHeader.FromStream(lz4Stream);
            Logger.Debug("Read Mare Chara File");
            Logger.Debug("Version: " + charaFile.Version);
            Logger.Debug("Applying to " + charaTarget.Name.TextValue);
            var extractedFiles = ExtractFilesFromCharaFile(charaFile, lz4Stream);
            Dictionary<string, string> fileSwaps = new(StringComparer.Ordinal);
            foreach (var fileSwap in charaFile.CharaFileData.FileSwaps)
            {
                foreach (var path in fileSwap.GamePaths)
                {
                    fileSwaps.Add(path, fileSwap.FileSwapPath);
                }
            }
            _ipcManager.ToggleGposeQueueMode(true);
            _ipcManager.PenumbraRemoveTemporaryCollection(charaTarget.Name.TextValue);
            _ipcManager.PenumbraSetTemporaryMods(charaTarget.Name.TextValue,
                extractedFiles.Union(fileSwaps).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal),
                charaFile.CharaFileData.ManipulationData);
            _ipcManager.GlamourerApplyAll(charaFile.CharaFileData.GlamourerData, charaTarget.Address);
            _ipcManager.ToggleGposeQueueMode(false);
            System.Threading.Thread.Sleep(2000);
            _ipcManager.ToggleGposeQueueMode(true);
            _ipcManager.PenumbraRemoveTemporaryCollection(charaTarget.Name.TextValue);
            _ipcManager.ToggleGposeQueueMode(false);
            Logger.Debug("Clearing local files");
            foreach (var file in extractedFiles)
            {
                File.Delete(file.Value);
            }
        }
        catch { throw; }
        finally { CurrentlyWorking = false; }
    }

    private Dictionary<string, string> ExtractFilesFromCharaFile(MareCharaFileHeader charaFileHeader, Stream stream)
    {
        Dictionary<string, string> gamePathToFilePath = new(StringComparer.Ordinal);
        foreach (var fileData in charaFileHeader.CharaFileData.Files)
        {
            var fileName = Path.GetTempFileName();
            var length = fileData.Length;
            var bufferSize = 4 * 1024 * 1024;
            var buffer = new byte[bufferSize > length ? (int) length : bufferSize];
            using var fs = File.OpenWrite(fileName);
            while (length > 0)
            {
                if (length < bufferSize) bufferSize = (int)length;
                var bytesRead = stream.Read(buffer, 0, bufferSize);
                fs.Write(buffer, 0, bytesRead);
                length -= bufferSize;
            }
            fs.Flush();
            foreach (var path in fileData.GamePaths)
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

            var mareCharaFileData = _factory.Create(description, dto);
            MareCharaFileHeader output = new(MareCharaFileHeader.CurrentVersion, mareCharaFileData);

            using var fs = File.OpenWrite(filePath);
            using var lz4 = new LZ4Stream(fs, LZ4StreamMode.Compress, LZ4StreamFlags.HighCompression);
            output.WriteToStream(lz4);

            if (dto.FileReplacements.TryGetValue(ObjectKind.Player, out var replacement))
            {
                foreach (var file in replacement.Select(item => _manager.GetFileCacheByHash(item.Hash)).Where(file => file != null))
                {
                    lz4.Write(File.ReadAllBytes(file.ResolvedFilepath));
                }
            }

            lz4.Flush();
            fs.Flush();
        }
        catch { throw; }
        finally { CurrentlyWorking = false; }
    }
}
