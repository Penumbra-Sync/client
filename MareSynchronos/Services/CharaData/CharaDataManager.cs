using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using LZ4;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.FileCache;
using MareSynchronos.Interop;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.CharaData;
using MareSynchronos.Services.CharaData.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Text;

namespace MareSynchronos.Services;

internal sealed partial class CharaDataManager : DisposableMediatorSubscriberBase
{
    public sealed record HandledCharaDataEntry(string Name, bool IsSelf, Guid? CustomizePlus, string DataId);

    private readonly ApiController _apiController;
    private readonly CharaDataConfigService _charaDataConfigService;
    private readonly VfxSpawnManager _vfxSpawnManager;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileDownloadManager _fileDownloadManager;
    private readonly FileUploadManager _fileUploadManager;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly HashSet<HandledCharaDataEntry> _handledCharaData = [];
    private readonly IpcManager _ipcManager;
    private readonly MareCharaFileDataFactory _mareCharaFileDataFactory;
    private readonly Dictionary<string, CharaDataMetaInfoDto?> _metaInfoCache = [];
    private readonly Dictionary<string, CharaDataFullExtendedDto> _ownCharaData = [];
    private readonly PlayerDataFactory _playerDataFactory;
    private readonly Dictionary<string, Task> _sharedMetaInfoTimeoutTasks = [];
    private readonly Dictionary<UserData, List<CharaDataMetaInfoDto>> _sharedWithYouData = [];
    private readonly Dictionary<string, CharaDataExtendedUpdateDto> _updateDtos = [];
    private CancellationTokenSource _applicationCts = new();
    private CancellationTokenSource _charaDataCreateCts = new();
    private CancellationTokenSource _getAllDataCts = new();
    private CancellationTokenSource _getSharedDataCts = new();
    private int _globalFileCounter = 0;
    private CancellationTokenSource _uploadCts = new();
    private CancellationTokenSource _connectCts = new();

