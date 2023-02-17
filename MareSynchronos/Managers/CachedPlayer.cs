using Dalamud.Interface.Internal.Notifications;
using Dalamud.Logging;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.User;
using MareSynchronos.FileCache;
using MareSynchronos.Mediator;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Managers;

public class CachedPlayer : MediatorSubscriberBase, IDisposable
{
    private readonly ApiController _apiController;
    private readonly DalamudUtil _dalamudUtil;
    private readonly IpcManager _ipcManager;
    private readonly FileCacheManager _fileDbManager;
    private API.Data.CharacterData _cachedData = new();
    private GameObjectHandler? _currentOtherChara;
    private CancellationTokenSource? _downloadCancellationTokenSource = new();
    private string _lastGlamourerData = string.Empty;
    private string _originalGlamourerData = string.Empty;

    public CachedPlayer(OnlineUserIdentDto onlineUser, IpcManager ipcManager, ApiController apiController, DalamudUtil dalamudUtil, FileCacheManager fileDbManager, MareMediator mediator) : base(mediator)
    {
        OnlineUser = onlineUser;
        _ipcManager = ipcManager;
        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _fileDbManager = fileDbManager;
    }

    public OnlineUserIdentDto OnlineUser { get; set; }
    public IntPtr PlayerCharacter => _currentOtherChara?.Address ?? IntPtr.Zero;
    public string? PlayerName { get; private set; }
    public string PlayerNameHash => OnlineUser.Ident;

    public void ApplyCharacterData(API.Data.CharacterData characterData, OptionalPluginWarning warning, bool forced = false)
    {
        Logger.Debug("Received data for " + this);

        Logger.Debug("Checking for files to download for player " + PlayerName);
        Logger.Debug("Hash for data is " + characterData.DataHash.Value + ", current cache hash is " + _cachedData.DataHash.Value);

        if (!_ipcManager.CheckPenumbraApi()) return;
        if (!_ipcManager.CheckGlamourerApi()) return;

        if (string.Equals(characterData.DataHash.Value, _cachedData.DataHash.Value, StringComparison.Ordinal) && !forced) return;

        CheckUpdatedData(characterData, out var charaDataToUpdate);

        NotifyForMissingPlugins(characterData, warning);

        _cachedData = characterData;

        DownloadAndApplyCharacter(characterData, charaDataToUpdate);
    }

    private void CheckUpdatedData(API.Data.CharacterData newData, out Dictionary<ObjectKind, HashSet<PlayerChanges>> charaDataToUpdate)
    {
        charaDataToUpdate = new();
        foreach (var objectKind in Enum.GetValues<ObjectKind>())
        {
            charaDataToUpdate[objectKind] = new();
            _cachedData.FileReplacements.TryGetValue(objectKind, out var existingFileReplacements);
            newData.FileReplacements.TryGetValue(objectKind, out var newFileReplacements);
            _cachedData.GlamourerData.TryGetValue(objectKind, out var existingGlamourerData);
            newData.GlamourerData.TryGetValue(objectKind, out var newGlamourerData);

            bool hasNewButNotOldFileReplacements = newFileReplacements != null && existingFileReplacements == null;
            bool hasOldButNotNewFileReplacements = existingFileReplacements != null && newFileReplacements == null;

            bool hasNewButNotOldGlamourerData = newGlamourerData != null && existingGlamourerData == null;
            bool hasOldButNotNewGlamourerData = existingGlamourerData != null && newGlamourerData == null;

            bool hasNewAndOldFileReplacements = newFileReplacements != null && existingFileReplacements != null;
            bool hasNewAndOldGlamourerData = newGlamourerData != null && existingGlamourerData != null;

            if (hasNewButNotOldFileReplacements || hasOldButNotNewFileReplacements || hasNewButNotOldGlamourerData || hasOldButNotNewGlamourerData)
            {
                Logger.Debug($"Updating {objectKind} (Some new data arrived: {hasNewButNotOldFileReplacements} {hasOldButNotNewFileReplacements} {hasNewButNotOldGlamourerData} {hasOldButNotNewGlamourerData})");
                charaDataToUpdate[objectKind].Add(PlayerChanges.Mods);
            }

            if (hasNewAndOldFileReplacements)
            {
                bool listsAreEqual = Enumerable.SequenceEqual(_cachedData.FileReplacements[objectKind], newData.FileReplacements[objectKind], FileReplacementDataComparer.Instance);
                if (!listsAreEqual)
                {
                    Logger.Debug($"Updating {objectKind} (FileReplacements not equal)");
                    charaDataToUpdate[objectKind].Add(PlayerChanges.Mods);
                }
            }

            if (hasNewAndOldGlamourerData)
            {
                bool glamourerDataDifferent = !string.Equals(_cachedData.GlamourerData[objectKind], newData.GlamourerData[objectKind], StringComparison.Ordinal);
                if (glamourerDataDifferent)
                {
                    Logger.Debug($"Updating {objectKind} (Diff glamourer data)");
                    charaDataToUpdate[objectKind].Add(PlayerChanges.Mods);
                }
            }

            if (objectKind == ObjectKind.Player)
            {
                bool manipDataDifferent = !string.Equals(_cachedData.ManipulationData, newData.ManipulationData, StringComparison.Ordinal);
                if (manipDataDifferent)
                {
                    Logger.Debug($"Updating {objectKind} (Diff manip data)");
                    charaDataToUpdate[objectKind].Add(PlayerChanges.Mods);
                }

                bool heelsOffsetDifferent = _cachedData.HeelsOffset != newData.HeelsOffset;
                if (heelsOffsetDifferent)
                {
                    Logger.Debug($"Updating {objectKind} (Diff heels data)");
                    charaDataToUpdate[objectKind].Add(PlayerChanges.Heels);
                }

                bool customizeDataDifferent = !string.Equals(_cachedData.CustomizePlusData, newData.CustomizePlusData, StringComparison.Ordinal);
                if (customizeDataDifferent)
                {
                    Logger.Debug($"Updating {objectKind} (Diff customize data)");
                    charaDataToUpdate[objectKind].Add(PlayerChanges.Customize);
                }

                bool palettePlusDataDifferent = !string.Equals(_cachedData.PalettePlusData, newData.PalettePlusData, StringComparison.Ordinal);
                if (palettePlusDataDifferent)
                {
                    Logger.Debug($"Updating {objectKind} (Diff palette data)");
                    charaDataToUpdate[objectKind].Add(PlayerChanges.Palette);
                }
            }
        }

        foreach (var data in charaDataToUpdate.ToList())
        {
            if (!data.Value.Any()) charaDataToUpdate.Remove(data.Key);
            else charaDataToUpdate[data.Key] = data.Value.OrderBy(p => (int)p).ToHashSet();
        }
    }

