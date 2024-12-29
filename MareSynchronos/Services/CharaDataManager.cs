using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using LZ4;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.PlayerData.Export;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Text;

namespace MareSynchronos.Services;

internal sealed class CharaDataManager : DisposableMediatorSubscriberBase
{
    public record CharaDataFullExtendedDto : CharaDataFullDto
    {
        public CharaDataFullExtendedDto(CharaDataFullDto baseDto) : base(baseDto)
        {
            MissingFiles = new ReadOnlyCollection<GamePathEntry>(baseDto.OriginalFiles.Except(baseDto.FileGamePaths).ToList());
            HasMissingFiles = MissingFiles.Any();
        }

        public bool HasMissingFiles { get; init; }
        public IReadOnlyCollection<GamePathEntry> MissingFiles { get; init; }
    }

    public record CharaDataExtendedUpdateDto : CharaDataUpdateDto
    {
        private readonly CharaDataFullDto _charaDataFullDto;

        public CharaDataExtendedUpdateDto(CharaDataUpdateDto dto, CharaDataFullDto charaDataFullDto) : base(dto)
        {
            _charaDataFullDto = charaDataFullDto;
            _userList = charaDataFullDto.AllowedUsers.ToList();
        }

        public CharaDataUpdateDto BaseDto => new(Id)
        {
            AllowedUsers = AllowedUsers,
            AccessType = base.AccessType,
            CustomizeData = base.CustomizeData,
            Description = base.Description,
            ExpiryDate = base.ExpiryDate,
            FileGamePaths = base.FileGamePaths,
            FileSwaps = base.FileSwaps,
            GlamourerData = base.GlamourerData,
            ShareType = base.ShareType,
            ManipulationData = base.ManipulationData
        };

        public new string ManipulationData
        {
            get
            {
                return base.ManipulationData ?? _charaDataFullDto.ManipulationData;
            }
            set
            {
                base.ManipulationData = value;
                if (string.Equals(base.ManipulationData, _charaDataFullDto.ManipulationData, StringComparison.Ordinal))
                {
                    base.ManipulationData = null;
                }
            }
        }

        public new string Description
        {
            get
            {
                return base.Description ?? _charaDataFullDto.Description;
            }
            set
            {
                base.Description = value;
                if (string.Equals(base.Description, _charaDataFullDto.Description, StringComparison.Ordinal))
                {
                    base.Description = null;
                }
            }
        }

        public new DateTime ExpiryDate
        {
            get
            {
                return base.ExpiryDate ?? _charaDataFullDto.ExpiryDate;
            }
            private set
            {
                base.ExpiryDate = value;
                if (Equals(base.ExpiryDate, _charaDataFullDto.ExpiryDate))
                {
                    base.ExpiryDate = null;
                }
            }
        }

        public new AccessTypeDto AccessType
        {
            get
            {
                return base.AccessType ?? _charaDataFullDto.AccessType;
            }
            set
            {
                base.AccessType = value;
                if (AccessType == AccessTypeDto.Public && ShareType == ShareTypeDto.Shared)
                {
                    ShareType = ShareTypeDto.Private;
                }

                if (Equals(base.AccessType, _charaDataFullDto.AccessType))
                {
                    base.AccessType = null;
                }
            }
        }

        public new ShareTypeDto ShareType
        {
            get
            {
                return base.ShareType ?? _charaDataFullDto.ShareType;
            }
            set
            {
                base.ShareType = value;
                if (ShareType == ShareTypeDto.Shared && AccessType == AccessTypeDto.Public)
                {
                    base.ShareType = ShareTypeDto.Private;
                }

                if (Equals(base.ShareType, _charaDataFullDto.ShareType))
                {
                    base.ShareType = null;
                }
            }
        }

