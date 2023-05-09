using Dalamud.Game.ClientState.Objects.Types;
using LZ4;
using MareSynchronos.FileCache;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.MareConfiguration;
using CharacterData = MareSynchronos.API.Data.CharacterData;
using Microsoft.Extensions.Logging;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Interop;
using MareSynchronos.Services;
using MareSynchronos.Utils;
using MareSynchronos.PlayerData.Factories;

namespace MareSynchronos.PlayerData.Export;

public class MareCharaFileManager
{
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareCharaFileDataFactory _factory;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly IpcManager _ipcManager;
    private readonly ILogger<MareCharaFileManager> _logger;
    private readonly FileCacheManager _manager;
    private int _globalFileCounter = 0;

    public MareCharaFileManager(ILogger<MareCharaFileManager> logger, GameObjectHandlerFactory gameObjectHandlerFactory,
        FileCacheManager manager, IpcManager ipcManager, MareConfigService configService, DalamudUtilService dalamudUtil)
    {
        _factory = new(manager);
        _logger = logger;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _manager = manager;
        _ipcManager = ipcManager;
        _configService = configService;
        _dalamudUtil = dalamudUtil;
    }

    public bool CurrentlyWorking { get; private set; } = false;
    public MareCharaFileHeader? LoadedCharaFile { get; private set; }

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
                MareCharaFileHeader.AdvanceReaderToData(reader);
                _logger.LogDebug("Applying to {chara}", charaTarget.Name.TextValue);
                extractedFiles = ExtractFilesFromCharaFile(LoadedCharaFile, reader);
                Dictionary<string, string> fileSwaps = new(StringComparer.Ordinal);
                foreach (var fileSwap in LoadedCharaFile.CharaFileData.FileSwaps)
                {
                    foreach (var path in fileSwap.GamePaths)
                    {
                        fileSwaps.Add(path, fileSwap.FileSwapPath);
                    }
                }
                var applicationId = Guid.NewGuid();
                _ipcManager.ToggleGposeQueueMode(on: true);
                await _ipcManager.PenumbraRemoveTemporaryCollectionAsync(_logger, applicationId, charaTarget.Name.TextValue).ConfigureAwait(false);
                var coll = await _ipcManager.PenumbraCreateTemporaryCollectionAsync(_logger, charaTarget.Name.TextValue).ConfigureAwait(false);
                await _ipcManager.PenumbraAssignTemporaryCollectionAsync(_logger, coll, charaTarget.ObjectTableIndex()!.Value).ConfigureAwait(false);
                await _ipcManager.PenumbraSetTemporaryModsAsync(_logger, applicationId, coll, extractedFiles.Union(fileSwaps).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal)).ConfigureAwait(false);
                await _ipcManager.PenumbraSetManipulationDataAsync(_logger, applicationId, coll, LoadedCharaFile.CharaFileData.ManipulationData).ConfigureAwait(false);
                using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Player, () => charaTarget.Address, false).ConfigureAwait(false);
                await _ipcManager.GlamourerApplyAllAsync(_logger, tempHandler, LoadedCharaFile.CharaFileData.GlamourerData, applicationId, disposeCts.Token).ConfigureAwait(false);
                _dalamudUtil.WaitWhileGposeCharacterIsDrawing(charaTarget.Address, 30000);
                await _ipcManager.PenumbraRemoveTemporaryCollectionAsync(_logger, applicationId, coll).ConfigureAwait(false);
                _ipcManager.ToggleGposeQueueMode(on: false);
            }
        }
        finally
        {
            CurrentlyWorking = false;

            _logger.LogDebug("Clearing local files");
            foreach (var file in extractedFiles)
            {
                File.Delete(file.Value);
            }
        }
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
                _logger.LogTrace($"Reading chunk {chunk++} {bufferSize}/{length} of {filePath}");
                wr.Write(length > bufferSize ? buffer : buffer.Take((int)length).ToArray());
            }*/
            _logger.LogInformation("Read Mare Chara File");
            _logger.LogInformation("Version: {ver}", (LoadedCharaFile?.Version ?? -1));
            long expectedLength = 0;
            if (LoadedCharaFile != null)
            {
                _logger.LogTrace("Data");
                foreach (var item in LoadedCharaFile.CharaFileData.FileSwaps)
                {
                    foreach (var gamePath in item.GamePaths)
                    {
                        _logger.LogTrace("Swap: {gamePath} => {fileSwapPath}", gamePath, item.FileSwapPath);
                    }
                }

                var itemNr = 0;
                foreach (var item in LoadedCharaFile.CharaFileData.Files)
                {
                    itemNr++;
                    expectedLength += item.Length;
                    foreach (var gamePath in item.GamePaths)
                    {
                        _logger.LogTrace("File {itemNr}: {gamePath} = {len}", itemNr, gamePath, item.Length);
                    }
                }

                _logger.LogInformation("Expected length: {expected}", expectedLength);
            }
        }
        finally { CurrentlyWorking = false; }
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
            foreach (var item in output.CharaFileData.Files)
            {
                var itemFromData = playerReplacements.First(f => f.GamePaths.Any(p => item.GamePaths.Contains(p, StringComparer.OrdinalIgnoreCase)));
                var file = _manager.GetFileCacheByHash(itemFromData.Hash)!;
                using var fsRead = File.OpenRead(file.ResolvedFilepath);
                using var br = new BinaryReader(fsRead);
                int readBytes = 0;
                while ((readBytes = br.Read(buffer, 0, bufferSize)) > 0)
                {
                    writer.Write(readBytes == bufferSize ? buffer : buffer.Take(readBytes).ToArray());
                }
            }
        }
        finally { CurrentlyWorking = false; }
    }

    private Dictionary<string, string> ExtractFilesFromCharaFile(MareCharaFileHeader charaFileHeader, BinaryReader reader)
    {
        Dictionary<string, string> gamePathToFilePath = new(StringComparer.Ordinal);
        foreach (var fileData in charaFileHeader.CharaFileData.Files)
        {
            var fileName = Path.Combine(_configService.Current.CacheFolder, "mare_" + _globalFileCounter++ + ".tmp");
            var length = fileData.Length;
            var bufferSize = 4 * 1024 * 1024;
            using var fs = File.OpenWrite(fileName);
            using var wr = new BinaryWriter(fs);
            int chunk = 0;
            while (length > 0)
            {
                if (length < bufferSize) bufferSize = (int)length;
                _logger.LogTrace("Reading chunk {chunk} {bufferSize}/{length} of {fileName}", chunk++, bufferSize, length, fileName);
                var buffer = reader.ReadBytes(bufferSize);
                wr.Write(length > bufferSize ? buffer : buffer.Take((int)length).ToArray());
                length -= bufferSize;
            }
            wr.Flush();
            foreach (var path in fileData.GamePaths)
            {
                gamePathToFilePath[path] = fileName;
                _logger.LogTrace("{path} => {fileName}", path, fileName);
            }
        }

        return gamePathToFilePath;
    }
}