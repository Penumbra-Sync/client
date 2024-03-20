using Dalamud.Game.ClientState.Objects.Types;
using LZ4;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using CharacterData = MareSynchronos.API.Data.CharacterData;

namespace MareSynchronos.PlayerData.Export;

public class MareCharaFileManager : DisposableMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareCharaFileDataFactory _factory;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly Dictionary<string, GameObjectHandler> _gposeGameObjects;
    private readonly IpcManager _ipcManager;
    private readonly ILogger<MareCharaFileManager> _logger;
    private readonly FileCacheManager _manager;
    private int _globalFileCounter = 0;
    private bool _isInGpose = false;

    public MareCharaFileManager(ILogger<MareCharaFileManager> logger, GameObjectHandlerFactory gameObjectHandlerFactory,
        FileCacheManager manager, IpcManager ipcManager, MareConfigService configService, DalamudUtilService dalamudUtil,
        MareMediator mediator) : base(logger, mediator)
    {
        _factory = new(manager);
        _logger = logger;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _manager = manager;
        _ipcManager = ipcManager;
        _configService = configService;
        _dalamudUtil = dalamudUtil;
        _gposeGameObjects = [];
        Mediator.Subscribe<GposeStartMessage>(this, _ => _isInGpose = true);
        Mediator.Subscribe<GposeEndMessage>(this, async _ =>
        {
            _isInGpose = false;
            CancellationTokenSource cts = new();
            foreach (var item in _gposeGameObjects)
            {
                if ((await dalamudUtil.RunOnFrameworkThread(() => item.Value.CurrentAddress()).ConfigureAwait(false)) != nint.Zero)
                {
                    await _ipcManager.Glamourer.RevertAsync(logger, item.Value.Name, item.Value, Guid.NewGuid(), cts.Token).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogDebug("Reverting by name: {name}", item.Key);
                    _ipcManager.Glamourer.RevertByName(logger, item.Key, Guid.NewGuid());
                }


                item.Value.Dispose();
            }
            _gposeGameObjects.Clear();
        });
    }

    public bool CurrentlyWorking { get; private set; } = false;
    public MareCharaFileHeader? LoadedCharaFile { get; private set; }

    public async Task ApplyMareCharaFile(GameObject? charaTarget, long expectedLength)
    {
        if (charaTarget == null) return;
        Dictionary<string, string> extractedFiles = new(StringComparer.Ordinal);
        CurrentlyWorking = true;
        try
        {
            if (LoadedCharaFile == null || !File.Exists(LoadedCharaFile.FilePath)) return;
            var unwrapped = File.OpenRead(LoadedCharaFile.FilePath);
            await using (unwrapped.ConfigureAwait(false))
            {
                CancellationTokenSource disposeCts = new();
                using var lz4Stream = new LZ4Stream(unwrapped, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
                using var reader = new BinaryReader(lz4Stream);
                MareCharaFileHeader.AdvanceReaderToData(reader);
                _logger.LogDebug("Applying to {chara}, expected length of contents: {exp}, stream length: {len}", charaTarget.Name.TextValue, expectedLength, reader.BaseStream.Length);
                extractedFiles = ExtractFilesFromCharaFile(LoadedCharaFile, reader, expectedLength);
                Dictionary<string, string> fileSwaps = new(StringComparer.Ordinal);
                foreach (var fileSwap in LoadedCharaFile.CharaFileData.FileSwaps)
                {
                    foreach (var path in fileSwap.GamePaths)
                    {
                        fileSwaps.Add(path, fileSwap.FileSwapPath);
                    }
                }
                var applicationId = Guid.NewGuid();
                await _ipcManager.Penumbra.RemoveTemporaryCollectionAsync(_logger, applicationId, charaTarget.Name.TextValue).ConfigureAwait(false);
                var coll = await _ipcManager.Penumbra.CreateTemporaryCollectionAsync(_logger, charaTarget.Name.TextValue).ConfigureAwait(false);
                await _ipcManager.Penumbra.AssignTemporaryCollectionAsync(_logger, coll, charaTarget.ObjectTableIndex()!.Value).ConfigureAwait(false);
                await _ipcManager.Penumbra.SetTemporaryModsAsync(_logger, applicationId, coll, extractedFiles.Union(fileSwaps).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal)).ConfigureAwait(false);
                await _ipcManager.Penumbra.SetManipulationDataAsync(_logger, applicationId, coll, LoadedCharaFile.CharaFileData.ManipulationData).ConfigureAwait(false);

                GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Player,
                    () => _dalamudUtil.GetGposeCharacterFromObjectTableByName(charaTarget.Name.ToString(), _isInGpose)?.Address ?? IntPtr.Zero, isWatched: false).ConfigureAwait(false);

                if (!_gposeGameObjects.ContainsKey(charaTarget.Name.ToString()))
                    _gposeGameObjects[charaTarget.Name.ToString()] = tempHandler;

                await _ipcManager.Glamourer.ApplyAllAsync(_logger, tempHandler, LoadedCharaFile.CharaFileData.GlamourerData, applicationId, disposeCts.Token).ConfigureAwait(false);
                await _ipcManager.Penumbra.RedrawAsync(_logger, tempHandler, applicationId, disposeCts.Token).ConfigureAwait(false);
                _dalamudUtil.WaitWhileGposeCharacterIsDrawing(charaTarget.Address, 30000);
                await _ipcManager.Penumbra.RemoveTemporaryCollectionAsync(_logger, applicationId, coll).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(LoadedCharaFile.CharaFileData.CustomizePlusData))
                {
                    await _ipcManager.CustomizePlus.SetBodyScaleAsync(tempHandler.Address, LoadedCharaFile.CharaFileData.CustomizePlusData).ConfigureAwait(false);
                }
                else
                {
                    await _ipcManager.CustomizePlus.RevertAsync(tempHandler.Address).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failure to read MCDF");
            throw;
        }
        finally
        {
            CurrentlyWorking = false;

            _logger.LogDebug("Clearing local files");
            foreach (var file in Directory.EnumerateFiles(_configService.Current.CacheFolder, "*.tmp"))
            {
                File.Delete(file);
            }
        }
    }

    public void ClearMareCharaFile()
    {
        LoadedCharaFile = null;
    }

    public long LoadMareCharaFile(string filePath)
    {
        CurrentlyWorking = true;
        try
        {
            using var unwrapped = File.OpenRead(filePath);
            using var lz4Stream = new LZ4Stream(unwrapped, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
            using var reader = new BinaryReader(lz4Stream);
            LoadedCharaFile = MareCharaFileHeader.FromBinaryReader(filePath, reader);

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
                        _logger.LogTrace("File {itemNr}: {gamePath} = {len}", itemNr, gamePath, item.Length.ToByteString());
                    }
                }

                _logger.LogInformation("Expected length: {expected}", expectedLength.ToByteString());
            }
            return expectedLength;
        }
        finally { CurrentlyWorking = false; }
    }

    public void SaveMareCharaFile(CharacterData? dto, string description, string filePath)
    {
        CurrentlyWorking = true;
        var tempFilePath = filePath + ".tmp";

        try
        {
            if (dto == null) return;

            var mareCharaFileData = _factory.Create(description, dto);
            MareCharaFileHeader output = new(MareCharaFileHeader.CurrentVersion, mareCharaFileData);

            using var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var lz4 = new LZ4Stream(fs, LZ4StreamMode.Compress, LZ4StreamFlags.HighCompression);
            using var writer = new BinaryWriter(lz4);
            output.WriteToStream(writer);

            foreach (var item in output.CharaFileData.Files)
            {
                var file = _manager.GetFileCacheByHash(item.Hash)!;
                _logger.LogDebug("Saving to MCDF: {hash}:{file}", item.Hash, file.ResolvedFilepath);
                _logger.LogDebug("\tAssociated GamePaths:");
                foreach (var path in item.GamePaths)
                {
                    _logger.LogDebug("\t{path}", path);
                }
                using var fsRead = File.OpenRead(file.ResolvedFilepath);
                using var br = new BinaryReader(fsRead);
                byte[] buffer = new byte[item.Length];
                br.Read(buffer, 0, item.Length);
                writer.Write(buffer);
            }
            writer.Flush();
            lz4.Flush();
            fs.Flush();
            fs.Close();
            File.Move(tempFilePath, filePath, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failure Saving Mare Chara File, deleting output");
            File.Delete(tempFilePath);
        }
        finally { CurrentlyWorking = false; }
    }

    private Dictionary<string, string> ExtractFilesFromCharaFile(MareCharaFileHeader charaFileHeader, BinaryReader reader, long expectedLength)
    {
        long totalRead = 0;
        Dictionary<string, string> gamePathToFilePath = new(StringComparer.Ordinal);
        foreach (var fileData in charaFileHeader.CharaFileData.Files)
        {
            var fileName = Path.Combine(_configService.Current.CacheFolder, "mare_" + _globalFileCounter++ + ".tmp");
            var length = fileData.Length;
            var bufferSize = length;
            using var fs = File.OpenWrite(fileName);
            using var wr = new BinaryWriter(fs);
            _logger.LogTrace("Reading {length} of {fileName}", length.ToByteString(), fileName);
            var buffer = reader.ReadBytes(bufferSize);
            wr.Write(buffer);
            wr.Flush();
            wr.Close();
            if (buffer.Length == 0) throw new EndOfStreamException("Unexpected EOF");
            foreach (var path in fileData.GamePaths)
            {
                gamePathToFilePath[path] = fileName;
                _logger.LogTrace("{path} => {fileName} [{hash}]", path, fileName, fileData.Hash);
            }
            totalRead += length;
            _logger.LogTrace("Read {read}/{expected} bytes", totalRead.ToByteString(), expectedLength.ToByteString());
        }

        return gamePathToFilePath;
    }
}