        public new List<GamePathEntry>? FileGamePaths
        {
            get
            {
                return base.FileGamePaths ?? _charaDataFullDto.FileGamePaths;
            }
            set
            {
                base.FileGamePaths = value;
                if (!(base.FileGamePaths ?? []).Except(_charaDataFullDto.FileGamePaths).Any()
                    && !_charaDataFullDto.FileGamePaths.Except(base.FileGamePaths ?? []).Any())
                {
                    base.FileGamePaths = null;
                }
            }
        }

        public new List<GamePathEntry>? FileSwaps
        {
            get
            {
                return base.FileSwaps ?? _charaDataFullDto.FileSwaps;
            }
            set
            {
                base.FileSwaps = value;
                if (!(base.FileSwaps ?? []).Except(_charaDataFullDto.FileSwaps).Any()
                    && !_charaDataFullDto.FileSwaps.Except(base.FileSwaps ?? []).Any())
                {
                    base.FileSwaps = null;
                }
            }
        }

        public new string? GlamourerData
        {
            get
            {
                return base.GlamourerData ?? _charaDataFullDto.GlamourerData;
            }
            set
            {
                base.GlamourerData = value;
                if (string.Equals(base.GlamourerData, _charaDataFullDto.GlamourerData, StringComparison.Ordinal))
                {
                    base.GlamourerData = null;
                }
            }
        }

        public new string? CustomizeData
        {
            get
            {
                return base.CustomizeData ?? _charaDataFullDto.CustomizeData;
            }
            set
            {
                base.CustomizeData = value;
                if (string.Equals(base.CustomizeData, _charaDataFullDto.CustomizeData, StringComparison.Ordinal))
                {
                    base.CustomizeData = null;
                }
            }
        }

        public IEnumerable<UserData> UserList => _userList;
        private readonly List<UserData> _userList;

        public void AddToList(string user)
        {
            _userList.Add(new(user, null));
            UpdateAllowedUsers();
        }

        private void UpdateAllowedUsers()
        {
            AllowedUsers = [.. _userList.Select(u => u.UID)];
            if (!AllowedUsers.Except(_charaDataFullDto.AllowedUsers.Select(u => u.UID), StringComparer.Ordinal).Any()
                && !_charaDataFullDto.AllowedUsers.Select(u => u.UID).Except(AllowedUsers, StringComparer.Ordinal).Any())
            {
                AllowedUsers = null;
            }
        }

        public void RemoveFromList(string user)
        {
            _userList.RemoveAll(u => string.Equals(u.UID, user, StringComparison.Ordinal));
            UpdateAllowedUsers();
        }

        public void SetExpiry(bool expiring)
        {
            if (expiring)
            {
                var date = DateTime.UtcNow.AddDays(7);
                SetExpiry(date.Year, date.Month, date.Day);
            }
            else
            {
                ExpiryDate = DateTime.MaxValue;
            }
        }

        public void SetExpiry(int year, int month, int day)
        {
            int daysInMonth = DateTime.DaysInMonth(year, month);
            if (day > daysInMonth) day = 1;
            ExpiryDate = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
        }

        internal void UndoChanges()
        {
            base.Description = null;
            base.AccessType = null;
            base.ShareType = null;
            base.GlamourerData = null;
            base.FileSwaps = null;
            base.FileGamePaths = null;
            base.CustomizeData = null;
            base.ManipulationData = null;
            AllowedUsers = null;
        }

        public bool HasChanges =>
                    base.Description != null
                    || base.ExpiryDate != null
                    || base.AccessType != null
                    || base.ShareType != null
                    || AllowedUsers != null
                    || base.GlamourerData != null
                    || base.FileSwaps != null
                    || base.FileGamePaths != null
                    || base.CustomizeData != null
                    || base.ManipulationData != null;