    private enum PlayerChanges
    {
        Heels = 1,
        Customize = 2,
        Palette = 3,
        Mods = 4
    }

    private void NotifyForMissingPlugins(API.Data.CharacterData characterData, OptionalPluginWarning warning)
    {
        List<string> missingPluginsForData = new();
        if (characterData.HeelsOffset != default)
        {
            if (!warning.ShownHeelsWarning && !_ipcManager.CheckHeelsApi())
            {
                missingPluginsForData.Add("Heels");
                warning.ShownHeelsWarning = true;
            }
        }
        if (!string.IsNullOrEmpty(characterData.CustomizePlusData))
        {
            if (!warning.ShownCustomizePlusWarning && !_ipcManager.CheckCustomizePlusApi())
            {
                missingPluginsForData.Add("Customize+");
                warning.ShownCustomizePlusWarning = true;
            }
        }

        if (!string.IsNullOrEmpty(characterData.PalettePlusData))
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
            || _currentOtherChara.Address == IntPtr.Zero)
        {
            return false;
        }

        return true;
    }

    public override async void Dispose()
    {
        if (string.IsNullOrEmpty(PlayerName)) return; // already disposed

        base.Dispose();

        Logger.Debug("Disposing " + PlayerName + " (" + OnlineUser + ")");
        try
        {
            Logger.Verbose($"Restoring state for {PlayerName} ({OnlineUser})");
            _currentOtherChara?.Dispose();
            _ipcManager.PenumbraRemoveTemporaryCollection(PlayerName);
            _downloadCancellationTokenSource?.Cancel();
            _downloadCancellationTokenSource?.Dispose();
            _downloadCancellationTokenSource = null;
            if (PlayerCharacter != IntPtr.Zero)
            {
                foreach (var item in _cachedData.FileReplacements)
                {
                    await RevertCustomizationData(item.Key).ConfigureAwait(false);
                }
            }
            _currentOtherChara = null;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex.Message + Environment.NewLine + ex.StackTrace);
        }
        finally
        {
            _cachedData = new();
            Logger.Debug("Disposing " + PlayerName + " complete");
            PlayerName = null;
        }
    }

    public void Initialize(string name)
    {
        PlayerName = name;
        _currentOtherChara = new GameObjectHandler(Mediator, ObjectKind.Player, () => _dalamudUtil.GetPlayerCharacterFromObjectTableByName(PlayerName)?.Address ?? IntPtr.Zero, watchedObject: false);

        _originalGlamourerData = _ipcManager.GlamourerGetCharacterCustomization(PlayerCharacter);
        _lastGlamourerData = _originalGlamourerData;
        Mediator.Subscribe<PenumbraRedrawMessage>(this, (msg) => IpcManagerOnPenumbraRedrawEvent(((PenumbraRedrawMessage)msg)));
        Mediator.Subscribe<CharacterChangedMessage>(this, (msg) =>
        {
            var actualMsg = (CharacterChangedMessage)msg;
            if (actualMsg.GameObjectHandler == _currentOtherChara && !_ipcManager.RequestedRedraw(_currentOtherChara.Address))
            {
                _lastGlamourerData = _ipcManager.GlamourerGetCharacterCustomization(PlayerCharacter);
            }
        });

        Logger.Debug("Initializing Player " + this);
    }

    public override string ToString()
    {
        return OnlineUser.User.AliasOrUID + ":" + PlayerName + ":HasChar " + (PlayerCharacter != IntPtr.Zero);
    }

    private void ApplyBaseData(Dictionary<string, string> moddedPaths)
    {
        _ipcManager.PenumbraRemoveTemporaryCollection(PlayerName!);
        _ipcManager.PenumbraSetTemporaryMods(PlayerName!, moddedPaths, _cachedData.ManipulationData);
    }

    private async Task ApplyCustomizationData(KeyValuePair<ObjectKind, HashSet<PlayerChanges>> changes, API.Data.CharacterData charaData)
    {
        if (PlayerCharacter == IntPtr.Zero) return;

        var handler = changes.Key switch
        {
            ObjectKind.Player => _currentOtherChara!,
            ObjectKind.Companion => new GameObjectHandler(Mediator, changes.Key, () => _dalamudUtil.GetCompanion(PlayerCharacter), false),
            ObjectKind.MinionOrMount => new GameObjectHandler(Mediator, changes.Key, () => _dalamudUtil.GetMinionOrMount(PlayerCharacter) ?? IntPtr.Zero, false),
            ObjectKind.Pet => new GameObjectHandler(Mediator, changes.Key, () => _dalamudUtil.GetPet(PlayerCharacter), false),
            _ => throw new NotSupportedException("ObjectKind not supported: " + changes.Key)
        };

        CancellationTokenSource applicationTokenSource = new();
        applicationTokenSource.CancelAfter(TimeSpan.FromSeconds(30));

        if (handler.Address == IntPtr.Zero) return;

        _dalamudUtil.WaitWhileCharacterIsDrawing(handler, 30000);

        foreach (var change in changes.Value)
        {
            Logger.Debug("Processing " + change + " for " + handler);
            switch (change)
            {
                case PlayerChanges.Palette:
                    _ipcManager.PalettePlusSetPalette(handler.Address, charaData.PalettePlusData);
                    break;
                case PlayerChanges.Customize:
                    _ipcManager.CustomizePlusSetBodyScale(handler.Address, charaData.CustomizePlusData);
                    break;
                case PlayerChanges.Heels:
                    _ipcManager.HeelsSetOffsetForPlayer(charaData.HeelsOffset, handler.Address);
                    break;
                case PlayerChanges.Mods:
                    if (charaData.GlamourerData.TryGetValue(changes.Key, out var glamourerData))
                    {
                        await _ipcManager.GlamourerApplyAll(glamourerData, handler, applicationTokenSource.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await _ipcManager.PenumbraRedraw(handler, applicationTokenSource.Token).ConfigureAwait(false);
                    }
                    break;
            }
        }
    }

    private void DownloadAndApplyCharacter(API.Data.CharacterData charaData, Dictionary<ObjectKind, HashSet<PlayerChanges>> updatedData)
    {
        if (!updatedData.Any())
        {
            Logger.Debug("Nothing to update for " + this);
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

                    Logger.Debug("Downloading missing files for player " + PlayerName + ", kind: " + updatedData);
                    if (toDownloadReplacements.Any())
                    {
                        await _apiController.DownloadFiles(downloadId, toDownloadReplacements, downloadToken).ConfigureAwait(false);
                        _apiController.CancelDownload(downloadId);
                    }

                    if (downloadToken.IsCancellationRequested)
                    {
                        Logger.Verbose("Detected cancellation");
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
                Logger.Debug("Waiting for current data application to finish");
                await Task.Delay(250).ConfigureAwait(false);
                if (downloadToken.IsCancellationRequested) return;
            }

            _applicationTask = Task.Run(async () =>
            {
                if (updateModdedPaths)
                {
                    ApplyBaseData(moddedPaths);
                }

                foreach (var kind in updatedData)
                {
                    await ApplyCustomizationData(kind, charaData).ConfigureAwait(false);
                }
            });

        }, downloadToken).ContinueWith(task =>
        {
            _downloadCancellationTokenSource = null;

            if (!task.IsCanceled) return;

            Logger.Debug("Application was cancelled");
            _apiController.CancelDownload(downloadId);
        });
    }

    private Task? _applicationTask;

    private CancellationTokenSource _redrawCts = new CancellationTokenSource();

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
            _dalamudUtil.WaitWhileCharacterIsDrawing(_currentOtherChara!, ct: token);
            Logger.Debug("Unauthorized character change detected");
            await ApplyCustomizationData(new(ObjectKind.Player,
                new HashSet<PlayerChanges>(new[] { PlayerChanges.Palette, PlayerChanges.Customize, PlayerChanges.Heels, PlayerChanges.Mods })),
                _cachedData).ConfigureAwait(false);
        }, token);
    }

    private async Task RevertCustomizationData(ObjectKind objectKind)
    {
        if (PlayerCharacter == IntPtr.Zero) return;

        var cancelToken = new CancellationTokenSource();
        cancelToken.CancelAfter(TimeSpan.FromSeconds(10));

        if (objectKind == ObjectKind.Player)
        {
            Logger.Debug($"Restoring Customization for {OnlineUser.User.AliasOrUID}/{PlayerName}: {_originalGlamourerData}");
            await _ipcManager.GlamourerApplyOnlyCustomization(_originalGlamourerData, _currentOtherChara!, cancelToken.Token, fireAndForget: true).ConfigureAwait(false);
            Logger.Debug($"Restoring Equipment for {OnlineUser.User.AliasOrUID}/{PlayerName}: {_lastGlamourerData}");
            await _ipcManager.GlamourerApplyOnlyEquipment(_lastGlamourerData, _currentOtherChara!, cancelToken.Token, fireAndForget: true).ConfigureAwait(false);
            Logger.Debug($"Restoring Heels for {OnlineUser.User.AliasOrUID}/{PlayerName}");
            _ipcManager.HeelsRestoreOffsetForPlayer(PlayerCharacter);
            Logger.Debug($"Restoring C+ for {OnlineUser.User.AliasOrUID}/{PlayerName}");
            _ipcManager.CustomizePlusRevert(PlayerCharacter);
            _ipcManager.PalettePlusRemovePalette(PlayerCharacter);
        }
        else if (objectKind == ObjectKind.MinionOrMount)
        {
            var minionOrMount = _dalamudUtil.GetMinionOrMount(PlayerCharacter);
            if (minionOrMount != null)
            {
                using GameObjectHandler tempHandler = new(Mediator, ObjectKind.MinionOrMount, () => minionOrMount.Value,
                    false);
                await _ipcManager.PenumbraRedraw(tempHandler, cancelToken.Token, fireAndForget: true).ConfigureAwait(false);
            }
        }
        else if (objectKind == ObjectKind.Pet)
        {
            var pet = _dalamudUtil.GetPet(PlayerCharacter);
            if (pet != IntPtr.Zero)
            {
                using GameObjectHandler tempHandler = new(Mediator, ObjectKind.Pet, () => pet,
                    false);
                await _ipcManager.PenumbraRedraw(tempHandler, cancelToken.Token, fireAndForget: true).ConfigureAwait(false);
            }
        }
        else if (objectKind == ObjectKind.Companion)
        {
            var companion = _dalamudUtil.GetCompanion(PlayerCharacter);
            if (companion != IntPtr.Zero)
            {
                using GameObjectHandler tempHandler = new(Mediator, ObjectKind.Pet, () => companion,
                    false);
                await _ipcManager.PenumbraRedraw(tempHandler, cancelToken.Token, fireAndForget: true).ConfigureAwait(false);
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
                        Logger.Verbose("Missing file: " + item.Hash);
                        missingFiles.Add(item);
                    }
                }
            }

            foreach (var item in _cachedData.FileReplacements.SelectMany(k => k.Value.Where(v => !string.IsNullOrEmpty(v.FileSwapPath))).ToList())
            {
                foreach (var gamePath in item.GamePaths)
                {
                    Logger.Verbose("Adding file swap for " + gamePath + ":" + item.FileSwapPath);
                    moddedDictionary[gamePath] = item.FileSwapPath;
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Something went wrong during calculation replacements");
        }
        Logger.Debug("ModdedPaths calculated, missing files: " + missingFiles.Count);
        return missingFiles;
    }
}