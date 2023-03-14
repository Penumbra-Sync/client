using Dalamud.Interface.Internal.Notifications;
using Dalamud.Logging;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.User;
using MareSynchronos.FileCache;
using MareSynchronos.Interop;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Pairs;

public sealed class CachedPlayer : DisposableMediatorSubscriberBase
{
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileDownloadManager _downloadManager;
    private readonly FileCacheManager _fileDbManager;
    private readonly Func<ObjectKind, Func<nint>, bool, GameObjectHandler> _gameObjectHandlerFactory;
    private readonly IpcManager _ipcManager;
    private readonly IHostApplicationLifetime _lifetime;
    private CancellationTokenSource _applicationCancellationTokenSource = new();
    private Guid _applicationId;
    private Task? _applicationTask;
    private CharacterData _cachedData = new();
    private GameObjectHandler? _charaHandler;
    private CancellationTokenSource? _downloadCancellationTokenSource = new();
    private string _lastGlamourerData = string.Empty;
    private string _originalGlamourerData = string.Empty;

    private CancellationTokenSource _redrawCts = new();

    public CachedPlayer(ILogger<CachedPlayer> logger, OnlineUserIdentDto onlineUser,
            Func<ObjectKind, Func<nint>, bool, GameObjectHandler> gameObjectHandlerFactory,
        IpcManager ipcManager, FileDownloadManager transferManager,
        DalamudUtilService dalamudUtil, IHostApplicationLifetime lifetime, FileCacheManager fileDbManager, MareMediator mediator) : base(logger, mediator)
    {
        OnlineUser = onlineUser;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _ipcManager = ipcManager;
        _downloadManager = transferManager;
        _dalamudUtil = dalamudUtil;
        _lifetime = lifetime;
        _fileDbManager = fileDbManager;
    }

    private enum PlayerChanges
    {
        Heels = 1,
        Customize = 2,
        Palette = 3,
        Mods = 4
    }

    public string? PlayerName { get; private set; }
    public string PlayerNameHash => OnlineUser.Ident;
    private OnlineUserIdentDto OnlineUser { get; set; }
    private IntPtr PlayerCharacter => _charaHandler?.Address ?? IntPtr.Zero;

    public void ApplyCharacterData(CharacterData characterData, OptionalPluginWarning warning, bool forced = false)
    {
        SetUploading(false);

        Logger.LogDebug("Received data for {player}", this);

        Logger.LogDebug("Checking for files to download for player {name}", this);
        Logger.LogDebug("Hash for data is {newHash}, current cache hash is {oldHash}", characterData.DataHash.Value, _cachedData.DataHash.Value);

        if (!_ipcManager.CheckPenumbraApi()) return;
        if (!_ipcManager.CheckGlamourerApi()) return;

        if (string.Equals(characterData.DataHash.Value, _cachedData.DataHash.Value, StringComparison.Ordinal) && !forced) return;

        var charaDataToUpdate = CheckUpdatedData(_cachedData.DeepClone(), characterData, forced);

        if (charaDataToUpdate.TryGetValue(ObjectKind.Player, out var playerChanges))
        {
            NotifyForMissingPlugins(playerChanges, warning);
        }

        Logger.LogDebug("Downloading and applying character for {name}", this);

        DownloadAndApplyCharacter(characterData, charaDataToUpdate);

        _cachedData = characterData;
    }

    public bool CheckExistence()
    {
        if (PlayerName == null || _charaHandler == null
            || !string.Equals(PlayerName, _charaHandler.Name, StringComparison.Ordinal)
            || _charaHandler.CurrentAddress == IntPtr.Zero)
        {
            return false;
        }

        return true;
    }

    public void Initialize(string name)
    {
        PlayerName = name;
        _charaHandler = _gameObjectHandlerFactory(ObjectKind.Player, () => _dalamudUtil.GetPlayerCharacterFromObjectTableByName(PlayerName)?.Address ?? IntPtr.Zero, false);

        _originalGlamourerData = _ipcManager.GlamourerGetCharacterCustomization(PlayerCharacter);
        _lastGlamourerData = _originalGlamourerData;
        Mediator.Subscribe<PenumbraRedrawMessage>(this, IpcManagerOnPenumbraRedrawEvent);
        Mediator.Subscribe<CharacterChangedMessage>(this, (msg) =>
        {
            if (msg.GameObjectHandler == _charaHandler && (_applicationTask?.IsCompleted ?? true))
            {
                Logger.LogTrace("Saving new Glamourer Data for {this}", this);
                _lastGlamourerData = _ipcManager.GlamourerGetCharacterCustomization(PlayerCharacter);
            }
        });

        Logger.LogDebug("Initializing Player {obj}", this);
    }

