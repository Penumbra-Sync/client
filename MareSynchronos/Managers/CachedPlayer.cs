using Dalamud.Interface.Internal.Notifications;
using Dalamud.Logging;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Factories;
using MareSynchronos.FileCache;
using MareSynchronos.Mediator;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Managers;

public class CachedPlayer : MediatorSubscriberBase, IDisposable
{
    private readonly ApiController _apiController;
    private readonly DalamudUtil _dalamudUtil;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly IpcManager _ipcManager;
    private readonly FileCacheManager _fileDbManager;
    private API.Data.CharacterData _cachedData = new();
    private GameObjectHandler? _currentOtherChara;
    private CancellationTokenSource? _downloadCancellationTokenSource = new();
    private string _lastGlamourerData = string.Empty;
    private string _originalGlamourerData = string.Empty;

    public CachedPlayer(ILogger<CachedPlayer> logger, OnlineUserIdentDto onlineUser, GameObjectHandlerFactory gameObjectHandlerFactory,
        IpcManager ipcManager, ApiController apiController,
        DalamudUtil dalamudUtil, FileCacheManager fileDbManager, MareMediator mediator) : base(logger, mediator)
    {
        OnlineUser = onlineUser;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _ipcManager = ipcManager;
        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _fileDbManager = fileDbManager;
    }

    private OnlineUserIdentDto OnlineUser { get; set; }
    private IntPtr PlayerCharacter => _currentOtherChara?.Address ?? IntPtr.Zero;
    public string? PlayerName { get; private set; }
    public string PlayerNameHash => OnlineUser.Ident;

    public void ApplyCharacterData(API.Data.CharacterData characterData, OptionalPluginWarning warning, bool forced = false)
    {
        _logger.LogDebug("Received data for {player}", this);

        _logger.LogDebug("Checking for files to download for player {name}", this);
        _logger.LogDebug("Hash for data is {newHash}, current cache hash is {oldHash}", characterData.DataHash.Value, _cachedData.DataHash.Value);

        if (!_ipcManager.CheckPenumbraApi()) return;
        if (!_ipcManager.CheckGlamourerApi()) return;

        if (string.Equals(characterData.DataHash.Value, _cachedData.DataHash.Value, StringComparison.Ordinal) && !forced) return;

        var charaDataToUpdate = CheckUpdatedData(_cachedData.DeepClone(), characterData, forced);

        if (charaDataToUpdate.TryGetValue(ObjectKind.Player, out var playerChanges))
        {
            NotifyForMissingPlugins(playerChanges, warning);
        }

        DownloadAndApplyCharacter(characterData, charaDataToUpdate);

        _cachedData = characterData;
    }

