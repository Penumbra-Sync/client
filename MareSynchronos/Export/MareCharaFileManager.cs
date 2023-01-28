using Dalamud.Game.ClientState.Objects.Types;
using LZ4;
using MareSynchronos.FileCache;
using MareSynchronos.Managers;
using MareSynchronos.Utils;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.MareConfiguration;

namespace MareSynchronos.Export;
public class MareCharaFileManager
{
    private readonly FileCacheManager _manager;
    private readonly IpcManager _ipcManager;
    private readonly ConfigurationService _configService;
    private readonly DalamudUtil _dalamudUtil;
    private readonly MareCharaFileDataFactory _factory;
    public MareCharaFileHeader? LoadedCharaFile { get; private set; }
    public bool CurrentlyWorking { get; private set; } = false;

    public MareCharaFileManager(FileCacheManager manager, IpcManager ipcManager, ConfigurationService configService, DalamudUtil dalamudUtil)
    {
        _factory = new(manager);
        _manager = manager;
        _ipcManager = ipcManager;
        _configService = configService;
        _dalamudUtil = dalamudUtil;
    }

    public void ClearMareCharaFile()
    {
        LoadedCharaFile = null;
    }

    public void LoadMareCharaFile(string filePath)
    {
        CurrentlyWorking = true;
        try
        {
            using var unwrapped = File.OpenRead(filePath);
            using var lz4Stream = new LZ4Stream(unwrapped, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
            using var reader = new BinaryReader(lz4Stream);
            LoadedCharaFile = MareCharaFileHeader.FromBinaryReader(filePath, reader);
            Logger.Debug("Read Mare Chara File");
            Logger.Debug("Version: " + LoadedCharaFile.Version);

        }
        catch { throw; }
        finally { CurrentlyWorking = false; }
    }

    public Task ApplyMareCharaFile(GameObject? charaTarget)
    {
        Dictionary<string, string> extractedFiles = new();
        CurrentlyWorking = true;
        try
        {
            if (LoadedCharaFile == null || charaTarget == null || !File.Exists(LoadedCharaFile.FilePath)) return Task.CompletedTask;

            using var unwrapped = File.OpenRead(LoadedCharaFile.FilePath);
            using var lz4Stream = new LZ4Stream(unwrapped, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
            using var reader = new BinaryReader(lz4Stream);
            LoadedCharaFile.AdvanceReaderToData(reader);
            Logger.Debug("Applying to " + charaTarget.Name.TextValue);
            extractedFiles = ExtractFilesFromCharaFile(LoadedCharaFile, reader);
            Dictionary<string, string> fileSwaps = new(StringComparer.Ordinal);
            foreach (var fileSwap in LoadedCharaFile.CharaFileData.FileSwaps)
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
                LoadedCharaFile.CharaFileData.ManipulationData);
            _ipcManager.GlamourerApplyAll(LoadedCharaFile.CharaFileData.GlamourerData, charaTarget.Address);
            _dalamudUtil.WaitWhileGposeCharacterIsDrawing(charaTarget.Address);
            _ipcManager.PenumbraRemoveTemporaryCollection(charaTarget.Name.TextValue);
            _ipcManager.ToggleGposeQueueMode(false);
            return Task.CompletedTask;
        }
        catch { throw; }
        finally
        {
            CurrentlyWorking = false;

            Logger.Debug("Clearing local files");
            foreach (var file in extractedFiles)
            {
                File.Delete(file.Value);
            }
        }
    }

    private Dictionary<string, string> ExtractFilesFromCharaFile(MareCharaFileHeader charaFileHeader, BinaryReader reader)
    {
        Dictionary<string, string> gamePathToFilePath = new(StringComparer.Ordinal);
        int i = 0;
        foreach (var fileData in charaFileHeader.CharaFileData.Files)
        {
            var fileName = Path.Combine(_configService.Current.CacheFolder, "mare_" + (i++) + ".tmp");
            var length = fileData.Length;
            var bufferSize = 4 * 1024 * 1024;
            var buffer = new byte[bufferSize];
            using var fs = File.OpenWrite(fileName);
            using var wr = new BinaryWriter(fs);
            while (length > 0)
            {
                if (length < bufferSize) bufferSize = (int)length;
                buffer = reader.ReadBytes(bufferSize);
                wr.Write(length > bufferSize ? buffer : buffer.Take((int)length).ToArray());
                length -= bufferSize;
            }
            foreach (var path in fileData.GamePaths)
            {
                gamePathToFilePath[path] = fileName;
                Logger.Verbose(path + " => " + fileName);
            }
        }

        return gamePathToFilePath;
    }

    public void SaveMareCharaFile(CharacterData? dto, string description, string filePath)
    {
        CurrentlyWorking = true;
        try
        {
            if (dto == null) return;

            var mareCharaFileData = _factory.Create(description, dto);
            MareCharaFileHeader output = new(MareCharaFileHeader.CurrentVersion, mareCharaFileData);

            using var fs = new FileStream(filePath, FileMode.Create);
            using var lz4 = new LZ4Stream(fs, LZ4StreamMode.Compress, LZ4StreamFlags.HighCompression);
            using var writer = new BinaryWriter(lz4);
            output.WriteToStream(writer);
            var bufferSize = 4 * 1024 * 1024;
            byte[] buffer = new byte[bufferSize];

            if (dto.FileReplacements.TryGetValue(ObjectKind.Player, out var replacement))
            {
                foreach (var file in replacement.Select(item => _manager.GetFileCacheByHash(item.Hash)).Where(file => file != null))
                {
                    var length = new FileInfo(file.ResolvedFilepath).Length;
                    using var fsRead = File.OpenRead(file.ResolvedFilepath);
                    using var br = new BinaryReader(fsRead);
                    int readBytes = 0;
                    while ((readBytes = br.Read(buffer, 0, bufferSize)) > 0)
                    {
                        writer.Write(readBytes == bufferSize ? buffer : buffer.Take(readBytes).ToArray());
                    }
                }
            }
        }
        catch { throw; }
        finally { CurrentlyWorking = false; }
    }
}