    public override string ToString()
    {
        return OnlineUser == null
            ? (base.ToString() ?? string.Empty)
            : (OnlineUser.User.AliasOrUID + ":" + PlayerName + ":" + (PlayerCharacter != IntPtr.Zero ? "HasChar" : "NoChar"));
    }

    internal void SetUploading(bool isUploading = true)
    {
        Logger.LogTrace("Setting {this} uploading {uploading}", this, isUploading);
        if (_charaHandler != null)
        {
            Mediator.Publish(new PlayerUploadingMessage(_charaHandler, isUploading));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (string.IsNullOrEmpty(PlayerName)) return; // already disposed

        base.Dispose(disposing);

        SetUploading(false);
        _downloadManager.Dispose();
        var name = PlayerName;
        Logger.LogDebug("Disposing {name} ({user})", name, OnlineUser);
        try
        {
            Guid applicationId = Guid.NewGuid();
            _applicationCancellationTokenSource.Cancel();
            _applicationCancellationTokenSource.Dispose();
            _downloadCancellationTokenSource?.Cancel();
            _downloadCancellationTokenSource?.Dispose();
            _downloadCancellationTokenSource = null;
            nint ptr = PlayerCharacter;
            _charaHandler?.Dispose();
            _charaHandler = null;
            if (!_lifetime.ApplicationStopping.IsCancellationRequested && ptr != IntPtr.Zero && !_dalamudUtil.IsZoning)
            {
                Logger.LogTrace("[{applicationId}] Restoring state for {name} ({OnlineUser})", applicationId, name, OnlineUser);
                _ipcManager.PenumbraRemoveTemporaryCollection(Logger, applicationId, name);

                foreach (KeyValuePair<ObjectKind, List<FileReplacementData>> item in _cachedData.FileReplacements)
                {
                    RevertCustomizationData(ptr, item.Key, name, applicationId).GetAwaiter().GetResult();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error on disposal of {name}", name);
        }
        finally
        {
            PlayerName = null;
            _cachedData = new();
            Logger.LogDebug("Disposing {name} complete", name);
        }
    }

    private async Task ApplyBaseData(Guid applicationId, Dictionary<string, string> moddedPaths, string manipulationData, CancellationToken token)
    {
        await _dalamudUtil.RunOnFrameworkThread(() => _ipcManager.PenumbraRemoveTemporaryCollection(Logger, applicationId, PlayerName!)).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        await _dalamudUtil.RunOnFrameworkThread(() => _ipcManager.PenumbraSetTemporaryMods(Logger, applicationId, PlayerName!, moddedPaths, manipulationData)).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }

    private async Task ApplyCustomizationData(Guid applicationId, KeyValuePair<ObjectKind, HashSet<PlayerChanges>> changes, CharacterData charaData, CancellationToken token)
    {
        if (PlayerCharacter == IntPtr.Zero) return;
        var ptr = PlayerCharacter;

        var handler = changes.Key switch
        {
            ObjectKind.Player => _charaHandler!,
            ObjectKind.Companion => _gameObjectHandlerFactory(changes.Key, () => _dalamudUtil.GetCompanion(ptr), false),
            ObjectKind.MinionOrMount => _gameObjectHandlerFactory(changes.Key, () => _dalamudUtil.GetMinionOrMount(ptr), false),
            ObjectKind.Pet => _gameObjectHandlerFactory(changes.Key, () => _dalamudUtil.GetPet(ptr), false),
            _ => throw new NotSupportedException("ObjectKind not supported: " + changes.Key)
        };

        try
        {
            if (handler.Address == IntPtr.Zero)
            {
                return;
            }

            Logger.LogDebug("[{applicationId}] Applying Customization Data for {handler}", applicationId, handler);
            await _dalamudUtil.WaitWhileCharacterIsDrawing(Logger, handler, applicationId, 30000, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            foreach (var change in changes.Value)
            {
                Logger.LogDebug("[{applicationId}] Processing {change} for {handler}", applicationId, change, handler);
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
                            await _ipcManager.GlamourerApplyAll(Logger, handler, glamourerData, applicationId, token).ConfigureAwait(false);
                        }
                        else
                        {
                            await _ipcManager.PenumbraRedraw(Logger, handler, applicationId, token).ConfigureAwait(false);
                        }
                        break;
                }
                token.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            if (handler != _charaHandler) handler.Dispose();
        }
    }

    private Dictionary<ObjectKind, HashSet<PlayerChanges>> CheckUpdatedData(CharacterData oldData, CharacterData newData, bool forced)
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
                Logger.LogDebug("Updating {object}/{kind} (Some new data arrived: NewButNotOldFiles:{hasNewButNotOldFileReplacements}," +
                    " OldButNotNewFiles:{hasOldButNotNewFileReplacements}, NewButNotOldGlam:{hasNewButNotOldGlamourerData}, OldButNotNewGlam:{hasOldButNotNewGlamourerData}) => {change}",
                    this, objectKind, hasNewButNotOldFileReplacements, hasOldButNotNewFileReplacements, hasNewButNotOldGlamourerData, hasOldButNotNewGlamourerData, PlayerChanges.Mods);
                charaDataToUpdate[objectKind].Add(PlayerChanges.Mods);
            }
            else
            {
                if (hasNewAndOldFileReplacements)
                {
                    bool listsAreEqual = oldData.FileReplacements[objectKind].SequenceEqual(newData.FileReplacements[objectKind], Data.FileReplacementDataComparer.Instance);
                    if (!listsAreEqual || forced)
                    {
                        Logger.LogDebug("Updating {object}/{kind} (FileReplacements not equal) => {change}", this, objectKind, PlayerChanges.Mods);
                        charaDataToUpdate[objectKind].Add(PlayerChanges.Mods);
                    }
                }

                if (hasNewAndOldGlamourerData)
                {
                    bool glamourerDataDifferent = !string.Equals(oldData.GlamourerData[objectKind], newData.GlamourerData[objectKind], StringComparison.Ordinal);
                    if (glamourerDataDifferent || forced)
                    {
                        Logger.LogDebug("Updating {object}/{kind} (Glamourer different) => {change}", this, objectKind, PlayerChanges.Mods);
                        charaDataToUpdate[objectKind].Add(PlayerChanges.Mods);
                    }
                }
            }

            if (objectKind != ObjectKind.Player) continue;

            bool manipDataDifferent = !string.Equals(oldData.ManipulationData, newData.ManipulationData, StringComparison.Ordinal);
            if (manipDataDifferent || forced)
            {
                Logger.LogDebug("Updating {object}/{kind} (Diff manip data) => {change}", this, objectKind, PlayerChanges.Mods);
                charaDataToUpdate[objectKind].Add(PlayerChanges.Mods);
            }

            bool heelsOffsetDifferent = oldData.HeelsOffset != newData.HeelsOffset;
            if (heelsOffsetDifferent || forced)
            {
                Logger.LogDebug("Updating {object}/{kind} (Diff heels data) => {change}", this, objectKind, PlayerChanges.Heels);
                charaDataToUpdate[objectKind].Add(PlayerChanges.Heels);
            }

            bool customizeDataDifferent = !string.Equals(oldData.CustomizePlusData, newData.CustomizePlusData, StringComparison.Ordinal);
            if (customizeDataDifferent || forced)
            {
                Logger.LogDebug("Updating {object}/{kind} (Diff customize data) => {change}", this, objectKind, PlayerChanges.Customize);
                charaDataToUpdate[objectKind].Add(PlayerChanges.Customize);
            }

            bool palettePlusDataDifferent = !string.Equals(oldData.PalettePlusData, newData.PalettePlusData, StringComparison.Ordinal);
            if (palettePlusDataDifferent || forced)
            {
                Logger.LogDebug("Updating {object}/{kind} (Diff palette data) => {change}", this, objectKind, PlayerChanges.Palette);
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

    private void DownloadAndApplyCharacter(CharacterData charaData, Dictionary<ObjectKind, HashSet<PlayerChanges>> updatedData)
    {
        if (!updatedData.Any())
        {
            Logger.LogDebug("Nothing to update for {obj}", this);
        }

        var updateModdedPaths = updatedData.Values.Any(v => v.Any(p => p == PlayerChanges.Mods));

        _downloadCancellationTokenSource?.Cancel();
        _downloadCancellationTokenSource?.Dispose();
        _downloadCancellationTokenSource = new CancellationTokenSource();
        var downloadToken = _downloadCancellationTokenSource.Token;

        Task.Run(async () =>
        {
            List<FileReplacementData> toDownloadReplacements;

            Dictionary<string, string> moddedPaths = new(StringComparer.Ordinal);

            if (updateModdedPaths)
            {
                int attempts = 0;
                while ((toDownloadReplacements = TryCalculateModdedDictionary(charaData, out moddedPaths)).Count > 0 && attempts++ <= 10)
                {
                    _downloadManager.CancelDownload();
                    Logger.LogDebug("Downloading missing files for player {name}, {kind}", PlayerName, updatedData);
                    if (toDownloadReplacements.Any())
                    {
                        await _downloadManager.DownloadFiles(_charaHandler, toDownloadReplacements, downloadToken).ConfigureAwait(false);
                        _downloadManager.CancelDownload();
                    }

                    if (downloadToken.IsCancellationRequested)
                    {
                        Logger.LogTrace("Detected cancellation");
                        _downloadManager.CancelDownload();
                        return;
                    }

                    if (TryCalculateModdedDictionary(charaData, out moddedPaths).All(c => _downloadManager.ForbiddenTransfers.Any(f => string.Equals(f.Hash, c.Hash, StringComparison.Ordinal))))
                    {
                        break;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
            }

            while ((!_applicationTask?.IsCompleted ?? false) && !downloadToken.IsCancellationRequested && !_applicationCancellationTokenSource.IsCancellationRequested)
            {
                // block until current application is done
                Logger.LogDebug("Waiting for current data application (Id: {id}) to finish", _applicationId);
                await Task.Delay(250).ConfigureAwait(false);
            }

            if (downloadToken.IsCancellationRequested || _applicationCancellationTokenSource.IsCancellationRequested) return;

            _applicationCancellationTokenSource?.Dispose();
            _applicationCancellationTokenSource = new();
            var token = _applicationCancellationTokenSource.Token;
            _applicationTask = Task.Run(async () =>
            {
                _applicationId = Guid.NewGuid();
                Logger.LogDebug("[{applicationId}] Starting application task", _applicationId);

                if (updateModdedPaths && (moddedPaths.Any() || !string.IsNullOrEmpty(charaData.ManipulationData)))
                {
                    await ApplyBaseData(_applicationId, moddedPaths, charaData.ManipulationData, token).ConfigureAwait(false);
                }

                token.ThrowIfCancellationRequested();

                foreach (var kind in updatedData)
                {
                    await ApplyCustomizationData(_applicationId, kind, charaData, token).ConfigureAwait(false);
                }

                Logger.LogDebug("[{applicationId}] Application finished", _applicationId);
            }, token);
        }, downloadToken);
    }

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
            await _dalamudUtil.WaitWhileCharacterIsDrawing(Logger, _charaHandler!, applicationId, ct: token).ConfigureAwait(false);
            Logger.LogDebug("Unauthorized character change detected");
            await ApplyCustomizationData(applicationId, new(ObjectKind.Player,
                new HashSet<PlayerChanges>(new[] { PlayerChanges.Palette, PlayerChanges.Customize, PlayerChanges.Heels, PlayerChanges.Mods })),
                _cachedData, _applicationCancellationTokenSource.Token).ConfigureAwait(false);
        }, token);
    }

    private void NotifyForMissingPlugins(HashSet<PlayerChanges> changes, OptionalPluginWarning warning)
    {
        List<string> missingPluginsForData = new();
        if (changes.Contains(PlayerChanges.Heels) && !warning.ShownHeelsWarning && !_ipcManager.CheckHeelsApi())
        {
            missingPluginsForData.Add("Heels");
            warning.ShownHeelsWarning = true;
        }
        if (changes.Contains(PlayerChanges.Customize) && !warning.ShownCustomizePlusWarning && !_ipcManager.CheckCustomizePlusApi())
        {
            missingPluginsForData.Add("Customize+");
            warning.ShownCustomizePlusWarning = true;
        }

        if (changes.Contains(PlayerChanges.Palette) && !warning.ShownPalettePlusWarning && !_ipcManager.CheckPalettePlusApi())
        {
            missingPluginsForData.Add("Palette+");
            warning.ShownPalettePlusWarning = true;
        }

        if (missingPluginsForData.Any())
        {
            Mediator.Publish(new NotificationMessage("Missing plugins for " + PlayerName,
                $"Received data for {PlayerName} that contained information for plugins you have not installed. Install {string.Join(", ", missingPluginsForData)} to experience their character fully.",
                NotificationType.Warning, 10000));
        }
    }

    private async Task RevertCustomizationData(IntPtr address, ObjectKind objectKind, string name, Guid applicationId)
    {
        if (address == IntPtr.Zero) return;

        var cancelToken = new CancellationTokenSource();
        cancelToken.CancelAfter(TimeSpan.FromSeconds(10));

        Logger.LogDebug("[{applicationId}] Reverting all Customization for {alias}/{name} {objectKind}", applicationId, OnlineUser.User.AliasOrUID, name, objectKind);

        if (objectKind == ObjectKind.Player)
        {
            using GameObjectHandler tempHandler = _gameObjectHandlerFactory(ObjectKind.Player, () => address, false);
            Logger.LogDebug("[{applicationId}] Restoring Customization for {alias}/{name}: {data}", applicationId, OnlineUser.User.AliasOrUID, name, _originalGlamourerData);
            await _ipcManager.GlamourerApplyOnlyCustomization(Logger, tempHandler, _originalGlamourerData, applicationId, cancelToken.Token, fireAndForget: false).ConfigureAwait(false);
            Logger.LogDebug("[{applicationId}] Restoring Equipment for {alias}/{name}: {data}", applicationId, OnlineUser.User.AliasOrUID, name, _lastGlamourerData);
            await _ipcManager.GlamourerApplyOnlyEquipment(Logger, tempHandler, _lastGlamourerData, applicationId, cancelToken.Token, fireAndForget: false).ConfigureAwait(false);
            Logger.LogDebug("[{applicationId}] Restoring Heels for {alias}/{name}", applicationId, OnlineUser.User.AliasOrUID, name);
            await _ipcManager.HeelsRestoreOffsetForPlayer(address).ConfigureAwait(false);
            Logger.LogDebug("[{applicationId}] Restoring C+ for {alias}/{name}", applicationId, OnlineUser.User.AliasOrUID, name);
            await _ipcManager.CustomizePlusRevert(address).ConfigureAwait(false);
            Logger.LogDebug("[{applicationId}] Restoring Palette+ for {alias}/{name}", applicationId, OnlineUser.User.AliasOrUID, name);
            await _ipcManager.PalettePlusRemovePalette(address).ConfigureAwait(false);
        }
        else if (objectKind == ObjectKind.MinionOrMount)
        {
            var minionOrMount = _dalamudUtil.GetMinionOrMount(address);
            if (minionOrMount != IntPtr.Zero)
            {
                using GameObjectHandler tempHandler = _gameObjectHandlerFactory(ObjectKind.MinionOrMount, () => minionOrMount, false);
                await _ipcManager.PenumbraRedraw(Logger, tempHandler, applicationId, cancelToken.Token, fireAndForget: false).ConfigureAwait(false);
            }
        }
        else if (objectKind == ObjectKind.Pet)
        {
            var pet = _dalamudUtil.GetPet(address);
            if (pet != IntPtr.Zero)
            {
                using GameObjectHandler tempHandler = _gameObjectHandlerFactory(ObjectKind.Pet, () => pet, false);
                await _ipcManager.PenumbraRedraw(Logger, tempHandler, applicationId, cancelToken.Token, fireAndForget: false).ConfigureAwait(false);
            }
        }
        else if (objectKind == ObjectKind.Companion)
        {
            var companion = _dalamudUtil.GetCompanion(address);
            if (companion != IntPtr.Zero)
            {
                using GameObjectHandler tempHandler = _gameObjectHandlerFactory(ObjectKind.Pet, () => companion, false);
                await _ipcManager.PenumbraRedraw(Logger, tempHandler, applicationId, cancelToken.Token, fireAndForget: false).ConfigureAwait(false);
            }
        }
    }

    private List<FileReplacementData> TryCalculateModdedDictionary(CharacterData charaData, out Dictionary<string, string> moddedDictionary)
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
                        Logger.LogTrace("Missing file: {hash}", item.Hash);
                        missingFiles.Add(item);
                    }
                }
            }

            foreach (var item in charaData.FileReplacements.SelectMany(k => k.Value.Where(v => !string.IsNullOrEmpty(v.FileSwapPath))).ToList())
            {
                foreach (var gamePath in item.GamePaths)
                {
                    Logger.LogTrace("Adding file swap for {path}: {fileSwap}", gamePath, item.FileSwapPath);
                    moddedDictionary[gamePath] = item.FileSwapPath;
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Something went wrong during calculation replacements");
        }
        Logger.LogDebug("ModdedPaths calculated, missing files: {count}", missingFiles.Count);
        return missingFiles;
    }
}