    private Dictionary<ObjectKind, HashSet<PlayerChanges>> CheckUpdatedData(API.Data.CharacterData oldData, API.Data.CharacterData newData, bool forced)
    {
        var charaDataToUpdate = new Dictionary<ObjectKind, HashSet<PlayerChanges>>();
        foreach (ObjectKind objectKind in Enum.GetValues<ObjectKind>())
        {
            charaDataToUpdate[objectKind] = new();
            oldData.FileReplacements.TryGetValue(objectKind, out var existingFileReplacements);
            newData.FileReplacements.TryGetValue(objectKind, out var newFileReplacements);
            oldData.GlamourerData.TryGetValue(objectKind, out var existingGlamourerData);
            newData.GlamourerData.TryGetValue(objectKind, out var newGlamourerData);

            bool hasNewButNotOldFileReplacements = newFileReplacements != null && existingFileReplacements == null;
            bool hasOldButNotNewFileReplacements = existingFileReplacements != null && newFileReplacements == null;

            bool hasNewButNotOldGlamourerData = newGlamourerData != null && existingGlamourerData == null;
            bool hasOldButNotNewGlamourerData = existingGlamourerData != null && newGlamourerData == null;

            bool hasNewAndOldFileReplacements = newFileReplacements != null && existingFileReplacements != null;
            bool hasNewAndOldGlamourerData = newGlamourerData != null && existingGlamourerData != null;

            if (hasNewButNotOldFileReplacements || hasOldButNotNewFileReplacements || hasNewButNotOldGlamourerData || hasOldButNotNewGlamourerData)
            {
                _logger.LogDebug("Updating {object}/{kind} (Some new data arrived: NewButNotOldFiles:{hasNewButNotOldFileReplacements}," +
                    " OldButNotNewFiles:{hasOldButNotNewFileReplacements}, NewButNotOldGlam:{hasNewButNotOldGlamourerData}, OldButNotNewGlam:{hasOldButNotNewGlamourerData}) => {change}",
                    this, objectKind, hasNewButNotOldFileReplacements, hasOldButNotNewFileReplacements, hasNewButNotOldGlamourerData, hasOldButNotNewGlamourerData, PlayerChanges.Mods);
                charaDataToUpdate[objectKind].Add(PlayerChanges.Mods);
            }
            else
            {
                if (hasNewAndOldFileReplacements)
                {
                    bool listsAreEqual = Enumerable.SequenceEqual(oldData.FileReplacements[objectKind], newData.FileReplacements[objectKind], FileReplacementDataComparer.Instance);
                    if (!listsAreEqual || forced)
                    {
                        _logger.LogDebug("Updating {object}/{kind} (FileReplacements not equal) => {change}", this, objectKind, PlayerChanges.Mods);
                        charaDataToUpdate[objectKind].Add(PlayerChanges.Mods);
                    }
                }

                if (hasNewAndOldGlamourerData)
                {
                    bool glamourerDataDifferent = !string.Equals(oldData.GlamourerData[objectKind], newData.GlamourerData[objectKind], StringComparison.Ordinal);
                    if (glamourerDataDifferent || forced)
                    {
                        _logger.LogDebug("Updating {object}/{kind} (Glamourer different) => {change}", this, objectKind, PlayerChanges.Mods);
                        charaDataToUpdate[objectKind].Add(PlayerChanges.Mods);
                    }
                }
            }

            if (objectKind != ObjectKind.Player) continue;

            bool manipDataDifferent = !string.Equals(oldData.ManipulationData, newData.ManipulationData, StringComparison.Ordinal);
            if (manipDataDifferent || forced)
            {
                _logger.LogDebug("Updating {object}/{kind} (Diff manip data) => {change}", this, objectKind, PlayerChanges.Mods);
                charaDataToUpdate[objectKind].Add(PlayerChanges.Mods);
            }

            bool heelsOffsetDifferent = oldData.HeelsOffset != newData.HeelsOffset;
            if (heelsOffsetDifferent || forced)
            {
                _logger.LogDebug("Updating {object}/{kind} (Diff heels data) => {change}", this, objectKind, PlayerChanges.Heels);
                charaDataToUpdate[objectKind].Add(PlayerChanges.Heels);
            }

            bool customizeDataDifferent = !string.Equals(oldData.CustomizePlusData, newData.CustomizePlusData, StringComparison.Ordinal);
            if (customizeDataDifferent || forced)
            {
                _logger.LogDebug("Updating {object}/{kind} (Diff customize data) => {change}", this, objectKind, PlayerChanges.Customize);
                charaDataToUpdate[objectKind].Add(PlayerChanges.Customize);
            }

            bool palettePlusDataDifferent = !string.Equals(oldData.PalettePlusData, newData.PalettePlusData, StringComparison.Ordinal);
            if (palettePlusDataDifferent || forced)
            {
                _logger.LogDebug("Updating {object}/{kind} (Diff palette data) => {change}", this, objectKind, PlayerChanges.Palette);
                charaDataToUpdate[objectKind].Add(PlayerChanges.Palette);
            }
        }

        foreach (KeyValuePair<ObjectKind, HashSet<PlayerChanges>> data in charaDataToUpdate.ToList())
        {
            if (!data.Value.Any()) charaDataToUpdate.Remove(data.Key);
            else charaDataToUpdate[data.Key] = data.Value.OrderByDescending(p => (int)p).ToHashSet();
        }

        return charaDataToUpdate;
    }

    private enum PlayerChanges
    {
        Heels = 1,
        Customize = 2,
        Palette = 3,
        Mods = 4
    }