        public bool IsAppearanceEqual =>
            string.Equals(GlamourerData, _charaDataFullDto.GlamourerData, StringComparison.Ordinal)
            && string.Equals(CustomizeData, _charaDataFullDto.CustomizeData, StringComparison.Ordinal)
            && FileGamePaths == _charaDataFullDto.FileGamePaths
            && FileSwaps == _charaDataFullDto.FileSwaps
            && string.Equals(ManipulationData, _charaDataFullDto.ManipulationData, StringComparison.Ordinal);
    }

    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileDownloadManager _fileDownloadManager;
    private readonly FileUploadManager _fileUploadManager;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly IpcManager _ipcManager;
    private readonly MareCharaFileDataFactory _mareCharaFileDataFactory;
    private readonly Dictionary<string, CharaDataFullExtendedDto> _ownCharaData = [];
    private readonly PlayerDataFactory _playerDataFactory;
    private readonly Dictionary<string, CharaDataExtendedUpdateDto> _updateDtos = [];
    private CancellationTokenSource _applicationCts = new();
    private CancellationTokenSource _charaDataCreateCts = new();

    private CharacterData _lastCreatedCharaData = null!;
    private CancellationTokenSource _uploadCts = new();

    public CharaDataManager(ILogger<CharaDataManager> logger, ApiController apiController,
        FileUploadManager fileUploadManager, FileCacheManager fileCacheManager,
        MareMediator mareMediator, IpcManager ipcManager, GameObjectHandlerFactory gameObjectHandlerFactory,
        DalamudUtilService dalamudUtilService, FileDownloadManagerFactory fileDownloadManagerFactory,
        PlayerDataFactory playerDataFactory) : base(logger, mareMediator)
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
        mareMediator.Subscribe<ConnectedMessage>(this, (msg) =>
        {
            _ownCharaData.Clear();
            Initialized = false;
        });

        mareMediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) =>
        {
            _lastCreatedCharaData = msg.CharacterData;
        });
    }

    public string DataApplicationProgress { get; private set; } = string.Empty;
    public Task? DataApplicationTask { get; private set; }
    public Task<(string Output, bool Success)>? DataCreationTask { get; private set; }
    public Task? DataGetTimeoutTask { get; private set; }
    public Task<List<CharaDataFullExtendedDto>>? GetAllDataTask { get; private set; }
    public bool Initialized { get; private set; }
    public bool IsExportingMcdf { get; private set; }
    public IDictionary<string, CharaDataFullExtendedDto> OwnCharaData => _ownCharaData;
    public Task? CharaUpdateTask { get; set; }
    public ValueProgress<string>? UploadProgress { get; private set; }
    public Task<(string Output, bool Success)>? UploadTask { get; private set; }

    public void ApplyDataToGposeTarget(CharaDataFullExtendedDto dataDto)
    {
        var charaName = _dalamudUtilService.GposeTargetGameObject?.Name.TextValue ?? string.Empty;
        CharaDataDownloadDto downloadDto = new(dataDto.Id, dataDto.UploaderUID)
        {
            CustomizeData = dataDto.CustomizeData,
            Description = dataDto.Description,
            FileGamePaths = dataDto.FileGamePaths,
            GlamourerData = dataDto.GlamourerData,
            FileSwaps = dataDto.FileSwaps,
            ManipulationData = dataDto.ManipulationData,
            UpdatedDate = dataDto.UpdatedDate
        };
        DataApplicationTask = ApplyDataAsync(charaName, downloadDto);
    }

    public void CancelDataApplicationToSelf()
    {
        _applicationCts.Cancel();
    }

    public void CancelUpload()
    {
        _uploadCts.Cancel();
    }

    public void CreateCharaData(CancellationToken cancelToken)
    {
        DataCreationTask = Task.Run(async () =>
        {
            var result = await _apiController.CharaDataCreate().ConfigureAwait(false);
            _ = Task.Run(async () =>
            {
                _charaDataCreateCts = _charaDataCreateCts.CancelRecreate();
                using var ct = CancellationTokenSource.CreateLinkedTokenSource(_charaDataCreateCts.Token, cancelToken);
                await Task.Delay(TimeSpan.FromSeconds(10), ct.Token).ConfigureAwait(false);
                DataCreationTask = null;
            });

            AddOrUpdateDto(result);

            return ("Failed to create character data, see log for more information", false);
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

    public async Task GetAllData(CancellationToken cancelToken)
    {
        _ownCharaData.Clear();
        GetAllDataTask = Task.Run(async () =>
        {
            _charaDataCreateCts = _charaDataCreateCts.CancelRecreate();
            var result = await _apiController.CharaDataGetOwn().ConfigureAwait(false);

            Initialized = true;

            if (result.Any())
            {
                DataGetTimeoutTask = Task.Run(async () =>
                {
                    using var ct = CancellationTokenSource.CreateLinkedTokenSource(_charaDataCreateCts.Token, cancelToken);
                    await Task.Delay(TimeSpan.FromMinutes(1), ct.Token).ConfigureAwait(false);
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

    public CharaDataExtendedUpdateDto? GetUpdateDto(string id)
    {
        if (_updateDtos.TryGetValue(id, out var dto))
            return dto;
        return null;
    }

    public void SaveMareCharaFile(string description, string filePath)
    {
        IsExportingMcdf = true;
        var tempFilePath = filePath + ".tmp";

        try
        {
            if (_lastCreatedCharaData == null) return;

            var mareCharaFileData = _mareCharaFileDataFactory.Create(description, _lastCreatedCharaData);
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
            lz4.Flush();
            fs.Flush();
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

        _ = Task.Run(async () =>
        {
            if (_dalamudUtilService.IsInGpose)
            {
                var chara = await _dalamudUtilService.GetPlayerCharacterAsync().ConfigureAwait(false);
                if (_dalamudUtilService.IsInGpose)
                {
                    chara = (IPlayerCharacter?)(await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(chara.Name.TextValue, true).ConfigureAwait(false));
                }

                if (chara == null)
                    return;

                using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Player,
                    () => _dalamudUtilService.GetCharacterFromObjectTableByIndex(chara.ObjectIndex)?.Address ?? IntPtr.Zero, isWatched: false).ConfigureAwait(false);

                PlayerData.Data.CharacterData newCdata = new();
                await _playerDataFactory.BuildCharacterData(newCdata, tempHandler, CancellationToken.None).ConfigureAwait(false);
                _lastCreatedCharaData = newCdata.ToAPI();
            }

            if (_lastCreatedCharaData != null)
            {
                var hasGlamourerData = _lastCreatedCharaData.GlamourerData.TryGetValue(API.Data.Enum.ObjectKind.Player, out var playerDataString);
                if (!hasGlamourerData) updateDto.GlamourerData = null;
                else updateDto.GlamourerData = playerDataString;

                var hasCustomizeData = _lastCreatedCharaData.CustomizePlusData.TryGetValue(API.Data.Enum.ObjectKind.Player, out var customizeDataString);
                if (!hasCustomizeData) updateDto.CustomizeData = null;
                else updateDto.CustomizeData = customizeDataString;

                updateDto.ManipulationData = _lastCreatedCharaData.ManipulationData;

                var hasFiles = _lastCreatedCharaData.FileReplacements.TryGetValue(API.Data.Enum.ObjectKind.Player, out var fileReplacements);
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
        });
    }

    public void UploadCharaData(string id)
    {
        var hasUpdateDto = _updateDtos.TryGetValue(id, out var updateDto);
        if (!hasUpdateDto || updateDto == null) return;

        CharaUpdateTask = CharaUpdateAsync(updateDto);
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

    public void UploadMissingFiles(string id)
    {
        var hasDto = _ownCharaData.TryGetValue(id, out var dto);
        if (!hasDto || dto == null) return;

        var missingFileList = dto.MissingFiles.ToList();
        UploadTask = UploadFiles(missingFileList, async () =>
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
        CharaDataDownloadDto downloadDto = new(dataDto.Id, dataDto.UploaderUID)
        {
            CustomizeData = dataDto.CustomizeData,
            Description = dataDto.Description,
            FileGamePaths = dataDto.FileGamePaths,
            GlamourerData = dataDto.GlamourerData,
            FileSwaps = dataDto.FileSwaps,
            ManipulationData = dataDto.ManipulationData,
            UpdatedDate = dataDto.UpdatedDate
        };
        DataApplicationTask = ApplyDataAsync(chara, downloadDto);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
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

    private async Task ApplyDataAsync(string charaName, CharaDataDownloadDto charaDataDownloadDto, bool autoRevert = true)
    {
        _applicationCts = _applicationCts.CancelRecreate();
        var token = _applicationCts.Token;
        ICharacter? chara = (await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(charaName, _dalamudUtilService.IsInGpose).ConfigureAwait(false));

        if (chara == null)
            return;

        var applicationId = Guid.NewGuid();

        DataApplicationProgress = "Checking local files";

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
                DataApplicationProgress = "Preview application aborted.";
                DataApplicationTask = null;
                return;
            }
        }

        if (!_dalamudUtilService.IsInGpose)
            Mediator.Publish(new HaltCharaDataCreation(false));

        Guid? cPlusId = null;
        Guid penumbraCollection;
        try
        {
            DataApplicationProgress = "Applying Penumbra information";
            penumbraCollection = await _ipcManager.Penumbra.CreateTemporaryCollectionAsync(Logger, charaDataDownloadDto.Id).ConfigureAwait(false);
            await _ipcManager.Penumbra.AssignTemporaryCollectionAsync(Logger, penumbraCollection, chara.ObjectIndex).ConfigureAwait(false);
            await _ipcManager.Penumbra.SetTemporaryModsAsync(Logger, applicationId, penumbraCollection, modPaths).ConfigureAwait(false);
            await _ipcManager.Penumbra.SetManipulationDataAsync(Logger, applicationId, penumbraCollection, charaDataDownloadDto.ManipulationData ?? string.Empty).ConfigureAwait(false);

            DataApplicationProgress = "Applying Glamourer and redrawing Character";
            await _ipcManager.Glamourer.ApplyAllAsync(Logger, tempHandler, charaDataDownloadDto.GlamourerData, applicationId, token).ConfigureAwait(false);
            await _ipcManager.Penumbra.RedrawAsync(Logger, tempHandler, applicationId, token).ConfigureAwait(false);
            await _dalamudUtilService.WaitWhileCharacterIsDrawing(Logger, tempHandler, applicationId, ct: token).ConfigureAwait(false);
            await _ipcManager.Penumbra.RemoveTemporaryCollectionAsync(Logger, applicationId, penumbraCollection).ConfigureAwait(false);

            DataApplicationProgress = "Applying Customize+ data";
            if (!string.IsNullOrEmpty(charaDataDownloadDto.CustomizeData))
            {
                cPlusId = await _ipcManager.CustomizePlus.SetBodyScaleAsync(tempHandler.Address, charaDataDownloadDto.CustomizeData).ConfigureAwait(false);
            }
            else
            {
                cPlusId = await _ipcManager.CustomizePlus.SetBodyScaleAsync(tempHandler.Address, Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"))).ConfigureAwait(false);
            }

            if (autoRevert)
            {
                int i = 15;
                while (i > 0)
                {
                    DataApplicationProgress = $"All data applied. Reverting automatically in {i} seconds.";
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                    i--;
                }
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
                await _ipcManager.Glamourer.RevertAsync(Logger, tempHandler, applicationId, CancellationToken.None).ConfigureAwait(false);
                if (cPlusId != null)
                {
                    await _ipcManager.CustomizePlus.RevertByIdAsync(cPlusId).ConfigureAwait(false);
                }
                await _ipcManager.Penumbra.RedrawAsync(Logger, tempHandler, applicationId, CancellationToken.None).ConfigureAwait(false);
                await _dalamudUtilService.WaitWhileCharacterIsDrawing(Logger, tempHandler, applicationId).ConfigureAwait(false);
            }

            if (!_dalamudUtilService.IsInGpose)
                Mediator.Publish(new HaltCharaDataCreation(true));
            DataApplicationTask = null;
            DataApplicationProgress = string.Empty;
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
}
