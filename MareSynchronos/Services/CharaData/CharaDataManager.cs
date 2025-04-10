using Dalamud.Game.ClientState.Objects.Types;
using K4os.Compression.LZ4.Legacy;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.CharaData.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;

namespace MareSynchronos.Services;

public sealed partial class CharaDataManager : DisposableMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly CharaDataConfigService _configService;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly CharaDataFileHandler _fileHandler;
    private readonly IpcManager _ipcManager;
    private readonly ConcurrentDictionary<string, CharaDataMetaInfoExtendedDto?> _metaInfoCache = [];
    private readonly List<CharaDataMetaInfoExtendedDto> _nearbyData = [];
    private readonly CharaDataNearbyManager _nearbyManager;
    private readonly CharaDataCharacterHandler _characterHandler;
    private readonly PairManager _pairManager;
    private readonly Dictionary<string, CharaDataFullExtendedDto> _ownCharaData = [];
    private readonly Dictionary<string, Task> _sharedMetaInfoTimeoutTasks = [];
    private readonly Dictionary<UserData, List<CharaDataMetaInfoExtendedDto>> _sharedWithYouData = [];
    private readonly Dictionary<string, CharaDataExtendedUpdateDto> _updateDtos = [];
    private CancellationTokenSource _applicationCts = new();
    private CancellationTokenSource _charaDataCreateCts = new();
    private CancellationTokenSource _connectCts = new();
    private CancellationTokenSource _getAllDataCts = new();
    private CancellationTokenSource _getSharedDataCts = new();
    private CancellationTokenSource _uploadCts = new();

    public CharaDataManager(ILogger<CharaDataManager> logger, ApiController apiController,
        CharaDataFileHandler charaDataFileHandler,
        MareMediator mareMediator, IpcManager ipcManager, DalamudUtilService dalamudUtilService,
        FileDownloadManagerFactory fileDownloadManagerFactory,
        CharaDataConfigService charaDataConfigService, CharaDataNearbyManager charaDataNearbyManager,
        CharaDataCharacterHandler charaDataCharacterHandler, PairManager pairManager) : base(logger, mareMediator)
    {
        _apiController = apiController;
        _fileHandler = charaDataFileHandler;
        _ipcManager = ipcManager;
        _dalamudUtilService = dalamudUtilService;
        _configService = charaDataConfigService;
        _nearbyManager = charaDataNearbyManager;
        _characterHandler = charaDataCharacterHandler;
        _pairManager = pairManager;
        mareMediator.Subscribe<ConnectedMessage>(this, (msg) =>
        {
            _connectCts?.Cancel();
            _connectCts?.Dispose();
            _connectCts = new();
            _ownCharaData.Clear();
            _metaInfoCache.Clear();
            _sharedWithYouData.Clear();
            _updateDtos.Clear();
            Initialized = false;
            MaxCreatableCharaData = string.IsNullOrEmpty(msg.Connection.User.Alias)
                ? msg.Connection.ServerInfo.MaxCharaData
                : msg.Connection.ServerInfo.MaxCharaDataVanity;
            if (_configService.Current.DownloadMcdDataOnConnection)
            {
                var token = _connectCts.Token;
                _ = GetAllData(token);
                _ = GetAllSharedData(token);
            }
        });
        mareMediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            _ownCharaData.Clear();
            _metaInfoCache.Clear();
            _sharedWithYouData.Clear();
            _updateDtos.Clear();
            Initialized = false;
        });
    }

    public Task? AttachingPoseTask { get; private set; }
    public Task? CharaUpdateTask { get; set; }
    public string DataApplicationProgress { get; private set; } = string.Empty;
    public Task? DataApplicationTask { get; private set; }
    public Task<(string Output, bool Success)>? DataCreationTask { get; private set; }
    public Task? DataGetTimeoutTask { get; private set; }
    public Task<(string Result, bool Success)>? DownloadMetaInfoTask { get; private set; }
    public Task<List<CharaDataFullExtendedDto>>? GetAllDataTask { get; private set; }
    public Task<List<CharaDataMetaInfoDto>>? GetSharedWithYouTask { get; private set; }
    public Task? GetSharedWithYouTimeoutTask { get; private set; }
    public IEnumerable<HandledCharaDataEntry> HandledCharaData => _characterHandler.HandledCharaData;
    public bool Initialized { get; private set; }
    public CharaDataMetaInfoExtendedDto? LastDownloadedMetaInfo { get; private set; }
    public Task<(MareCharaFileHeader LoadedFile, long ExpectedLength)>? LoadedMcdfHeader { get; private set; }
    public int MaxCreatableCharaData { get; private set; }
    public Task? McdfApplicationTask { get; private set; }
    public List<CharaDataMetaInfoExtendedDto> NearbyData => _nearbyData;
    public IDictionary<string, CharaDataFullExtendedDto> OwnCharaData => _ownCharaData;
    public IDictionary<UserData, List<CharaDataMetaInfoExtendedDto>> SharedWithYouData => _sharedWithYouData;
    public Task? UiBlockingComputation { get; private set; }
    public ValueProgress<string>? UploadProgress { get; private set; }
    public Task<(string Output, bool Success)>? UploadTask { get; set; }
    public bool BrioAvailable => _ipcManager.Brio.APIAvailable;

    public Task ApplyCharaData(CharaDataDownloadDto dataDownloadDto, string charaName)
    {
        return UiBlockingComputation = DataApplicationTask = Task.Run(async () =>
        {
            if (string.IsNullOrEmpty(charaName)) return;

            CharaDataMetaInfoDto metaInfo = new(dataDownloadDto.Id, dataDownloadDto.Uploader)
            {
                CanBeDownloaded = true,
                Description = $"Data from {dataDownloadDto.Uploader.AliasOrUID} for {dataDownloadDto.Id}",
                UpdatedDate = dataDownloadDto.UpdatedDate,
            };

            await DownloadAndAplyDataAsync(charaName, dataDownloadDto, metaInfo, false).ConfigureAwait(false);
        });
    }

    public Task ApplyCharaData(CharaDataMetaInfoDto dataMetaInfoDto, string charaName)
    {
        return UiBlockingComputation = DataApplicationTask = Task.Run(async () =>
        {
            if (string.IsNullOrEmpty(charaName)) return;

            var download = await _apiController.CharaDataDownload(dataMetaInfoDto.Uploader.UID + ":" + dataMetaInfoDto.Id).ConfigureAwait(false);
            if (download == null)
            {
                DataApplicationTask = null;
                return;
            }

            await DownloadAndAplyDataAsync(charaName, download, dataMetaInfoDto, false).ConfigureAwait(false);
        });
    }

    public Task ApplyCharaDataToGposeTarget(CharaDataMetaInfoDto dataMetaInfoDto)
    {
        return UiBlockingComputation = DataApplicationTask = Task.Run(async () =>
        {
            var obj = await _dalamudUtilService.GetGposeTargetGameObjectAsync().ConfigureAwait(false);
            var charaName = obj?.Name.TextValue ?? string.Empty;
            if (string.IsNullOrEmpty(charaName)) return;

            await ApplyCharaData(dataMetaInfoDto, charaName).ConfigureAwait(false);
        });
    }

    public async Task ApplyOwnDataToGposeTarget(CharaDataFullExtendedDto dataDto)
    {
        var chara = await _dalamudUtilService.GetGposeTargetGameObjectAsync().ConfigureAwait(false);
        var charaName = chara?.Name.TextValue ?? string.Empty;
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

        CharaDataMetaInfoDto metaInfoDto = new(dataDto.Id, dataDto.Uploader)
        {
            CanBeDownloaded = true,
            Description = dataDto.Description,
            PoseData = dataDto.PoseData,
            UpdatedDate = dataDto.UpdatedDate,
        };

        UiBlockingComputation = DataApplicationTask = DownloadAndAplyDataAsync(charaName, downloadDto, metaInfoDto, false);
    }

    public Task ApplyPoseData(PoseEntry pose, string targetName)
    {
        return UiBlockingComputation = Task.Run(async () =>
        {
            if (string.IsNullOrEmpty(pose.PoseData) || !(await CanApplyInGpose().ConfigureAwait(false)).CanApply) return;
            var gposeChara = await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(targetName, true).ConfigureAwait(false);
            if (gposeChara == null) return;

            var poseJson = Encoding.UTF8.GetString(LZ4Wrapper.Unwrap(Convert.FromBase64String(pose.PoseData)));
            if (string.IsNullOrEmpty(poseJson)) return;

            await _ipcManager.Brio.SetPoseAsync(gposeChara.Address, poseJson).ConfigureAwait(false);
        });
    }

    public Task ApplyPoseDataToGPoseTarget(PoseEntry pose)
    {
        return UiBlockingComputation = Task.Run(async () =>
        {
            var apply = await CanApplyInGpose().ConfigureAwait(false);

            if (apply.CanApply)
            {
                await ApplyPoseData(pose, apply.TargetName).ConfigureAwait(false);
            }
        });
    }

    public Task ApplyWorldDataToTarget(PoseEntry pose, string targetName)
    {
        return UiBlockingComputation = Task.Run(async () =>
        {
            var apply = await CanApplyInGpose().ConfigureAwait(false);
            if (pose.WorldData == default || !(await CanApplyInGpose().ConfigureAwait(false)).CanApply) return;
            var gposeChara = await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(targetName, true).ConfigureAwait(false);
            if (gposeChara == null) return;

            if (pose.WorldData == null || pose.WorldData == default) return;

            Logger.LogDebug("Applying World data {data}", pose.WorldData);

            await _ipcManager.Brio.ApplyTransformAsync(gposeChara.Address, pose.WorldData.Value).ConfigureAwait(false);
        });
    }

    public Task ApplyWorldDataToGPoseTarget(PoseEntry pose)
    {
        return UiBlockingComputation = Task.Run(async () =>
        {
            var apply = await CanApplyInGpose().ConfigureAwait(false);
            if (apply.CanApply)
            {
                await ApplyPoseData(pose, apply.TargetName).ConfigureAwait(false);
            }
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

    public async Task<(bool CanApply, string TargetName)> CanApplyInGpose()
    {
        var obj = await _dalamudUtilService.GetGposeTargetGameObjectAsync().ConfigureAwait(false);
        string targetName = string.Empty;
        bool canApply = _dalamudUtilService.IsInGpose && obj != null
            && obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player;
        if (canApply)
        {
            targetName = obj!.Name.TextValue;
        }
        else
        {
            targetName = "Invalid Target";
        }
        return (canApply, targetName);
    }

    public void CancelDataApplication()
    {
        _applicationCts.Cancel();
    }

    public void CancelUpload()
    {
        _uploadCts.Cancel();
    }

    public void CreateCharaDataEntry(CancellationToken cancelToken)
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

            await AddOrUpdateDto(result).ConfigureAwait(false);

            return ("Created Character Data", true);
        });
    }

    public async Task DeleteCharaData(CharaDataFullExtendedDto dto)
    {
        var ret = await _apiController.CharaDataDelete(dto.Id).ConfigureAwait(false);
        if (ret)
        {
            _ownCharaData.Remove(dto.Id);
            _metaInfoCache.Remove(dto.FullId, out _);
        }
        DistributeMetaInfo();
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
                await CacheData(metaInfo).ConfigureAwait(false);
                if (store)
                {
                    LastDownloadedMetaInfo = await CharaDataMetaInfoExtendedDto.Create(metaInfo, _dalamudUtilService).ConfigureAwait(false);
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
            _metaInfoCache.Remove(data.Key, out _);
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
            await AddOrUpdateDto(item).ConfigureAwait(false);
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
        foreach (var grouping in result.GroupBy(r => r.Uploader))
        {
            var pair = _pairManager.GetPairByUID(grouping.Key.UID);
            if (pair?.IsPaused ?? false) continue;
            List<CharaDataMetaInfoExtendedDto> newList = new();
            foreach (var item in grouping)
            {
                var extended = await CharaDataMetaInfoExtendedDto.Create(item, _dalamudUtilService).ConfigureAwait(false);
                newList.Add(extended);
                CacheData(extended);
            }
            _sharedWithYouData[grouping.Key] = newList;
        }

        DistributeMetaInfo();

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
        LoadedMcdfHeader = _fileHandler.LoadCharaFileHeader(filePath);
    }

    public void McdfApplyToTarget(string charaName)
    {
        if (LoadedMcdfHeader == null || !LoadedMcdfHeader.IsCompletedSuccessfully) return;

        List<string> actuallyExtractedFiles = [];

        UiBlockingComputation = McdfApplicationTask = Task.Run(async () =>
        {
            Guid applicationId = Guid.NewGuid();
            try
            {
                using GameObjectHandler? tempHandler = await _characterHandler.TryCreateGameObjectHandler(charaName, true).ConfigureAwait(false);
                if (tempHandler == null) return;
                var playerChar = await _dalamudUtilService.GetPlayerCharacterAsync().ConfigureAwait(false);
                bool isSelf = playerChar != null && string.Equals(playerChar.Name.TextValue, tempHandler.Name, StringComparison.Ordinal);

                long expectedExtractedSize = LoadedMcdfHeader.Result.ExpectedLength;
                var charaFile = LoadedMcdfHeader.Result.LoadedFile;
                DataApplicationProgress = "Extracting MCDF data";

                var extractedFiles = _fileHandler.McdfExtractFiles(charaFile, expectedExtractedSize, actuallyExtractedFiles);

                foreach (var entry in charaFile.CharaFileData.FileSwaps.SelectMany(k => k.GamePaths, (k, p) => new KeyValuePair<string, string>(p, k.FileSwapPath)))
                {
                    extractedFiles[entry.Key] = entry.Value;
                }

                DataApplicationProgress = "Applying MCDF data";

                var extended = await CharaDataMetaInfoExtendedDto.Create(new(charaFile.FilePath, new UserData(string.Empty)), _dalamudUtilService)
                    .ConfigureAwait(false);
                await ApplyDataAsync(applicationId, tempHandler, isSelf, autoRevert: false, extended,
                    extractedFiles, charaFile.CharaFileData.ManipulationData, charaFile.CharaFileData.GlamourerData,
                    charaFile.CharaFileData.CustomizePlusData, CancellationToken.None).ConfigureAwait(false);
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

    public async Task McdfApplyToGposeTarget()
    {
        var apply = await CanApplyInGpose().ConfigureAwait(false);
        if (apply.CanApply)
        {
            McdfApplyToTarget(apply.TargetName);
        }
    }

    public void SaveMareCharaFile(string description, string filePath)
    {
        UiBlockingComputation = Task.Run(async () => await _fileHandler.SaveCharaFileAsync(description, filePath).ConfigureAwait(false));
    }

    public void SetAppearanceData(string dtoId)
    {
        var hasDto = _ownCharaData.TryGetValue(dtoId, out var dto);
        if (!hasDto || dto == null) return;

        var hasUpdateDto = _updateDtos.TryGetValue(dtoId, out var updateDto);
        if (!hasUpdateDto || updateDto == null) return;

        UiBlockingComputation = Task.Run(async () =>
        {
            await _fileHandler.UpdateCharaDataAsync(updateDto).ConfigureAwait(false);
        });
    }

    public Task<HandledCharaDataEntry?> SpawnAndApplyData(CharaDataDownloadDto charaDataDownloadDto)
    {
        var task = Task.Run(async () =>
        {
            var newActor = await _ipcManager.Brio.SpawnActorAsync().ConfigureAwait(false);
            if (newActor == null) return null;
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

            await ApplyCharaData(charaDataDownloadDto, newActor.Name.TextValue).ConfigureAwait(false);

            return _characterHandler.HandledCharaData.FirstOrDefault(f => string.Equals(f.Name, newActor.Name.TextValue, StringComparison.Ordinal));
        });
        UiBlockingComputation = task;
        return task;
    }

    public Task<HandledCharaDataEntry?> SpawnAndApplyData(CharaDataMetaInfoDto charaDataMetaInfoDto)
    {
        var task = Task.Run(async () =>
        {
            var newActor = await _ipcManager.Brio.SpawnActorAsync().ConfigureAwait(false);
            if (newActor == null) return null;
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

            await ApplyCharaData(charaDataMetaInfoDto, newActor.Name.TextValue).ConfigureAwait(false);

            return _characterHandler.HandledCharaData.FirstOrDefault(f => string.Equals(f.Name, newActor.Name.TextValue, StringComparison.Ordinal));
        });
        UiBlockingComputation = task;
        return task;
    }

    private async Task<CharaDataMetaInfoExtendedDto> CacheData(CharaDataFullExtendedDto ownCharaData)
    {
        var metaInfo = new CharaDataMetaInfoDto(ownCharaData.Id, ownCharaData.Uploader)
        {
            Description = ownCharaData.Description,
            UpdatedDate = ownCharaData.UpdatedDate,
            CanBeDownloaded = !string.IsNullOrEmpty(ownCharaData.GlamourerData) && (ownCharaData.OriginalFiles.Count == ownCharaData.FileGamePaths.Count),
            PoseData = ownCharaData.PoseData,
        };

        var extended = await CharaDataMetaInfoExtendedDto.Create(metaInfo, _dalamudUtilService, isOwnData: true).ConfigureAwait(false);
        _metaInfoCache[extended.FullId] = extended;
        DistributeMetaInfo();

        return extended;
    }

    private async Task<CharaDataMetaInfoExtendedDto> CacheData(CharaDataMetaInfoDto metaInfo, bool isOwnData = false)
    {
        var extended = await CharaDataMetaInfoExtendedDto.Create(metaInfo, _dalamudUtilService, isOwnData).ConfigureAwait(false);
        _metaInfoCache[extended.FullId] = extended;
        DistributeMetaInfo();

        return extended;
    }

    private readonly SemaphoreSlim _distributionSemaphore = new(1, 1);

    private void DistributeMetaInfo()
    {
        _distributionSemaphore.Wait();
        _nearbyManager.UpdateSharedData(_metaInfoCache.ToDictionary());
        _characterHandler.UpdateHandledData(_metaInfoCache.ToDictionary());
        _distributionSemaphore.Release();
    }

    private void CacheData(CharaDataMetaInfoExtendedDto charaData)
    {
        _metaInfoCache[charaData.FullId] = charaData;
    }

    public bool TryGetMetaInfo(string key, out CharaDataMetaInfoExtendedDto? metaInfo)
    {
        return _metaInfoCache.TryGetValue(key, out metaInfo);
    }

    public void UploadAllCharaData()
    {
        UiBlockingComputation = Task.Run(async () =>
        {
            foreach (var updateDto in _updateDtos.Values.Where(u => u.HasChanges))
            {
                CharaUpdateTask = CharaUpdateAsync(updateDto);
                await CharaUpdateTask.ConfigureAwait(false);
            }
        });
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

        UiBlockingComputation = UploadTask = RestoreThenUpload(dto);
    }

    private async Task<(string Output, bool Success)> RestoreThenUpload(CharaDataFullExtendedDto dto)
    {
        var newDto = await _apiController.CharaDataAttemptRestore(dto.Id).ConfigureAwait(false);
        if (newDto == null)
        {
            _ownCharaData.Remove(dto.Id);
            _metaInfoCache.Remove(dto.FullId, out _);
            UiBlockingComputation = null;
            return ("No such DTO found", false);
        }

        await AddOrUpdateDto(newDto).ConfigureAwait(false);
        _ = _ownCharaData.TryGetValue(dto.Id, out var extendedDto);

        if (!extendedDto!.HasMissingFiles)
        {
            UiBlockingComputation = null;
            return ("Restored successfully", true);
        }

        var missingFileList = extendedDto!.MissingFiles.ToList();
        var result = await UploadFiles(missingFileList, async () =>
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
            await AddOrUpdateDto(res).ConfigureAwait(false);
        }).ConfigureAwait(false);

        UiBlockingComputation = null;
        return result;
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

        CharaDataMetaInfoDto metaInfoDto = new(dataDto.Id, dataDto.Uploader)
        {
            CanBeDownloaded = true,
            Description = dataDto.Description,
            PoseData = dataDto.PoseData,
            UpdatedDate = dataDto.UpdatedDate,
        };

        UiBlockingComputation = DataApplicationTask = DownloadAndAplyDataAsync(chara, downloadDto, metaInfoDto);
    }

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

            var compressedByteData = LZ4Wrapper.WrapHC(Encoding.UTF8.GetBytes(poseData));
            pose.PoseData = Convert.ToBase64String(compressedByteData);
            updateDto.UpdatePoseList();
        });
    }

    internal void McdfSpawnApplyToGposeTarget()
    {
        UiBlockingComputation = Task.Run(async () =>
        {
            var newActor = await _ipcManager.Brio.SpawnActorAsync().ConfigureAwait(false);
            if (newActor == null) return;
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            unsafe
            {
                _dalamudUtilService.GposeTarget = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)newActor.Address;
            }

            await McdfApplyToGposeTarget().ConfigureAwait(false);
        });
    }

    internal void ApplyFullPoseDataToTarget(PoseEntry value, string targetName)
    {
        UiBlockingComputation = Task.Run(async () =>
        {
            await ApplyPoseData(value, targetName).ConfigureAwait(false);
            await ApplyWorldDataToTarget(value, targetName).ConfigureAwait(false);
        });
    }

    internal void ApplyFullPoseDataToGposeTarget(PoseEntry value)
    {
        UiBlockingComputation = Task.Run(async () =>
        {
            var apply = await CanApplyInGpose().ConfigureAwait(false);
            if (apply.CanApply)
            {
                await ApplyPoseData(value, apply.TargetName).ConfigureAwait(false);
                await ApplyWorldDataToTarget(value, apply.TargetName).ConfigureAwait(false);
            }
        });
    }

    internal void SpawnAndApplyWorldTransform(CharaDataMetaInfoDto metaInfo, PoseEntry value)
    {
        UiBlockingComputation = Task.Run(async () =>
        {
            var actor = await SpawnAndApplyData(metaInfo).ConfigureAwait(false);
            if (actor == null) return;
            await ApplyPoseData(value, actor.Name).ConfigureAwait(false);
            await ApplyWorldDataToTarget(value, actor.Name).ConfigureAwait(false);
        });
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
            _connectCts?.Cancel();
            _connectCts?.Dispose();
        }
    }

    private async Task AddOrUpdateDto(CharaDataFullDto? dto)
    {
        if (dto == null) return;

        _ownCharaData[dto.Id] = new(dto);
        _updateDtos[dto.Id] = new(new(dto.Id), _ownCharaData[dto.Id]);

        await CacheData(_ownCharaData[dto.Id]).ConfigureAwait(false);
    }

    private async Task ApplyDataAsync(Guid applicationId, GameObjectHandler tempHandler, bool isSelf, bool autoRevert,
        CharaDataMetaInfoExtendedDto metaInfo, Dictionary<string, string> modPaths, string? manipData, string? glamourerData, string? customizeData, CancellationToken token)
    {
        Guid? cPlusId = null;
        Guid penumbraCollection;
        try
        {
            DataApplicationProgress = "Reverting previous Application";

            Logger.LogTrace("[{appId}] Reverting chara {chara}", applicationId, tempHandler.Name);
            bool reverted = await _characterHandler.RevertHandledChara(tempHandler.Name).ConfigureAwait(false);
            if (reverted)
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

            Logger.LogTrace("[{appId}] Applying data in Penumbra", applicationId);

            DataApplicationProgress = "Applying Penumbra information";
            penumbraCollection = await _ipcManager.Penumbra.CreateTemporaryCollectionAsync(Logger, metaInfo.Uploader.UID + metaInfo.Id).ConfigureAwait(false);
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

                _characterHandler.AddHandledChara(new HandledCharaDataEntry(tempHandler.Name, isSelf, cPlusId, metaInfo));
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
                await _characterHandler.RevertChara(tempHandler.Name, cPlusId).ConfigureAwait(false);
            }

            if (!_dalamudUtilService.IsInGpose)
                Mediator.Publish(new HaltCharaDataCreation(Resume: true));

            if (metaInfo != null && _configService.Current.FavoriteCodes.TryGetValue(metaInfo.Uploader.UID + ":" + metaInfo.Id, out var favorite) && favorite != null)
            {
                favorite.LastDownloaded = DateTime.UtcNow;
                _configService.Save();
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
        await AddOrUpdateDto(res).ConfigureAwait(false);
    }

    private async Task DownloadAndAplyDataAsync(string charaName, CharaDataDownloadDto charaDataDownloadDto, CharaDataMetaInfoDto metaInfo, bool autoRevert = true)
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

        Dictionary<string, string> modPaths;
        List<FileReplacementData> missingFiles;
        _fileHandler.ComputeMissingFiles(charaDataDownloadDto, out modPaths, out missingFiles);

        Logger.LogTrace("[{appId}] Computing local missing files", applicationId);

        using GameObjectHandler? tempHandler = await _characterHandler.TryCreateGameObjectHandler(chara.ObjectIndex).ConfigureAwait(false);
        if (tempHandler == null) return;

        if (missingFiles.Any())
        {
            try
            {
                DataApplicationProgress = "Downloading Missing Files. Please be patient.";
                await _fileHandler.DownloadFilesAsync(tempHandler, missingFiles, modPaths, token).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                DataApplicationProgress = "Failed to download one or more files. Aborting.";
                DataApplicationTask = null;
                return;
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

        var extendedMetaInfo = await CacheData(metaInfo).ConfigureAwait(false);

        await ApplyDataAsync(applicationId, tempHandler, isSelf, autoRevert, extendedMetaInfo, modPaths, charaDataDownloadDto.ManipulationData, charaDataDownloadDto.GlamourerData,
            charaDataDownloadDto.CustomizeData, token).ConfigureAwait(false);
    }

    public async Task<(string Result, bool Success)> UploadFiles(List<GamePathEntry> missingFileList, Func<Task>? postUpload = null)
    {
        UploadProgress = new ValueProgress<string>();
        try
        {
            _uploadCts = _uploadCts.CancelRecreate();
            var missingFiles = await _fileHandler.UploadFiles([.. missingFileList.Select(k => k.HashOrFileSwap)], UploadProgress, _uploadCts.Token).ConfigureAwait(false);
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
            UiBlockingComputation = null;
        }
    }

    public void RevertChara(HandledCharaDataEntry? handled)
    {
        UiBlockingComputation = _characterHandler.RevertHandledChara(handled);
    }

    internal void RemoveChara(string handledActor)
    {
        if (string.IsNullOrEmpty(handledActor)) return;
        UiBlockingComputation = Task.Run(async () =>
        {
            await _characterHandler.RevertHandledChara(handledActor).ConfigureAwait(false);
            var gposeChara = await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(handledActor, true).ConfigureAwait(false);
            if (gposeChara != null)
                await _ipcManager.Brio.DespawnActorAsync(gposeChara.Address).ConfigureAwait(false);
        });
    }
}