    private void NotifyForMissingPlugins(HashSet<PlayerChanges> changes, OptionalPluginWarning warning)
    {
        List<string> missingPluginsForData = new();
        if (changes.Contains(PlayerChanges.Heels))
        {
            if (!warning.ShownHeelsWarning && !_ipcManager.CheckHeelsApi())
            {
                missingPluginsForData.Add("Heels");
                warning.ShownHeelsWarning = true;
            }
        }
        if (changes.Contains(PlayerChanges.Customize))
        {
            if (!warning.ShownCustomizePlusWarning && !_ipcManager.CheckCustomizePlusApi())
            {
                missingPluginsForData.Add("Customize+");
                warning.ShownCustomizePlusWarning = true;
            }
        }

        if (changes.Contains(PlayerChanges.Palette))
        {
            if (!warning.ShownPalettePlusWarning && !_ipcManager.CheckPalettePlusApi())
            {
                missingPluginsForData.Add("Palette+");
                warning.ShownPalettePlusWarning = true;
            }
        }

        if (missingPluginsForData.Any())
        {
            Mediator.Publish(new NotificationMessage("Missing plugins for " + PlayerName,
                $"Received data for {PlayerName} that contained information for plugins you have not installed. Install {string.Join(", ", missingPluginsForData)} to experience their character fully.",
                NotificationType.Warning, 10000));
        }
    }

    public bool CheckExistence()
    {
        if (PlayerName == null || _currentOtherChara == null
            || !string.Equals(PlayerName, _currentOtherChara.Name, StringComparison.Ordinal)
            || _currentOtherChara.CurrentAddress == IntPtr.Zero)
        {
            return false;
        }

        return true;
    }

