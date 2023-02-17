using Dalamud.Game.ClientState.Objects.Types;
using LZ4;
using MareSynchronos.FileCache;
using MareSynchronos.Managers;
using MareSynchronos.Utils;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Mediator;
using MareSynchronos.Models;
using CharacterData = MareSynchronos.API.Data.CharacterData;

namespace MareSynchronos.Export;
public class MareCharaFileManager
{
    private readonly MareMediator _mediator;
    private readonly FileCacheManager _manager;
    private readonly IpcManager _ipcManager;
    private readonly MareConfigService _configService;
    private readonly DalamudUtil _dalamudUtil;
    private readonly MareCharaFileDataFactory _factory;
    public MareCharaFileHeader? LoadedCharaFile { get; private set; }
    public bool CurrentlyWorking { get; private set; } = false;
    private static int GlobalFileCounter = 0;

    public MareCharaFileManager(MareMediator mediator, FileCacheManager manager, IpcManager ipcManager, MareConfigService configService, DalamudUtil dalamudUtil)
    {
        _factory = new(manager);
        _mediator = mediator;
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
            /*using var unwrapped2 = File.OpenRead(filePath);
            using var lz4Stream2 = new LZ4Stream(unwrapped2, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
            using var reader2 = new BinaryReader(lz4Stream2);
            using var writer = File.OpenWrite(filePath + ".raw");
            using var wr = new BinaryWriter(writer);
            var bufferSize = 4 * 1024 * 1024;
            var buffer = new byte[bufferSize];
            int chunk = 0;
            int length = 0;
            while ((length = reader2.Read(buffer)) > 0)
            {
                if (length < bufferSize) bufferSize = (int)length;
                Logger.Verbose($"Reading chunk {chunk++} {bufferSize}/{length} of {filePath}");
                wr.Write(length > bufferSize ? buffer : buffer.Take((int)length).ToArray());
            }*/
            Logger.Info("Read Mare Chara File");
            Logger.Info("Version: " + (LoadedCharaFile?.Version ?? -1));
            long expectedLength = 0;
            if (LoadedCharaFile != null)
            {
                Logger.Verbose("Data");
                foreach (var item in LoadedCharaFile.CharaFileData.FileSwaps)
                {
                    foreach (var gamePath in item.GamePaths)
                    {
                        Logger.Verbose("Swap: " + gamePath + " => " + item.FileSwapPath);
                    }
                }

                var itemNr = 0;
                foreach (var item in LoadedCharaFile.CharaFileData.Files)
                {
                    itemNr++;
                    expectedLength += item.Length;
                    foreach (var gamePath in item.GamePaths)
                    {
                        Logger.Verbose($"File {itemNr}: " + gamePath + " = " + item.Length);
                    }
                }

                Logger.Info("Expected length: " + expectedLength);
            }

        }
        catch { throw; }
        finally { CurrentlyWorking = false; }
    }

    public async Task ApplyMareCharaFile(GameObject? charaTarget)
    {
        Dictionary<string, string> extractedFiles = new(StringComparer.Ordinal);
        CurrentlyWorking = true;
        try
        {
            if (LoadedCharaFile == null || charaTarget == null || !File.Exists(LoadedCharaFile.FilePath)) return;
            var unwrapped = File.OpenRead(LoadedCharaFile.FilePath);
            await using (unwrapped.ConfigureAwait(false))
            {
                CancellationTokenSource disposeCts = new();
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
                _ipcManager.ToggleGposeQueueMode(on: true);
                _ipcManager.PenumbraRemoveTemporaryCollection(charaTarget.Name.TextValue);
                _ipcManager.PenumbraSetTemporaryMods(charaTarget.Name.TextValue,
                    extractedFiles.Union(fileSwaps).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal),
                    LoadedCharaFile.CharaFileData.ManipulationData);
                using GameObjectHandler tempHandler = new(_mediator, ObjectKind.Player, () => charaTarget.Address, false);
                await _ipcManager.GlamourerApplyAll(LoadedCharaFile.CharaFileData.GlamourerData, tempHandler, disposeCts.Token).ConfigureAwait(false);
                _dalamudUtil.WaitWhileGposeCharacterIsDrawing(charaTarget.Address, 30000);
                _ipcManager.PenumbraRemoveTemporaryCollection(charaTarget.Name.TextValue);
                _ipcManager.ToggleGposeQueueMode(on: false);
            }
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
        foreach (var fileData in charaFileHeader.CharaFileData.Files)
        {
            var fileName = Path.Combine(_configService.Current.CacheFolder, "mare_" + (GlobalFileCounter++) + ".tmp");
            var length = fileData.Length;
            var bufferSize = 4 * 1024 * 1024;
            var buffer = new byte[bufferSize];
            using var fs = File.OpenWrite(fileName);
            using var wr = new BinaryWriter(fs);
            int chunk = 0;
            while (length > 0)
            {
                if (length < bufferSize) bufferSize = (int)length;
                Logger.Verbose($"Reading chunk {chunk++} {bufferSize}/{length} of {fileName}");
                buffer = reader.ReadBytes(bufferSize);
                wr.Write(length > bufferSize ? buffer : buffer.Take((int)length).ToArray());
                length -= bufferSize;
            }
            wr.Flush();
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

            var playerReplacements = dto.FileReplacements[ObjectKind.Player];
            foreach(var item in output.CharaFileData.Files)
            {
                var itemFromData = playerReplacements.First(f => f.GamePaths.Any(p => item.GamePaths.Contains(p, StringComparer.OrdinalIgnoreCase)));
                var file = _manager.GetFileCacheByHash(itemFromData.Hash)!;
                var length = new FileInfo(file!.ResolvedFilepath).Length;
                using var fsRead = File.OpenRead(file.ResolvedFilepath);
                using var br = new BinaryReader(fsRead);
                int readBytes = 0;
                while ((readBytes = br.Read(buffer, 0, bufferSize)) > 0)
                {
                    writer.Write(readBytes == bufferSize ? buffer : buffer.Take(readBytes).ToArray());
                }
            }
        }
        catch { throw; }
        finally { CurrentlyWorking = false; }
    }
}