    public CharaDataManager(ILogger<CharaDataManager> logger, ApiController apiController,
        FileUploadManager fileUploadManager, FileCacheManager fileCacheManager,
        MareMediator mareMediator, IpcManager ipcManager, GameObjectHandlerFactory gameObjectHandlerFactory,
        DalamudUtilService dalamudUtilService, FileDownloadManagerFactory fileDownloadManagerFactory,
        PlayerDataFactory playerDataFactory, CharaDataConfigService charaDataConfigService,
        VfxSpawnManager vfxSpawnManager) : base(logger, mareMediator)
    {
        _fileDownloadManager = fileDownloadManagerFactory.Create();
        _apiController = apiController;
        _fileUploadManager = fileUploadManager;
        _mareCharaFileDataFactory = new(fileCacheManager);
        _fileCacheManager = fileCacheManager;
        _ipcManager = ipcManager;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _dalamudUtilService = dalamudUtilService;
        _playerDataFactory = playerDataFactory;
        _charaDataConfigService = charaDataConfigService;
        _vfxSpawnManager = vfxSpawnManager;
        mareMediator.Subscribe<ConnectedMessage>(this, (msg) =>
        {
            _ownCharaData.Clear();
            _metaInfoCache.Clear();
            _sharedWithYouData.Clear();
            _updateDtos.Clear();
            Initialized = false;
            MaxCreatableCharaData = string.IsNullOrEmpty(msg.Connection.User.Alias)
                ? msg.Connection.ServerInfo.MaxCharaData
                : msg.Connection.ServerInfo.MaxCharaDataVanity;
            if (_charaDataConfigService.Current.DownloadMcdDataOnConnection)
            {
                var token = _connectCts.Token;
                _ = GetAllData(token);
                _ = GetAllSharedData(token);
            }
        });

        mareMediator.Subscribe<CutsceneFrameworkUpdateMessage>(this, (_) => HandleCutsceneFrameworkUpdate());

        mareMediator.Subscribe<GposeEndMessage>(this, (_) =>
        {
            foreach (var chara in _handledCharaData)
            {
                RevertChara(chara.Name);
            }
        });

        mareMediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => HandleFrameworkUpdate());
    }

    public Task? AppearanceTask { get; private set; }
    public Task? CharaUpdateTask { get; set; }
    public string DataApplicationProgress { get; private set; } = string.Empty;
    public Task? DataApplicationTask { get; private set; }
    public Task<(string Output, bool Success)>? DataCreationTask { get; private set; }
    public Task? DataGetTimeoutTask { get; private set; }
    public Task<(string Result, bool Success)>? DownloadMetaInfoTask { get; private set; }
    public Task<List<CharaDataFullExtendedDto>>? GetAllDataTask { get; private set; }
    public Task<List<CharaDataMetaInfoDto>>? GetSharedWithYouTask { get; private set; }
    public Task? GetSharedWithYouTimeoutTask { get; private set; }
    public IEnumerable<HandledCharaDataEntry> HandledCharaData => _handledCharaData;
    public bool Initialized { get; private set; }
    public bool IsExportingMcdf { get; private set; }
    public CharaDataMetaInfoDto? LastDownloadedMetaInfo { get; private set; }
    public MareCharaFileHeader? LoadedCharaFile { get; private set; }
    public int MaxCreatableCharaData { get; private set; }
    public Task? McdfApplicationTask { get; private set; }
    public Task<long>? McdfHeaderLoadingTask { get; private set; }
    public IDictionary<string, CharaDataFullExtendedDto> OwnCharaData => _ownCharaData;
    public IDictionary<UserData, List<CharaDataMetaInfoDto>> SharedWithYouData => _sharedWithYouData;
    public Task? UiBlockingComputation { get; private set; }
    public ValueProgress<string>? UploadProgress { get; private set; }
    public Task<(string Output, bool Success)>? UploadTask { get; private set; }

    public void McdfApplyToGposeTarget()
    {
        if (LoadedCharaFile == null || McdfHeaderLoadingTask == null || !McdfHeaderLoadingTask.IsCompletedSuccessfully) return;
        var charaName = _dalamudUtilService.GposeTargetGameObject?.Name.TextValue ?? string.Empty;

        List<string> actuallyExtractedFiles = [];
        UiBlockingComputation = McdfApplicationTask = Task.Run(async () =>
        {
            Guid applicationId = Guid.NewGuid();
            try
            {
                using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Player,
                    () => _dalamudUtilService.GetGposeCharacterFromObjectTableByName(charaName, true)?.Address ?? IntPtr.Zero, false).ConfigureAwait(false);
                var playerChar = await _dalamudUtilService.GetPlayerCharacterAsync().ConfigureAwait(false);
                bool isSelf = playerChar != null && string.Equals(playerChar.Name.TextValue, tempHandler.Name, StringComparison.Ordinal);

                long expectedExtractedSize = McdfHeaderLoadingTask?.Result ?? 0;

                DataApplicationProgress = "Extracting MCDF data";

                var extractedFiles = McdfExtractFiles(LoadedCharaFile, expectedExtractedSize, actuallyExtractedFiles);
                foreach (var entry in LoadedCharaFile.CharaFileData.FileSwaps.SelectMany(k => k.GamePaths, (k, p) => new KeyValuePair<string, string>(p, k.FileSwapPath)))
                {
                    extractedFiles[entry.Key] = entry.Value;
                }

                DataApplicationProgress = "Applying MCDF data";

                await ApplyDataAsync(applicationId, tempHandler, isSelf, autoRevert: false, LoadedCharaFile.FilePath,
                    extractedFiles, LoadedCharaFile.CharaFileData.ManipulationData, LoadedCharaFile.CharaFileData.GlamourerData,
                    LoadedCharaFile.CharaFileData.CustomizePlusData, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to extract MCDF");
                throw;
            }
            finally
            {
                // delete extracted files
                foreach (var file in actuallyExtractedFiles)
                {
                    File.Delete(file);
                }
            }
        });
    }

    private Task? _spawnApplicationTask;

    public Task SpawnAndApplyOtherDataToGposeTarget(CharaDataMetaInfoDto charaDataMetaInfoDto)
    {
        return _spawnApplicationTask = Task.Run(async () =>
        {
            var newActor = await _ipcManager.Brio.SpawnActorAsync().ConfigureAwait(false);
            if (newActor == null) return;
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            unsafe
            {
                _dalamudUtilService.GposeTarget = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)newActor.Address;
            }

            await ApplyOtherDataToGposeTarget(charaDataMetaInfoDto).ConfigureAwait(false);
        });
    }

    public Task ApplyOtherDataToGposeTarget(CharaDataMetaInfoDto dataMetaInfoDto)
    {
        return UiBlockingComputation = DataApplicationTask = Task.Run(async () =>
        {
            var charaName = await _dalamudUtilService.RunOnFrameworkThread(() => _dalamudUtilService.GposeTargetGameObject?.Name.TextValue).ConfigureAwait(false)
                ?? string.Empty;
            if (string.IsNullOrEmpty(charaName)) return;

            var download = await _apiController.CharaDataDownload(dataMetaInfoDto.Uploader.UID + ":" + dataMetaInfoDto.Id).ConfigureAwait(false);
            if (download == null)
            {
                DataApplicationTask = null;
                return;
            }

            await DownloadAndAplyDataAsync(charaName, download, false).ConfigureAwait(false);
        });
    }

    public void ApplyOwnDataToGposeTarget(CharaDataFullExtendedDto dataDto)
    {
        var charaName = _dalamudUtilService.GposeTargetGameObject?.Name.TextValue ?? string.Empty;
        CharaDataDownloadDto downloadDto = new(dataDto.Id, dataDto.Uploader)
        {
            CustomizeData = dataDto.CustomizeData,
            Description = dataDto.Description,
            FileGamePaths = dataDto.FileGamePaths,
            GlamourerData = dataDto.GlamourerData,
            FileSwaps = dataDto.FileSwaps,
            ManipulationData = dataDto.ManipulationData,
            UpdatedDate = dataDto.UpdatedDate
        };

        UiBlockingComputation = DataApplicationTask = DownloadAndAplyDataAsync(charaName, downloadDto, false);
    }

    public bool CanApplyInGpose(out string targetName)
    {
        bool canApply = _dalamudUtilService.IsInGpose && _dalamudUtilService.GposeTargetGameObject != null
            && _dalamudUtilService.GposeTargetGameObject.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player;
        if (canApply)
        {
            targetName = _dalamudUtilService.GposeTargetGameObject!.Name.TextValue;
        }
        else
        {
            targetName = "Invalid Target";
        }
        return canApply;
    }

    public void CancelDataApplication()
    {
        _applicationCts.Cancel();
    }

    public void CancelUpload()
    {
        _uploadCts.Cancel();
    }

    public void CreateCharaData(CancellationToken cancelToken)
    {
        UiBlockingComputation = DataCreationTask = Task.Run(async () =>
        {
            var result = await _apiController.CharaDataCreate().ConfigureAwait(false);
            _ = Task.Run(async () =>
            {
                _charaDataCreateCts = _charaDataCreateCts.CancelRecreate();
                using var ct = CancellationTokenSource.CreateLinkedTokenSource(_charaDataCreateCts.Token, cancelToken);
                await Task.Delay(TimeSpan.FromSeconds(10), ct.Token).ConfigureAwait(false);
                DataCreationTask = null;
            });


            if (result == null)
                return ("Failed to create character data, see log for more information", false);

            AddOrUpdateDto(result);

            return ("Created Character Data", true);
        });
    }

    public async Task DeleteCharaData(string id)
    {
        var ret = await _apiController.CharaDataDelete(id).ConfigureAwait(false);
        if (ret)
        {
            _ownCharaData.Remove(id);
        }
    }

    public void DownloadMetaInfo(string importCode, bool store = true)
    {
        DownloadMetaInfoTask = Task.Run(async () =>
        {
            try
            {
                if (store)
                {
                    LastDownloadedMetaInfo = null;
                }
                var metaInfo = await _apiController.CharaDataGetMetainfo(importCode).ConfigureAwait(false);
                _sharedMetaInfoTimeoutTasks[importCode] = Task.Delay(TimeSpan.FromSeconds(10));
                if (metaInfo == null)
                {
                    _metaInfoCache[importCode] = null;
                    return ("Failed to download meta info for this code. Check if the code is valid and you have rights to access it.", false);
                }
                _metaInfoCache[metaInfo.Uploader.UID + ":" + metaInfo.Id] = metaInfo;
                if (store)
                {
                    LastDownloadedMetaInfo = metaInfo;
                }
                return ("Ok", true);
            }
            finally
            {
                if (!store)
                    DownloadMetaInfoTask = null;
            }
        });
    }

    public async Task GetAllData(CancellationToken cancelToken)
    {
        foreach (var data in _ownCharaData)
        {
            _metaInfoCache.Remove(data.Key);
        }
        _ownCharaData.Clear();
        UiBlockingComputation = GetAllDataTask = Task.Run(async () =>
        {
            _getAllDataCts = _getAllDataCts.CancelRecreate();
            var result = await _apiController.CharaDataGetOwn().ConfigureAwait(false);

            Initialized = true;

            if (result.Any())
            {
                DataGetTimeoutTask = Task.Run(async () =>
                {
                    using var ct = CancellationTokenSource.CreateLinkedTokenSource(_getAllDataCts.Token, cancelToken);
#if !DEBUG
                    await Task.Delay(TimeSpan.FromMinutes(1), ct.Token).ConfigureAwait(false);
#else
                    await Task.Delay(TimeSpan.FromSeconds(5), ct.Token).ConfigureAwait(false);
#endif
                });
            }

            return result.OrderBy(u => u.CreatedDate).Select(k => new CharaDataFullExtendedDto(k)).ToList();
        });

        var result = await GetAllDataTask.ConfigureAwait(false);
        foreach (var item in result)
        {
            AddOrUpdateDto(item);
        }

        foreach (var id in _updateDtos.Keys.Where(r => !result.Exists(res => string.Equals(res.Id, r, StringComparison.Ordinal))).ToList())
        {
            _updateDtos.Remove(id);
        }
        GetAllDataTask = null;
    }

    public async Task GetAllSharedData(CancellationToken token)
    {
        Logger.LogDebug("Getting Shared with You Data");

        UiBlockingComputation = GetSharedWithYouTask = _apiController.CharaDataGetShared();
        _sharedWithYouData.Clear();

        GetSharedWithYouTimeoutTask = Task.Run(async () =>
        {
            _getSharedDataCts = _getSharedDataCts.CancelRecreate();
            using var ct = CancellationTokenSource.CreateLinkedTokenSource(_getSharedDataCts.Token, token);
#if !DEBUG
            await Task.Delay(TimeSpan.FromMinutes(1), ct.Token).ConfigureAwait(false);
#else
            await Task.Delay(TimeSpan.FromSeconds(5), ct.Token).ConfigureAwait(false);
#endif
            GetSharedWithYouTimeoutTask = null;
            Logger.LogDebug("Finished Shared with You Data Timeout");
        });

        var result = await GetSharedWithYouTask.ConfigureAwait(false);
        foreach (var item in result.GroupBy(r => r.Uploader))
        {
            _sharedWithYouData[item.Key] = [.. item];
        }

        Logger.LogDebug("Finished getting Shared with You Data");
        GetSharedWithYouTask = null;
    }

    public CharaDataExtendedUpdateDto? GetUpdateDto(string id)
    {
        if (_updateDtos.TryGetValue(id, out var dto))
            return dto;
        return null;
    }

    public bool IsInTimeout(string key)
    {
        if (!_sharedMetaInfoTimeoutTasks.TryGetValue(key, out var task)) return false;
        return !task?.IsCompleted ?? false;
    }

    public void LoadMcdf(string filePath)
    {
        McdfHeaderLoadingTask = Task.Run(() =>
        {
            try
            {
                using var unwrapped = File.OpenRead(filePath);
                using var lz4Stream = new LZ4Stream(unwrapped, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
                using var reader = new BinaryReader(lz4Stream);
                LoadedCharaFile = MareCharaFileHeader.FromBinaryReader(filePath, reader);

                Logger.LogInformation("Read Mare Chara File");
                Logger.LogInformation("Version: {ver}", (LoadedCharaFile?.Version ?? -1));
                long expectedLength = 0;
                if (LoadedCharaFile != null)
                {
                    Logger.LogTrace("Data");
                    foreach (var item in LoadedCharaFile.CharaFileData.FileSwaps)
                    {
                        foreach (var gamePath in item.GamePaths)
                        {
                            Logger.LogTrace("Swap: {gamePath} => {fileSwapPath}", gamePath, item.FileSwapPath);
                        }
                    }

                    var itemNr = 0;
                    foreach (var item in LoadedCharaFile.CharaFileData.Files)
                    {
                        itemNr++;
                        expectedLength += item.Length;
                        foreach (var gamePath in item.GamePaths)
                        {
                            Logger.LogTrace("File {itemNr}: {gamePath} = {len}", itemNr, gamePath, item.Length.ToByteString());
                        }
                    }

                    Logger.LogInformation("Expected length: {expected}", expectedLength.ToByteString());
                }
                return expectedLength;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Could not parse MCDF header of file {file}", filePath);
                return 0;
            }
        });
    }

    public void RevertChara(string name)
    {
        var handled = _handledCharaData.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
        if (handled == null) return;
        _handledCharaData.Remove(handled);
        _ = _dalamudUtilService.RunOnFrameworkThread(() => RevertChara(handled.Name, handled.CustomizePlus));
    }

    public async Task SaveMareCharaFile(string description, string filePath)
    {
        IsExportingMcdf = true;
        var tempFilePath = filePath + ".tmp";

        try
        {
            var data = await CreatePlayerData().ConfigureAwait(false);
            if (data == null) return;

            var mareCharaFileData = _mareCharaFileDataFactory.Create(description, data);
            MareCharaFileHeader output = new(MareCharaFileHeader.CurrentVersion, mareCharaFileData);

            using var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var lz4 = new LZ4Stream(fs, LZ4StreamMode.Compress, LZ4StreamFlags.HighCompression);
            using var writer = new BinaryWriter(lz4);
            output.WriteToStream(writer);

            foreach (var item in output.CharaFileData.Files)
            {
                var file = _fileCacheManager.GetFileCacheByHash(item.Hash)!;
                Logger.LogDebug("Saving to MCDF: {hash}:{file}", item.Hash, file.ResolvedFilepath);
                Logger.LogDebug("\tAssociated GamePaths:");
                foreach (var path in item.GamePaths)
                {
                    Logger.LogDebug("\t{path}", path);
                }
                using var fsRead = File.OpenRead(file.ResolvedFilepath);
                using var br = new BinaryReader(fsRead);
                byte[] buffer = new byte[item.Length];
                br.Read(buffer, 0, item.Length);
                writer.Write(buffer);
            }
            writer.Flush();
            await lz4.FlushAsync().ConfigureAwait(false);
            await fs.FlushAsync().ConfigureAwait(false);
            fs.Close();
            File.Move(tempFilePath, filePath, true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failure Saving Mare Chara File, deleting output");
            File.Delete(tempFilePath);
        }
        finally { IsExportingMcdf = false; }
    }

    public void SetAppearanceData(string dtoId)
    {
        var hasDto = _ownCharaData.TryGetValue(dtoId, out var dto);
        if (!hasDto || dto == null) return;

        var hasUpdateDto = _updateDtos.TryGetValue(dtoId, out var updateDto);
        if (!hasUpdateDto || updateDto == null) return;

        UiBlockingComputation = AppearanceTask = Task.Run(async () =>
        {
            var data = await CreatePlayerData().ConfigureAwait(false);

            if (data != null)
            {
                var hasGlamourerData = data.GlamourerData.TryGetValue(ObjectKind.Player, out var playerDataString);
                if (!hasGlamourerData) updateDto.GlamourerData = null;
                else updateDto.GlamourerData = playerDataString;

                var hasCustomizeData = data.CustomizePlusData.TryGetValue(ObjectKind.Player, out var customizeDataString);
                if (!hasCustomizeData) updateDto.CustomizeData = null;
                else updateDto.CustomizeData = customizeDataString;

                updateDto.ManipulationData = data.ManipulationData;

                var hasFiles = data.FileReplacements.TryGetValue(ObjectKind.Player, out var fileReplacements);
                if (!hasFiles)
                {
                    updateDto.FileGamePaths = [];
                    updateDto.FileSwaps = [];
                }
                else
                {
                    updateDto.FileGamePaths = [.. fileReplacements!.Where(u => string.IsNullOrEmpty(u.FileSwapPath)).SelectMany(u => u.GamePaths, (file, path) => new GamePathEntry(file.Hash, path))];
                    updateDto.FileSwaps = [.. fileReplacements!.Where(u => !string.IsNullOrEmpty(u.FileSwapPath)).SelectMany(u => u.GamePaths, (file, path) => new GamePathEntry(file.FileSwapPath, path))];
                }
            }

            AppearanceTask = null;
        });
    }

    public bool TryGetMetaInfo(string key, out CharaDataMetaInfoDto? metaInfo)
    {
        if (!_metaInfoCache.TryGetValue(key, out metaInfo!))
        {
            var splitKey = key.Split(":");
            if (_ownCharaData.TryGetValue(splitKey[1], out var ownCharaData))
            {
                _metaInfoCache[key] = metaInfo = new(ownCharaData.Id, ownCharaData.Uploader)
                {
                    Description = ownCharaData.Description,
                    UpdatedDate = ownCharaData.UpdatedDate,
                    CanBeDownloaded = !string.IsNullOrEmpty(ownCharaData.GlamourerData) && (ownCharaData.OriginalFiles.Count == ownCharaData.FileGamePaths.Count),
                    PoseData = ownCharaData.PoseData,
                };
                return true;
            }
            var isShared = _sharedWithYouData.SelectMany(v => v.Value)
                .FirstOrDefault(f => string.Equals(f.Uploader.UID, splitKey[0], StringComparison.Ordinal) && string.Equals(f.Id, splitKey[1], StringComparison.Ordinal));
            if (isShared != null)
            {
                _metaInfoCache[key] = metaInfo = new(isShared.Id, isShared.Uploader)
                {
                    Description = isShared.Description,
                    UpdatedDate = isShared.UpdatedDate,
                    CanBeDownloaded = isShared.CanBeDownloaded,
                    PoseData = isShared.PoseData,
                };
                return true;
            }
        }
        else
        {
            return true;
        }

        return false;
    }

    public void UploadCharaData(string id)
    {
        var hasUpdateDto = _updateDtos.TryGetValue(id, out var updateDto);
        if (!hasUpdateDto || updateDto == null) return;

        UiBlockingComputation = CharaUpdateTask = CharaUpdateAsync(updateDto);
    }

    public void UploadMissingFiles(string id)
    {
        var hasDto = _ownCharaData.TryGetValue(id, out var dto);
        if (!hasDto || dto == null) return;

        var missingFileList = dto.MissingFiles.ToList();
        UiBlockingComputation = UploadTask = UploadFiles(missingFileList, async () =>
        {
            var newFilePaths = dto.FileGamePaths;
            foreach (var missing in missingFileList)
            {
                newFilePaths.Add(missing);
            }
            CharaDataUpdateDto updateDto = new(dto.Id)
            {
                FileGamePaths = newFilePaths
            };
            var res = await _apiController.CharaDataUpdate(updateDto).ConfigureAwait(false);
            AddOrUpdateDto(res);
        });
    }

    internal void ApplyDataToSelf(CharaDataFullExtendedDto dataDto)
    {
        var chara = _dalamudUtilService.GetPlayerName();
        CharaDataDownloadDto downloadDto = new(dataDto.Id, dataDto.Uploader)
        {
            CustomizeData = dataDto.CustomizeData,
            Description = dataDto.Description,
            FileGamePaths = dataDto.FileGamePaths,
            GlamourerData = dataDto.GlamourerData,
            FileSwaps = dataDto.FileSwaps,
            ManipulationData = dataDto.ManipulationData,
            UpdatedDate = dataDto.UpdatedDate
        };
        UiBlockingComputation = DataApplicationTask = DownloadAndAplyDataAsync(chara, downloadDto);
    }

    internal unsafe void TargetGposeActor(HandledCharaDataEntry actor)
    {
        var gposeActor = _dalamudUtilService.GetGposeCharacterFromObjectTableByName(actor.Name, true);
        if (gposeActor != null)
        {
            _dalamudUtilService.GposeTarget = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)gposeActor.Address;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _getAllDataCts?.Cancel();
            _getAllDataCts?.Dispose();
            _getSharedDataCts?.Cancel();
            _getSharedDataCts?.Dispose();
            _charaDataCreateCts?.Cancel();
            _charaDataCreateCts?.Dispose();
            _uploadCts?.Cancel();
            _uploadCts?.Dispose();
            _applicationCts.Cancel();
            _applicationCts.Dispose();
            _fileDownloadManager.Dispose();
        }
    }

    private void AddOrUpdateDto(CharaDataFullDto? dto)
    {
        if (dto == null) return;

        _ownCharaData[dto.Id] = new(dto);
        _updateDtos[dto.Id] = new(new(dto.Id), _ownCharaData[dto.Id]);
    }

    private async Task ApplyDataAsync(Guid applicationId, GameObjectHandler tempHandler, bool isSelf, bool autoRevert,
        string dataId, Dictionary<string, string> modPaths, string? manipData, string? glamourerData, string? customizeData, CancellationToken token)
    {
        Guid? cPlusId = null;
        Guid penumbraCollection;
        try
        {
            var handled = _handledCharaData.FirstOrDefault(f => string.Equals(f.Name, tempHandler.Name, StringComparison.OrdinalIgnoreCase));
            if (handled != null)
            {
                DataApplicationProgress = "Reverting previous Application";

                Logger.LogTrace("[{appId}] Reverting chara {chara}", applicationId, tempHandler.Name);
                await RevertChara(handled.Name, handled.CustomizePlus).ConfigureAwait(false);
                _handledCharaData.Remove(handled);
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }

            Logger.LogTrace("[{appId}] Applying data in Penumbra", applicationId);

            DataApplicationProgress = "Applying Penumbra information";
            penumbraCollection = await _ipcManager.Penumbra.CreateTemporaryCollectionAsync(Logger, dataId).ConfigureAwait(false);
            var idx = await _dalamudUtilService.RunOnFrameworkThread(() => tempHandler.GetGameObject()?.ObjectIndex).ConfigureAwait(false) ?? 0;
            await _ipcManager.Penumbra.AssignTemporaryCollectionAsync(Logger, penumbraCollection, idx).ConfigureAwait(false);
            await _ipcManager.Penumbra.SetTemporaryModsAsync(Logger, applicationId, penumbraCollection, modPaths).ConfigureAwait(false);
            await _ipcManager.Penumbra.SetManipulationDataAsync(Logger, applicationId, penumbraCollection, manipData ?? string.Empty).ConfigureAwait(false);

            Logger.LogTrace("[{appId}] Applying Glamourer data and Redrawing", applicationId);
            DataApplicationProgress = "Applying Glamourer and redrawing Character";
            await _ipcManager.Glamourer.ApplyAllAsync(Logger, tempHandler, glamourerData, applicationId, token).ConfigureAwait(false);
            await _ipcManager.Penumbra.RedrawAsync(Logger, tempHandler, applicationId, token).ConfigureAwait(false);
            await _dalamudUtilService.WaitWhileCharacterIsDrawing(Logger, tempHandler, applicationId, ct: token).ConfigureAwait(false);
            Logger.LogTrace("[{appId}] Removing collection", applicationId);
            await _ipcManager.Penumbra.RemoveTemporaryCollectionAsync(Logger, applicationId, penumbraCollection).ConfigureAwait(false);

            DataApplicationProgress = "Applying Customize+ data";
            Logger.LogTrace("[{appId}] Appplying C+ data", applicationId);

            if (!string.IsNullOrEmpty(customizeData))
            {
                cPlusId = await _ipcManager.CustomizePlus.SetBodyScaleAsync(tempHandler.Address, customizeData).ConfigureAwait(false);
            }
            else
            {
                cPlusId = await _ipcManager.CustomizePlus.SetBodyScaleAsync(tempHandler.Address, Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"))).ConfigureAwait(false);
            }

            if (autoRevert)
            {
                Logger.LogTrace("[{appId}] Starting wait for auto revert", applicationId);

                int i = 15;
                while (i > 0)
                {
                    DataApplicationProgress = $"All data applied. Reverting automatically in {i} seconds.";
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                    i--;
                }
            }
            else
            {
                Logger.LogTrace("[{appId}] Adding {name} to handled objects", applicationId, tempHandler.Name);

                _handledCharaData.Add(new(tempHandler.Name, isSelf, cPlusId, dataId));
            }
        }
        finally
        {
            if (token.IsCancellationRequested)
                DataApplicationProgress = "Application aborted. Reverting Character...";
            else if (autoRevert)
                DataApplicationProgress = "Application finished. Reverting Character...";
            if (autoRevert)
            {
                await RevertChara(tempHandler.Name, cPlusId).ConfigureAwait(false);
            }

            if (!_dalamudUtilService.IsInGpose)
                Mediator.Publish(new HaltCharaDataCreation(Resume: true));

            if (_charaDataConfigService.Current.FavoriteCodes.TryGetValue(dataId, out var favorite) && favorite != null)
            {
                favorite.LastDownloaded = DateTime.UtcNow;
                _charaDataConfigService.Save();
            }

            DataApplicationTask = null;
            DataApplicationProgress = string.Empty;
        }
    }

    private async Task CharaUpdateAsync(CharaDataExtendedUpdateDto updateDto)
    {
        Logger.LogDebug("Uploading Chara Data to Server");
        var baseUpdateDto = updateDto.BaseDto;
        if (baseUpdateDto.FileGamePaths != null)
        {
            Logger.LogDebug("Detected file path changes, starting file upload");

            UploadTask = UploadFiles(baseUpdateDto.FileGamePaths);
            var result = await UploadTask.ConfigureAwait(false);
            if (!result.Success)
            {
                return;
            }
        }

        Logger.LogDebug("Pushing update dto to server: {data}", baseUpdateDto);

        var res = await _apiController.CharaDataUpdate(baseUpdateDto).ConfigureAwait(false);
        AddOrUpdateDto(res);
        CharaUpdateTask = null;
    }

    private async Task<CharacterData?> CreatePlayerData()
    {
        var chara = await _dalamudUtilService.GetPlayerCharacterAsync().ConfigureAwait(false);
        if (_dalamudUtilService.IsInGpose)
        {
            chara = (IPlayerCharacter?)(await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(chara.Name.TextValue, _dalamudUtilService.IsInGpose).ConfigureAwait(false));
        }

        if (chara == null)
            return null;

        using var tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Player,
                        () => _dalamudUtilService.GetCharacterFromObjectTableByIndex(chara.ObjectIndex)?.Address ?? IntPtr.Zero, isWatched: false).ConfigureAwait(false);
        PlayerData.Data.CharacterData newCdata = new();
        await _playerDataFactory.BuildCharacterData(newCdata, tempHandler, CancellationToken.None).ConfigureAwait(false);
        if (newCdata.FileReplacements.TryGetValue(ObjectKind.Player, out var playerData) && playerData != null)
        {
            foreach (var data in playerData.Select(g => g.GamePaths))
            {
                data.RemoveWhere(g => g.EndsWith(".pap", StringComparison.OrdinalIgnoreCase)
                    || g.EndsWith(".tmb", StringComparison.OrdinalIgnoreCase)
                    || g.EndsWith(".scd", StringComparison.OrdinalIgnoreCase)
                    || (g.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase)
                        && !g.Contains("/weapon/", StringComparison.OrdinalIgnoreCase)
                        && !g.Contains("/equipment/", StringComparison.OrdinalIgnoreCase))
                    || (g.EndsWith(".atex", StringComparison.OrdinalIgnoreCase)
                        && !g.Contains("/weapon/", StringComparison.OrdinalIgnoreCase)
                        && !g.Contains("/equipment/", StringComparison.OrdinalIgnoreCase)));
            }

            playerData.RemoveWhere(g => g.GamePaths.Count == 0);
        }

        return newCdata.ToAPI();
    }

    private async Task DownloadAndAplyDataAsync(string charaName, CharaDataDownloadDto charaDataDownloadDto, bool autoRevert = true)
    {
        _applicationCts = _applicationCts.CancelRecreate();
        var token = _applicationCts.Token;
        ICharacter? chara = (await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(charaName, _dalamudUtilService.IsInGpose).ConfigureAwait(false));

        if (chara == null)
            return;

        var applicationId = Guid.NewGuid();

        var playerChar = await _dalamudUtilService.GetPlayerCharacterAsync().ConfigureAwait(false);
        bool isSelf = playerChar != null && string.Equals(playerChar.Name.TextValue, chara.Name.TextValue, StringComparison.Ordinal);

        DataApplicationProgress = "Checking local files";

        Logger.LogTrace("[{appId}] Computing local missing files", applicationId);

        Dictionary<string, string> modPaths = [];
        List<FileReplacementData> missingFiles = [];
        foreach (var file in charaDataDownloadDto.FileGamePaths)
        {
            var localCacheFile = _fileCacheManager.GetFileCacheByHash(file.HashOrFileSwap);
            if (localCacheFile == null)
            {
                var existingFile = missingFiles.Find(f => string.Equals(f.Hash, file.HashOrFileSwap, StringComparison.Ordinal));
                if (existingFile == null)
                {
                    missingFiles.Add(new FileReplacementData()
                    {
                        Hash = file.HashOrFileSwap,
                        GamePaths = [file.GamePath]
                    });
                }
                else
                {
                    existingFile.GamePaths = existingFile.GamePaths.Concat([file.GamePath]).ToArray();
                }
            }
            else
            {
                modPaths[file.GamePath] = localCacheFile.ResolvedFilepath;
            }
        }

        foreach (var swap in charaDataDownloadDto.FileSwaps)
        {
            modPaths[swap.GamePath] = swap.HashOrFileSwap;
        }

        Logger.LogTrace("[{appId}] Computing local missing files", applicationId);

        using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Player,
            () => _dalamudUtilService.GetCharacterFromObjectTableByIndex(chara.ObjectIndex)?.Address ?? IntPtr.Zero, isWatched: false).ConfigureAwait(false);

        if (missingFiles.Any())
        {
            try
            {
                DataApplicationProgress = "Downloading Missing Files. Please be patient.";
                await _fileDownloadManager.InitiateDownloadList(tempHandler, missingFiles, token).ConfigureAwait(false);
                await _fileDownloadManager.DownloadFiles(tempHandler, missingFiles, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                foreach (var file in missingFiles.SelectMany(m => m.GamePaths, (FileEntry, GamePath) => (FileEntry.Hash, GamePath)))
                {
                    var localFile = _fileCacheManager.GetFileCacheByHash(file.Hash)?.ResolvedFilepath;
                    if (localFile == null)
                    {
                        DataApplicationProgress = "Failed to download one or more files. Aborting.";
                        DataApplicationTask = null;
                        return;
                    }
                    modPaths[file.GamePath] = localFile;
                }
            }
            catch (OperationCanceledException)
            {
                DataApplicationProgress = "Application aborted.";
                DataApplicationTask = null;
                return;
            }
        }

        if (!_dalamudUtilService.IsInGpose)
            Mediator.Publish(new HaltCharaDataCreation());

        string dataId = charaDataDownloadDto.Uploader.UID + ":" + charaDataDownloadDto.Id;
        await ApplyDataAsync(applicationId, tempHandler, isSelf, autoRevert, dataId, modPaths, charaDataDownloadDto.ManipulationData, charaDataDownloadDto.GlamourerData,
            charaDataDownloadDto.CustomizeData, token).ConfigureAwait(false);
    }

    private Dictionary<string, string> McdfExtractFiles(MareCharaFileHeader charaFileHeader, long expectedLength, List<string> extractedFiles)
    {
        if (LoadedCharaFile == null) return [];

        using var lz4Stream = new LZ4Stream(File.OpenRead(LoadedCharaFile.FilePath), LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
        using var reader = new BinaryReader(lz4Stream);
        MareCharaFileHeader.AdvanceReaderToData(reader);

        long totalRead = 0;
        Dictionary<string, string> gamePathToFilePath = new(StringComparer.Ordinal);
        foreach (var fileData in charaFileHeader.CharaFileData.Files)
        {
            var fileName = Path.Combine(_fileCacheManager.CacheFolder, "mare_" + _globalFileCounter++ + ".tmp");
            extractedFiles.Add(fileName);
            var length = fileData.Length;
            var bufferSize = length;
            using var fs = File.OpenWrite(fileName);
            using var wr = new BinaryWriter(fs);
            Logger.LogTrace("Reading {length} of {fileName}", length.ToByteString(), fileName);
            var buffer = reader.ReadBytes(bufferSize);
            wr.Write(buffer);
            wr.Flush();
            wr.Close();
            if (buffer.Length == 0) throw new EndOfStreamException("Unexpected EOF");
            foreach (var path in fileData.GamePaths)
            {
                gamePathToFilePath[path] = fileName;
                Logger.LogTrace("{path} => {fileName} [{hash}]", path, fileName, fileData.Hash);
            }
            totalRead += length;
            Logger.LogTrace("Read {read}/{expected} bytes", totalRead.ToByteString(), expectedLength.ToByteString());
        }

        return gamePathToFilePath;
    }

    private void HandleFrameworkUpdate()
    {
        var previousPoses = _nearbyData.Values.SelectMany(k => k).Select(k => k.Id!.Value).ToList();
        _nearbyData.Clear();

        var map = _dalamudUtilService.GetMapData();
        var pos = _dalamudUtilService.GetPlayerCharacter().Position;
        var data = _sharedWithYouData.SelectMany(v => v.Value)
            .SelectMany(v => v.PoseData, (MetaInfo, PoseData) => (MetaInfo, PoseData))
            .Where(p => p.PoseData.WorldData != null && p.PoseData.WorldData != default(WorldData)
                && p.PoseData.WorldData.Value.LocationInfo.MapId == map.MapId && p.PoseData.WorldData.Value.LocationInfo.TerritoryId == map.TerritoryId)
            .ToList();

        foreach (var entry in data)
        {
            var dist = Vector3.Distance(pos, new Vector3(entry.PoseData.WorldData.Value.PositionX, entry.PoseData.WorldData.Value.PositionY, entry.PoseData.WorldData.Value.PositionZ));
            Logger.LogDebug("Distance from player to data {data} is {dist}", entry.MetaInfo.Id, dist);
            if (dist < 50)
            {
                if (!_nearbyData.TryGetValue(entry.MetaInfo, out var poseList))
                {
                    _nearbyData[entry.MetaInfo] = [entry.PoseData];

                }
                else
                {
                    poseList.Add(entry.PoseData);
                }
            }
        }

        foreach (var prevPose in previousPoses.Except(_nearbyData.Values.SelectMany(k => k).Select(k => k.Id!.Value)))
        {
            if (_poseVfx.TryGetValue(prevPose, out Guid vfx))
            {
                _vfxSpawnManager.DespawnObject(vfx);
                _poseVfx.Remove(prevPose);
            }
        }

        foreach (var newPoseList in _nearbyData)
        {
            foreach (var pose in newPoseList.Value)
            {
                if (!_poseVfx.TryGetValue(pose.Id!.Value, out Guid vfx))
                {
                    var guid = _vfxSpawnManager.SpawnObject(new Vector3(pose.WorldData.Value.PositionX, pose.WorldData.Value.PositionY, pose.WorldData.Value.PositionZ),
                        new Quaternion(pose.WorldData.Value.RotationX, pose.WorldData.Value.RotationY, pose.WorldData.Value.RotationZ, pose.WorldData.Value.RotationW));
                    if (guid != null)
                    {
                        _poseVfx[pose.Id!.Value] = guid.Value;
                    }
                }
            }
        }
    }

    private Dictionary<long, Guid> _poseVfx = [];

    private readonly Dictionary<CharaDataMetaInfoDto, List<PoseEntry>> _nearbyData = [];
    public IDictionary<CharaDataMetaInfoDto, List<PoseEntry>> NearbyData => _nearbyData;

    private void HandleCutsceneFrameworkUpdate()
    {
        if (!_dalamudUtilService.IsInGpose) return;

        foreach (var entry in _handledCharaData.ToList())
        {
            var chara = _dalamudUtilService.GetGposeCharacterFromObjectTableByName(entry.Name, onlyGposeCharacters: true);
            if (chara is null)
            {
                RevertChara(entry.Name, entry.CustomizePlus).GetAwaiter().GetResult();
                _handledCharaData.Remove(entry);
            }
        }
    }
    private async Task RevertChara(string name, Guid? cPlusId)
    {
        Guid applicationId = Guid.NewGuid();
        await _ipcManager.Glamourer.RevertByNameAsync(Logger, name, applicationId).ConfigureAwait(false);
        if (cPlusId != null)
        {
            await _ipcManager.CustomizePlus.RevertByIdAsync(cPlusId).ConfigureAwait(false);
        }
        using var handler = await _gameObjectHandlerFactory.Create(ObjectKind.Player,
            () => _dalamudUtilService.GetGposeCharacterFromObjectTableByName(name, _dalamudUtilService.IsInGpose)?.Address ?? IntPtr.Zero, false)
            .ConfigureAwait(false);
        if (handler.Address != IntPtr.Zero)
        {
            await _ipcManager.Penumbra.RedrawAsync(Logger, handler, applicationId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<(string Result, bool Success)> UploadFiles(List<GamePathEntry> missingFileList, Func<Task>? postUpload = null)
    {
        UploadProgress = new ValueProgress<string>();
        try
        {
            _uploadCts = _uploadCts.CancelRecreate();
            var missingFiles = await _fileUploadManager.UploadFiles([.. missingFileList.Select(k => k.HashOrFileSwap)], UploadProgress, _uploadCts.Token).ConfigureAwait(false);
            if (missingFiles.Any())
            {
                Logger.LogInformation("Failed to upload {files}", string.Join(", ", missingFiles));
                return ($"Upload failed: {missingFiles.Count} missing or forbidden to upload local files.", false);
            }

            if (postUpload != null)
                await postUpload.Invoke().ConfigureAwait(false);

            return ("Upload sucessful", true);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during upload");
            if (ex is OperationCanceledException)
            {
                return ("Upload Cancelled", false);
            }
            return ("Error in upload, see log for more details", false);
        }
        finally
        {
            UploadTask = null;
            UploadProgress = null;
        }
    }

    internal void McdfSpawnApplyToGposeTarget()
    {
        _ = Task.Run(async () =>
        {
            var newActor = await _ipcManager.Brio.SpawnActorAsync().ConfigureAwait(false);
            if (newActor == null) return;
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            unsafe
            {
                _dalamudUtilService.GposeTarget = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)newActor.Address;
            }

            McdfApplyToGposeTarget();
        });
    }

    public Task? AttachingPoseTask { get; private set; }

    internal void AttachPoseData(PoseEntry pose, CharaDataExtendedUpdateDto updateDto)
    {
        AttachingPoseTask = Task.Run(async () =>
        {
            ICharacter? playerChar = await _dalamudUtilService.GetPlayerCharacterAsync().ConfigureAwait(false);
            if (playerChar == null) return;
            if (_dalamudUtilService.IsInGpose)
            {
                playerChar = await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(playerChar.Name.TextValue, true).ConfigureAwait(false);
            }
            if (playerChar == null) return;
            var poseData = await _ipcManager.Brio.GetPoseAsync(playerChar.Address).ConfigureAwait(false);
            if (poseData == null) return;

            var compressedByteData = LZ4Codec.WrapHC(Encoding.UTF8.GetBytes(poseData));
            pose.PoseData = Convert.ToBase64String(compressedByteData);
            updateDto.UpdatePoseList();
        });
    }

    public Task ApplyPoseData(PoseEntry pose)
    {
        string chara = string.Empty;
        if (string.IsNullOrEmpty(pose.PoseData) || !CanApplyInGpose(out chara)) return Task.CompletedTask;
        return Task.Run(async () =>
        {
            var gposeChara = await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(chara, true).ConfigureAwait(false);
            if (gposeChara == null) return;

            var poseJson = Encoding.UTF8.GetString(LZ4Codec.Unwrap(Convert.FromBase64String(pose.PoseData)));
            if (string.IsNullOrEmpty(poseJson)) return;

            await _ipcManager.Brio.SetPoseAsync(gposeChara.Address, poseJson).ConfigureAwait(false);
        });
    }

    public void AttachWorldData(PoseEntry pose, CharaDataExtendedUpdateDto updateDto)
    {
        AttachingPoseTask = Task.Run(async () =>
        {
            ICharacter? playerChar = await _dalamudUtilService.GetPlayerCharacterAsync().ConfigureAwait(false);
            if (playerChar == null) return;
            if (_dalamudUtilService.IsInGpose)
            {
                playerChar = await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(playerChar.Name.TextValue, true).ConfigureAwait(false);
            }
            if (playerChar == null) return;
            var worldData = await _ipcManager.Brio.GetTransformAsync(playerChar.Address).ConfigureAwait(false);
            if (worldData == default) return;

            Logger.LogTrace("Attaching World data {data}", worldData);

            worldData.LocationInfo = await _dalamudUtilService.GetMapDataAsync().ConfigureAwait(false);

            Logger.LogTrace("World data serialized: {data}", worldData);

            pose.WorldData = worldData;

            updateDto.UpdatePoseList();
        });
    }

    public Task ApplyWorldData(PoseEntry pose)
    {
        string chara = string.Empty;
        if (pose.WorldData == default || !CanApplyInGpose(out chara)) return Task.CompletedTask;
        return Task.Run(async () =>
        {
            var gposeChara = await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(chara, true).ConfigureAwait(false);
            if (gposeChara == null) return;

            if (pose.WorldData == null || pose.WorldData == default) return;

            Logger.LogDebug("Applying World data {data}", pose.WorldData);

            await _ipcManager.Brio.ApplyTransformAsync(gposeChara.Address, pose.WorldData.Value).ConfigureAwait(false);
        });
    }

    internal void SpawnAndApplyWorldTransform(CharaDataMetaInfoDto metaInfo, PoseEntry value)
    {
        _ = Task.Run(async () =>
        {
            await SpawnAndApplyOtherDataToGposeTarget(metaInfo).ConfigureAwait(false);
            await ApplyPoseData(value).ConfigureAwait(false);
            await ApplyWorldData(value).ConfigureAwait(false);
        });
    }
}