    public override void Dispose()
    {
        if (string.IsNullOrEmpty(PlayerName)) return; // already disposed

        base.Dispose();
        var name = PlayerName;
        PlayerName = null;
        _logger.LogDebug("Disposing {name} ({user})", name, OnlineUser);
        try
        {
            Guid applicationId = Guid.NewGuid();
            _logger.LogTrace("[{applicationId}] Restoring state for {name} ({OnlineUser})", applicationId, name, OnlineUser);
            _ipcManager.PenumbraRemoveTemporaryCollection(_logger, applicationId, name);
            _downloadCancellationTokenSource?.Cancel();
            _downloadCancellationTokenSource?.Dispose();
            _downloadCancellationTokenSource = null;
            nint ptr = PlayerCharacter;
            _currentOtherChara?.Dispose();

            if (ptr != IntPtr.Zero && !_dalamudUtil.IsZoning)
            {
                foreach (KeyValuePair<ObjectKind, List<FileReplacementData>> item in _cachedData.FileReplacements)
                {
                    Task.Run(async () => await RevertCustomizationData(ptr, item.Key, name, applicationId).ConfigureAwait(false));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error on disposal of {name}", name);
        }
        finally
        {
            _currentOtherChara = null;
            _cachedData = new();
            _logger.LogDebug("Disposing {name} complete", name);
            PlayerName = null;
        }
    }

    public void Initialize(string name)
    {
        PlayerName = name;
        _currentOtherChara = _gameObjectHandlerFactory.Create(ObjectKind.Player, () => _dalamudUtil.GetPlayerCharacterFromObjectTableByName(PlayerName)?.Address ?? IntPtr.Zero, isWatched: false);

        _originalGlamourerData = _ipcManager.GlamourerGetCharacterCustomization(PlayerCharacter);
        _lastGlamourerData = _originalGlamourerData;
        Mediator.Subscribe<PenumbraRedrawMessage>(this, (msg) => IpcManagerOnPenumbraRedrawEvent(((PenumbraRedrawMessage)msg)));
        Mediator.Subscribe<CharacterChangedMessage>(this, (msg) =>
        {
            var actualMsg = (CharacterChangedMessage)msg;
            if (actualMsg.GameObjectHandler == _currentOtherChara && (_applicationTask?.IsCompleted ?? true))
            {
                _logger.LogTrace("Saving new Glamourer Data for {this}", this);
                _lastGlamourerData = _ipcManager.GlamourerGetCharacterCustomization(PlayerCharacter);
            }
        });

        _logger.LogDebug("Initializing Player {obj}", this);
    }

    public override string ToString()
    {
        return OnlineUser.User.AliasOrUID + ":" + PlayerName + ":" + ((PlayerCharacter != IntPtr.Zero) ? "HasChar" : "NoChar");
    }

    private void ApplyBaseData(Guid applicationId, Dictionary<string, string> moddedPaths, string manipulationData)
    {
        _ipcManager.PenumbraRemoveTemporaryCollection(_logger, applicationId, PlayerName!);
        _ipcManager.PenumbraSetTemporaryMods(_logger, applicationId, PlayerName!, moddedPaths, manipulationData);
    }

    private async Task ApplyCustomizationData(Guid applicationId, KeyValuePair<ObjectKind, HashSet<PlayerChanges>> changes, API.Data.CharacterData charaData)
    {
        if (PlayerCharacter == IntPtr.Zero) return;
        var ptr = PlayerCharacter;
        var handler = changes.Key switch
        {
            ObjectKind.Player => _currentOtherChara!,
            ObjectKind.Companion => _gameObjectHandlerFactory.Create(changes.Key, () => _dalamudUtil.GetCompanion(ptr), isWatched: false),
            ObjectKind.MinionOrMount => _gameObjectHandlerFactory.Create(changes.Key, () => _dalamudUtil.GetMinionOrMount(ptr), isWatched: false),
            ObjectKind.Pet => _gameObjectHandlerFactory.Create(changes.Key, () => _dalamudUtil.GetPet(ptr), isWatched: false),
            _ => throw new NotSupportedException("ObjectKind not supported: " + changes.Key)
        };


        CancellationTokenSource applicationTokenSource = new();
        applicationTokenSource.CancelAfter(TimeSpan.FromSeconds(30));

        if (handler.Address == IntPtr.Zero)
        {
            if (handler != _currentOtherChara) handler.Dispose();
            return;
        }

        _logger.LogDebug("[{applicationId}] Applying Customization Data for {handler}", applicationId, handler);
        await _dalamudUtil.WaitWhileCharacterIsDrawing(_logger, handler, applicationId, 30000).ConfigureAwait(false);
        foreach (var change in changes.Value)
        {
            _logger.LogDebug("[{applicationId}] Processing {change} for {handler}", applicationId, change, handler);
            switch (change)
            {
                case PlayerChanges.Palette:
                    await _ipcManager.PalettePlusSetPalette(handler.Address, charaData.PalettePlusData).ConfigureAwait(false);
                    break;
                case PlayerChanges.Customize:
                    await _ipcManager.CustomizePlusSetBodyScale(handler.Address, charaData.CustomizePlusData).ConfigureAwait(false);
                    break;
                case PlayerChanges.Heels:
                    await _ipcManager.HeelsSetOffsetForPlayer(handler.Address, charaData.HeelsOffset).ConfigureAwait(false);
                    break;
                case PlayerChanges.Mods:
                    if (charaData.GlamourerData.TryGetValue(changes.Key, out var glamourerData))
                    {
                        await _ipcManager.GlamourerApplyAll(_logger, handler, glamourerData, applicationId, applicationTokenSource.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await _ipcManager.PenumbraRedraw(_logger, handler, applicationId, applicationTokenSource.Token).ConfigureAwait(false);
                    }
                    break;
            }
        }

        if (handler != _currentOtherChara) handler.Dispose();
    }

    private void DownloadAndApplyCharacter(API.Data.CharacterData charaData, Dictionary<ObjectKind, HashSet<PlayerChanges>> updatedData)
    {
        if (!updatedData.Any())
        {
            _logger.LogDebug("Nothing to update for {obj}", this);
        }

        var updateModdedPaths = updatedData.Values.Any(v => v.Any(p => p == PlayerChanges.Mods));

        _downloadCancellationTokenSource?.Cancel();
        _downloadCancellationTokenSource?.Dispose();
        _downloadCancellationTokenSource = new CancellationTokenSource();
        var downloadToken = _downloadCancellationTokenSource.Token;

        var downloadId = _apiController.GetDownloadId();
        Task.Run(async () =>
        {
            List<FileReplacementData> toDownloadReplacements;

            Dictionary<string, string> moddedPaths = new(StringComparer.Ordinal);

            if (updateModdedPaths)
            {
                int attempts = 0;
                while ((toDownloadReplacements = TryCalculateModdedDictionary(charaData, out moddedPaths)).Count > 0 && attempts++ <= 10)
                {
                    downloadId = _apiController.GetDownloadId();
                    _logger.LogDebug("Downloading missing files for player {name}, {kind}", PlayerName, updatedData);
                    if (toDownloadReplacements.Any())
                    {
                        await _apiController.DownloadFiles(downloadId, toDownloadReplacements, downloadToken).ConfigureAwait(false);
                        _apiController.CancelDownload(downloadId);
                    }

                    if (downloadToken.IsCancellationRequested)
                    {
                        _logger.LogTrace("Detected cancellation");
                        return;
                    }

                    if ((TryCalculateModdedDictionary(charaData, out moddedPaths)).All(c => _apiController.ForbiddenTransfers.Any(f => string.Equals(f.Hash, c.Hash, StringComparison.Ordinal))))
                    {
                        break;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
            }

            while (!_applicationTask?.IsCompleted ?? false)
            {
                // block until current application is done
                _logger.LogDebug("Waiting for current data application (Id: {id}) to finish", _applicationId);
                await Task.Delay(250).ConfigureAwait(false);
                if (downloadToken.IsCancellationRequested) return;
            }

            _applicationTask = Task.Run(async () =>
            {
                _applicationId = Guid.NewGuid();
                _logger.LogDebug("[{applicationId}] Starting application task", _applicationId);

                if (updateModdedPaths && (moddedPaths.Any() || !string.IsNullOrEmpty(charaData.ManipulationData)))
                {
                    ApplyBaseData(_applicationId, moddedPaths, charaData.ManipulationData);
                }

                foreach (var kind in updatedData)
                {
                    await ApplyCustomizationData(_applicationId, kind, charaData).ConfigureAwait(false);
                }

                _logger.LogDebug("[{applicationId}] Application finished", _applicationId);
            });

        }, downloadToken).ContinueWith(task =>
            {
                _downloadCancellationTokenSource = null;

                if (!task.IsCanceled) return;

                _logger.LogDebug("Download was cancelled");
                _apiController.CancelDownload(downloadId);
            });
    }

    private Task? _applicationTask;

    private CancellationTokenSource _redrawCts = new();
    private Guid _applicationId;

    private void IpcManagerOnPenumbraRedrawEvent(PenumbraRedrawMessage msg)
    {
        var player = _dalamudUtil.GetCharacterFromObjectTableByIndex(msg.ObjTblIdx);
        if (player == null || !string.Equals(player.Name.ToString(), PlayerName, StringComparison.OrdinalIgnoreCase)) return;
        _redrawCts.Cancel();
        _redrawCts.Dispose();
        _redrawCts = new();
        _redrawCts.CancelAfter(TimeSpan.FromSeconds(30));
        var token = _redrawCts.Token;

        Task.Run(async () =>
        {
            var applicationId = Guid.NewGuid();
            await _dalamudUtil.WaitWhileCharacterIsDrawing(_logger, _currentOtherChara!, applicationId, ct: token).ConfigureAwait(false);
            _logger.LogDebug("Unauthorized character change detected");
            await ApplyCustomizationData(applicationId, new(ObjectKind.Player,
                new HashSet<PlayerChanges>(new[] { PlayerChanges.Palette, PlayerChanges.Customize, PlayerChanges.Heels, PlayerChanges.Mods })),
                _cachedData).ConfigureAwait(false);
        }, token);
    }

    private async Task RevertCustomizationData(IntPtr address, ObjectKind objectKind, string name, Guid applicationId)
    {
        if (address == IntPtr.Zero) return;

        var cancelToken = new CancellationTokenSource();
        cancelToken.CancelAfter(TimeSpan.FromSeconds(10));

        _logger.LogDebug("[{applicationId}] Reverting all Customization for {alias}/{name} {objectKind}", applicationId, OnlineUser.User.AliasOrUID, name, objectKind);

        if (objectKind == ObjectKind.Player)
        {
            using GameObjectHandler tempHandler = _gameObjectHandlerFactory.Create(ObjectKind.Player, () => address, false);
            _logger.LogDebug("[{applicationId}] Restoring Customization for {alias}/{name}: {data}", applicationId, OnlineUser.User.AliasOrUID, name, _originalGlamourerData);
            await _ipcManager.GlamourerApplyOnlyCustomization(_logger, tempHandler, _originalGlamourerData, applicationId, cancelToken.Token, fireAndForget: false).ConfigureAwait(false);
            _logger.LogDebug("[{applicationId}] Restoring Equipment for {alias}/{name}: {data}", applicationId, OnlineUser.User.AliasOrUID, name, _lastGlamourerData);
            await _ipcManager.GlamourerApplyOnlyEquipment(_logger, tempHandler, _lastGlamourerData, applicationId, cancelToken.Token, fireAndForget: false).ConfigureAwait(false);
            _logger.LogDebug("[{applicationId}] Restoring Heels for {alias}/{name}", applicationId, OnlineUser.User.AliasOrUID, name);
            await _ipcManager.HeelsRestoreOffsetForPlayer(address).ConfigureAwait(false);
            _logger.LogDebug("[{applicationId}] Restoring C+ for {alias}/{name}", applicationId, OnlineUser.User.AliasOrUID, name);
            await _ipcManager.CustomizePlusRevert(address).ConfigureAwait(false);
            _logger.LogDebug("[{applicationId}] Restoring Palette+ for {alias}/{name}", applicationId, OnlineUser.User.AliasOrUID, name);
            await _ipcManager.PalettePlusRemovePalette(address).ConfigureAwait(false);
        }
        else if (objectKind == ObjectKind.MinionOrMount)
        {
            var minionOrMount = _dalamudUtil.GetMinionOrMount(address);
            if (minionOrMount != IntPtr.Zero)
            {
                using GameObjectHandler tempHandler = _gameObjectHandlerFactory.Create(ObjectKind.MinionOrMount, () => minionOrMount, isWatched: false);
                await _ipcManager.PenumbraRedraw(_logger, tempHandler, applicationId, cancelToken.Token, fireAndForget: false).ConfigureAwait(false);
            }
        }
        else if (objectKind == ObjectKind.Pet)
        {
            var pet = _dalamudUtil.GetPet(address);
            if (pet != IntPtr.Zero)
            {
                using GameObjectHandler tempHandler = _gameObjectHandlerFactory.Create(ObjectKind.Pet, () => pet, isWatched: false);
                await _ipcManager.PenumbraRedraw(_logger, tempHandler, applicationId, cancelToken.Token, fireAndForget: false).ConfigureAwait(false);
            }
        }
        else if (objectKind == ObjectKind.Companion)
        {
            var companion = _dalamudUtil.GetCompanion(address);
            if (companion != IntPtr.Zero)
            {
                using GameObjectHandler tempHandler = _gameObjectHandlerFactory.Create(ObjectKind.Pet, () => companion, isWatched: false);
                await _ipcManager.PenumbraRedraw(_logger, tempHandler, applicationId, cancelToken.Token, fireAndForget: false).ConfigureAwait(false);
            }
        }
    }

    private List<FileReplacementData> TryCalculateModdedDictionary(API.Data.CharacterData charaData, out Dictionary<string, string> moddedDictionary)
    {
        List<FileReplacementData> missingFiles = new();
        moddedDictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var item in charaData.FileReplacements.SelectMany(k => k.Value.Where(v => string.IsNullOrEmpty(v.FileSwapPath))).ToList())
            {
                foreach (var gamePath in item.GamePaths)
                {
                    var fileCache = _fileDbManager.GetFileCacheByHash(item.Hash);
                    if (fileCache != null)
                    {
                        moddedDictionary[gamePath] = fileCache.ResolvedFilepath;
                    }
                    else
                    {
                        _logger.LogTrace("Missing file: {hash}", item.Hash);
                        missingFiles.Add(item);
                    }
                }
            }

            foreach (var item in charaData.FileReplacements.SelectMany(k => k.Value.Where(v => !string.IsNullOrEmpty(v.FileSwapPath))).ToList())
            {
                foreach (var gamePath in item.GamePaths)
                {
                    _logger.LogTrace("Adding file swap for {path}: {fileSwap}", gamePath, item.FileSwapPath);
                    moddedDictionary[gamePath] = item.FileSwapPath;
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Something went wrong during calculation replacements");
        }
        _logger.LogDebug("ModdedPaths calculated, missing files: {count}", missingFiles.Count);
        return missingFiles;
    }
}