using Dalamud.Logging;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.User;
using MareSynchronos.FileCache;
using MareSynchronos.Interop;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using ObjectKind = MareSynchronos.API.Data.Enum.ObjectKind;

namespace MareSynchronos.PlayerData.Handlers;

public sealed class PairHandler : DisposableMediatorSubscriberBase
{
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileDownloadManager _downloadManager;
    private readonly FileCacheManager _fileDbManager;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly IpcManager _ipcManager;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly PluginWarningNotificationService _pluginWarningNotificationManager;
    private CancellationTokenSource? _applicationCancellationTokenSource = new();
    private Guid _applicationId;
    private Task? _applicationTask;
    private CharacterData? _cachedData = null;
    private GameObjectHandler? _charaHandler;
    private CancellationTokenSource? _downloadCancellationTokenSource = new();
    private bool _forceApplyMods = false;
    private string _lastGlamourerData = string.Empty;
    private string _originalGlamourerData = string.Empty;
    private string _penumbraCollection;
    private CancellationTokenSource _redrawCts = new();

    public PairHandler(ILogger<PairHandler> logger, OnlineUserIdentDto onlineUser,
        GameObjectHandlerFactory gameObjectHandlerFactory,
        IpcManager ipcManager, FileDownloadManager transferManager,
        PluginWarningNotificationService pluginWarningNotificationManager,
        DalamudUtilService dalamudUtil, IHostApplicationLifetime lifetime,
        FileCacheManager fileDbManager, MareMediator mediator) : base(logger, mediator)
    {
        OnlineUser = onlineUser;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _ipcManager = ipcManager;
        _downloadManager = transferManager;
        _pluginWarningNotificationManager = pluginWarningNotificationManager;
        _dalamudUtil = dalamudUtil;
        _lifetime = lifetime;
        _fileDbManager = fileDbManager;

        _penumbraCollection = _ipcManager.PenumbraCreateTemporaryCollectionAsync(logger, OnlineUser.User.UID).ConfigureAwait(false).GetAwaiter().GetResult();

        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => FrameworkUpdate());
        Mediator.Subscribe<ZoneSwitchStartMessage>(this, (_) =>
        {
            MediatorUnsubscribeFromCharacterChanged();
            _charaHandler?.Invalidate();
            IsVisible = false;
        });
        Mediator.Subscribe<PenumbraInitializedMessage>(this, (_) =>
        {
            _penumbraCollection = _ipcManager.PenumbraCreateTemporaryCollectionAsync(logger, OnlineUser.User.UID).ConfigureAwait(false).GetAwaiter().GetResult();
            if (!IsVisible && _charaHandler != null)
            {
                PlayerName = string.Empty;
                _charaHandler.Dispose();
                _charaHandler = null;
            }
        });
    }

    public bool IsVisible { get; private set; }
    public OnlineUserIdentDto OnlineUser { get; private set; }
    public nint PlayerCharacter => _charaHandler?.Address ?? nint.Zero;
    public unsafe uint PlayerCharacterId => (_charaHandler?.Address ?? nint.Zero) == nint.Zero
        ? uint.MaxValue
        : ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)_charaHandler!.Address)->ObjectID;
    public string? PlayerName { get; private set; }
    public string PlayerNameHash => OnlineUser.Ident;

    public void ApplyCharacterData(Guid applicationBase, CharacterData characterData, bool forceApplyCustomization = false)
    {
        if (_charaHandler == null || (PlayerCharacter == IntPtr.Zero))
        {
            Logger.LogDebug("[BASE-{appBase}] Received data but player was in invalid state, charaHandlerIsNull: {charaIsNull}, playerPointerIsNull: {ptrIsNull}",
                applicationBase, _charaHandler == null, PlayerCharacter == IntPtr.Zero);
            var hasDiffMods = characterData.CheckUpdatedData(applicationBase, _cachedData, Logger,
                this, forceApplyCustomization, false).Any(p => p.Value.Contains(PlayerChanges.ModManip) || p.Value.Contains(PlayerChanges.ModFiles));
            _forceApplyMods = hasDiffMods || _forceApplyMods || (PlayerCharacter == IntPtr.Zero && _cachedData == null);
            _cachedData = characterData;
            Logger.LogDebug("[BASE-{appBase}] Setting data: {hash}, forceApplyMods: {force}", applicationBase, _cachedData.DataHash.Value, _forceApplyMods);
            return;
        }

        SetUploading(false);

        Logger.LogDebug("[BASE-{appbase}] Applying data for {player}, forceApplyCustomization: {forced}, forceApplyMods: {forceMods}", applicationBase, this, forceApplyCustomization, _forceApplyMods);
        Logger.LogDebug("[BASE-{appbase}] Hash for data is {newHash}, current cache hash is {oldHash}", applicationBase, characterData.DataHash.Value, _cachedData?.DataHash.Value ?? "NODATA");

        if (string.Equals(characterData.DataHash.Value, _cachedData?.DataHash.Value ?? string.Empty, StringComparison.Ordinal) && !forceApplyCustomization) return;

        if (_dalamudUtil.IsInCutscene || _dalamudUtil.IsInGpose || !_ipcManager.CheckPenumbraApi() || !_ipcManager.CheckGlamourerApi())
        {
            Logger.LogInformation("[BASE-{appbase}] Application of data for {player} while in cutscene/gpose or Penumbra/Glamourer unavailable, returning", applicationBase, this);
            return;
        }

        var charaDataToUpdate = characterData.CheckUpdatedData(applicationBase, _cachedData?.DeepClone() ?? new(), Logger, this, forceApplyCustomization, _forceApplyMods);

        if (_charaHandler != null && _forceApplyMods)
        {
            _forceApplyMods = false;
        }

        if (charaDataToUpdate.TryGetValue(ObjectKind.Player, out var playerChanges))
        {
            _pluginWarningNotificationManager.NotifyForMissingPlugins(OnlineUser.User, PlayerName!, playerChanges);
        }

        Logger.LogDebug("[BASE-{appbase}] Downloading and applying character for {name}", applicationBase, this);

        DownloadAndApplyCharacter(applicationBase, characterData.DeepClone(), charaDataToUpdate);
    }

    public override string ToString()
    {
        return OnlineUser == null
            ? base.ToString() ?? string.Empty
            : OnlineUser.User.AliasOrUID + ":" + PlayerName + ":" + (PlayerCharacter != nint.Zero ? "HasChar" : "NoChar");
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
        base.Dispose(disposing);

        SetUploading(false);
        _downloadManager.Dispose();
        var name = PlayerName;
        Logger.LogDebug("Disposing {name} ({user})", name, OnlineUser);
        try
        {
            Guid applicationId = Guid.NewGuid();
            _applicationCancellationTokenSource?.CancelDispose();
            _applicationCancellationTokenSource = null;
            _downloadCancellationTokenSource?.CancelDispose();
            _downloadCancellationTokenSource = null;
            _charaHandler?.Dispose();
            _charaHandler = null;

            if (_lifetime.ApplicationStopping.IsCancellationRequested) return;

            if (_dalamudUtil is { IsZoning: false, IsInCutscene: false })
            {
                Logger.LogTrace("[{applicationId}] Restoring state for {name} ({OnlineUser})", applicationId, name, OnlineUser);
                _ipcManager.PenumbraRemoveTemporaryCollectionAsync(Logger, applicationId, _penumbraCollection).GetAwaiter().GetResult();

                foreach (KeyValuePair<ObjectKind, List<FileReplacementData>> item in _cachedData?.FileReplacements ?? new())
                {
                    try
                    {
                        RevertCustomizationDataAsync(item.Key, name, applicationId).GetAwaiter().GetResult();
                    }
                    catch (InvalidOperationException ex)
                    {
                        Logger.LogWarning(ex, "Failed disposing player (not present anymore?)");
                        break;
                    }
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
            _cachedData = null;
            Logger.LogDebug("Disposing {name} complete", name);
        }
    }

    private async Task ApplyCustomizationDataAsync(Guid applicationId, KeyValuePair<ObjectKind, HashSet<PlayerChanges>> changes, CharacterData charaData, CancellationToken token)
    {
        if (PlayerCharacter == nint.Zero) return;
        var ptr = PlayerCharacter;

        var handler = changes.Key switch
        {
            ObjectKind.Player => _charaHandler!,
            ObjectKind.Companion => await _gameObjectHandlerFactory.Create(changes.Key, () => _dalamudUtil.GetCompanion(ptr), false).ConfigureAwait(false),
            ObjectKind.MinionOrMount => await _gameObjectHandlerFactory.Create(changes.Key, () => _dalamudUtil.GetMinionOrMount(ptr), false).ConfigureAwait(false),
            ObjectKind.Pet => await _gameObjectHandlerFactory.Create(changes.Key, () => _dalamudUtil.GetPet(ptr), false).ConfigureAwait(false),
            _ => throw new NotSupportedException("ObjectKind not supported: " + changes.Key)
        };

        try
        {
            if (handler.Address == nint.Zero)
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
                        await _ipcManager.PalettePlusSetPaletteAsync(handler.Address, charaData.PalettePlusData).ConfigureAwait(false);
                        break;

                    case PlayerChanges.Customize:
                        if (charaData.CustomizePlusData.TryGetValue(changes.Key, out var customizePlusData))
                        {
                            await _ipcManager.CustomizePlusSetBodyScaleAsync(handler.Address, customizePlusData).ConfigureAwait(false);
                        }
                        else
                        {
                            await _ipcManager.CustomizePlusRevertAsync(handler.Address).ConfigureAwait(false);
                        }
                        break;

                    case PlayerChanges.Heels:
                        await _ipcManager.HeelsSetOffsetForPlayerAsync(handler.Address, charaData.HeelsData).ConfigureAwait(false);
                        break;

                    case PlayerChanges.Honorific:
                        await _ipcManager.HonorificSetTitleAsync(handler.Address, charaData.HonorificData).ConfigureAwait(false);
                        break;

                    case PlayerChanges.Glamourer:
                        if (charaData.GlamourerData.TryGetValue(changes.Key, out var glamourerData))
                        {
                            await _ipcManager.GlamourerApplyAllAsync(Logger, handler, glamourerData, applicationId, token).ConfigureAwait(false);
                        }
                        break;

                    case PlayerChanges.ModFiles:
                    case PlayerChanges.ModManip:
                        if (!changes.Value.Contains(PlayerChanges.Glamourer))
                        {
                            await _ipcManager.PenumbraRedrawAsync(Logger, handler, applicationId, token).ConfigureAwait(false);
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

    private void DownloadAndApplyCharacter(Guid applicationBase, CharacterData charaData, Dictionary<ObjectKind, HashSet<PlayerChanges>> updatedData)
    {
        if (!updatedData.Any())
        {
            Logger.LogDebug("[BASE-{appBase}] Nothing to update for {obj}", applicationBase, this);
            return;
        }

        var updateModdedPaths = updatedData.Values.Any(v => v.Any(p => p == PlayerChanges.ModFiles));
        var updateManip = updatedData.Values.Any(v => v.Any(p => p == PlayerChanges.ModManip));

        _downloadCancellationTokenSource = _downloadCancellationTokenSource?.CancelRecreate() ?? new CancellationTokenSource();
        var downloadToken = _downloadCancellationTokenSource.Token;

        _ = Task.Run(async () =>
        {
            Dictionary<string, string> moddedPaths = new(StringComparer.Ordinal);

            if (updateModdedPaths)
            {
                int attempts = 0;
                List<FileReplacementData> toDownloadReplacements = TryCalculateModdedDictionary(applicationBase, charaData, out moddedPaths, downloadToken);

                while (toDownloadReplacements.Count > 0 && attempts++ <= 10 && !downloadToken.IsCancellationRequested)
                {
                    _downloadManager.CancelDownload();
                    Logger.LogDebug("[BASE-{appBase}] Downloading missing files for player {name}, {kind}", applicationBase, PlayerName, updatedData);
                    if (toDownloadReplacements.Any())
                    {
                        await _downloadManager.DownloadFiles(_charaHandler!, toDownloadReplacements, downloadToken).ConfigureAwait(false);
                        _downloadManager.CancelDownload();
                    }

                    if (downloadToken.IsCancellationRequested)
                    {
                        Logger.LogTrace("[BASE-{appBase}] Detected cancellation", applicationBase);
                        _downloadManager.CancelDownload();
                        return;
                    }

                    toDownloadReplacements = TryCalculateModdedDictionary(applicationBase, charaData, out moddedPaths, downloadToken);

                    if (toDownloadReplacements.All(c => _downloadManager.ForbiddenTransfers.Any(f => string.Equals(f.Hash, c.Hash, StringComparison.Ordinal))))
                    {
                        break;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
            }

            downloadToken.ThrowIfCancellationRequested();

            var appToken = _applicationCancellationTokenSource?.Token;
            while ((!_applicationTask?.IsCompleted ?? false)
                   && !downloadToken.IsCancellationRequested
                   && (!appToken?.IsCancellationRequested ?? false))
            {
                // block until current application is done
                Logger.LogDebug("[BASE-{appBase}] Waiting for current data application (Id: {id}) for player ({handler}) to finish", applicationBase, _applicationId, PlayerName);
                await Task.Delay(250).ConfigureAwait(false);
            }

            if (downloadToken.IsCancellationRequested || (appToken?.IsCancellationRequested ?? false)) return;

            _applicationCancellationTokenSource = _applicationCancellationTokenSource.CancelRecreate() ?? new CancellationTokenSource();
            var token = _applicationCancellationTokenSource.Token;
            _applicationTask = Task.Run(async () =>
            {
                try
                {
                    _applicationId = Guid.NewGuid();
                    Logger.LogDebug("[BASE-{applicationId}] Starting application task for {this}: {appId}", applicationBase, this, _applicationId);

                    Logger.LogDebug("[{applicationId}] Waiting for initial draw for for {handler}", _applicationId, _charaHandler);
                    await _dalamudUtil.WaitWhileCharacterIsDrawing(Logger, _charaHandler!, _applicationId, 30000, token).ConfigureAwait(false);

                    token.ThrowIfCancellationRequested();

                    if (updateModdedPaths)
                    {
                        await _ipcManager.PenumbraSetTemporaryModsAsync(Logger, _applicationId, _penumbraCollection, moddedPaths).ConfigureAwait(false);
                    }

                    if (updateManip)
                    {
                        await _ipcManager.PenumbraSetManipulationDataAsync(Logger, _applicationId, _penumbraCollection, charaData.ManipulationData).ConfigureAwait(false);
                    }

                    token.ThrowIfCancellationRequested();

                    foreach (var kind in updatedData)
                    {
                        await ApplyCustomizationDataAsync(_applicationId, kind, charaData, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                    }

                    _cachedData = charaData;

                    Logger.LogDebug("[{applicationId}] Application finished", _applicationId);
                }
                catch (Exception ex)
                {
                    if (ex is AggregateException aggr && aggr.InnerExceptions.Any(e => e is ArgumentNullException))
                    {
                        IsVisible = false;
                        _forceApplyMods = true;
                        _cachedData = charaData;
                        Logger.LogDebug("[{applicationId}] Cancelled, player turned null during application", _applicationId);
                    }
                    else
                    {
                        Logger.LogWarning(ex, "[{applicationId}] Cancelled", _applicationId);
                    }
                }
            }, token);
        }, downloadToken);
    }

    private void FrameworkUpdate()
    {
        if (string.IsNullOrEmpty(PlayerName))
        {
            var pc = _dalamudUtil.FindPlayerByNameHash(OnlineUser.Ident);
            if (pc == default((string, nint))) return;
            Logger.LogDebug("One-Time Initializing {this}", this);
            Initialize(pc.Name.ToString());
            Logger.LogDebug("One-Time Initialized {this}", this);
        }

        if (_charaHandler?.Address != nint.Zero && !IsVisible)
        {
            Guid appData = Guid.NewGuid();
            IsVisible = true;
            Mediator.Publish(new PairHandlerVisibleMessage(this));
            if (_cachedData != null)
            {
                Logger.LogTrace("[BASE-{appBase}] {this} visibility changed, now: {visi}, cached data exists", appData, this, IsVisible);

                _ = Task.Run(async () =>
                {
                    _lastGlamourerData = await _ipcManager.GlamourerGetCharacterCustomizationAsync(PlayerCharacter).ConfigureAwait(false);
                    ApplyCharacterData(appData, _cachedData!, true);
                });
            }
            else
            {
                Logger.LogTrace("{this} visibility changed, now: {visi}, no cached data exists", this, IsVisible);
            }

            MediatorSubscribeToCharacterChanged();
        }
        else if (_charaHandler?.Address == nint.Zero && IsVisible)
        {
            IsVisible = false;
            _charaHandler?.Invalidate();
            _downloadCancellationTokenSource?.CancelDispose();
            _downloadCancellationTokenSource = null;
            MediatorUnsubscribeFromCharacterChanged();
            Logger.LogTrace("{this} visibility changed, now: {visi}", this, IsVisible);
        }
    }

    private void Initialize(string name)
    {
        PlayerName = name;
        _charaHandler = _gameObjectHandlerFactory.Create(ObjectKind.Player, () => _dalamudUtil.GetPlayerCharacterFromCachedTableByIdent(OnlineUser.Ident), false).GetAwaiter().GetResult();

        _originalGlamourerData = _ipcManager.GlamourerGetCharacterCustomizationAsync(PlayerCharacter).ConfigureAwait(false).GetAwaiter().GetResult();
        _lastGlamourerData = _originalGlamourerData;
        Mediator.Subscribe<PenumbraRedrawMessage>(this, IpcManagerOnPenumbraRedrawEvent);
        Mediator.Subscribe<HonorificReadyMessage>(this, async (_) =>
        {
            if (string.IsNullOrEmpty(_cachedData?.HonorificData)) return;
            Logger.LogTrace("Reapplying Honorific data for {this}", this);
            await _ipcManager.HonorificSetTitleAsync(PlayerCharacter, _cachedData.HonorificData).ConfigureAwait(false);
        });

        _ipcManager.PenumbraAssignTemporaryCollectionAsync(Logger, _penumbraCollection, _charaHandler.GetGameObject()!.ObjectIndex).GetAwaiter().GetResult();

        _downloadManager.Initialize();
    }

    private void IpcManagerOnPenumbraRedrawEvent(PenumbraRedrawMessage msg)
    {
        var player = _dalamudUtil.GetCharacterFromObjectTableByIndex(msg.ObjTblIdx);
        if (player == null || !string.Equals(player.Name.ToString(), PlayerName, StringComparison.OrdinalIgnoreCase)) return;
        _redrawCts = _redrawCts.CancelRecreate();
        _redrawCts.CancelAfter(TimeSpan.FromSeconds(30));
        var token = _redrawCts.Token;

        _ = Task.Run(async () =>
        {
            var applicationId = Guid.NewGuid();
            await _dalamudUtil.WaitWhileCharacterIsDrawing(Logger, _charaHandler!, applicationId, ct: token).ConfigureAwait(false);
            Logger.LogDebug("Unauthorized character change detected");
            if (_cachedData != null)
            {
                await ApplyCustomizationDataAsync(applicationId, new(ObjectKind.Player,
                    new HashSet<PlayerChanges>(new[] { PlayerChanges.Palette, PlayerChanges.Customize, PlayerChanges.Heels, PlayerChanges.Glamourer })),
                    _cachedData, token).ConfigureAwait(false);
            }
        }, token);
    }

    private void MediatorSubscribeToCharacterChanged()
    {
        Mediator.Subscribe<CharacterChangedMessage>(this, (msg) =>
        {
            if (msg.GameObjectHandler == _charaHandler && (_applicationTask?.IsCompleted ?? true))
            {
                Guid appBase = Guid.NewGuid();
                var newGlamData = _ipcManager.GlamourerGetCharacterCustomizationAsync(PlayerCharacter).ConfigureAwait(false).GetAwaiter().GetResult();
                if (!string.Equals(_lastGlamourerData, newGlamData, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogTrace("[BASE-{appBase}] Saving new Glamourer Data for {this}", appBase, this);

                    _lastGlamourerData = newGlamData;
                    if (_cachedData != null)
                    {
                        ApplyCharacterData(appBase, _cachedData!, true);
                    }
                }
            }
        });
    }

    private void MediatorUnsubscribeFromCharacterChanged()
    {
        Mediator.Unsubscribe<CharacterChangedMessage>(this);
    }

    private async Task RevertCustomizationDataAsync(ObjectKind objectKind, string name, Guid applicationId)
    {
        nint address = _dalamudUtil.GetPlayerCharacterFromCachedTableByIdent(OnlineUser.Ident);
        if (address == nint.Zero) return;

        var cancelToken = new CancellationTokenSource();
        cancelToken.CancelAfter(TimeSpan.FromSeconds(60));

        Logger.LogDebug("[{applicationId}] Reverting all Customization for {alias}/{name} {objectKind}", applicationId, OnlineUser.User.AliasOrUID, name, objectKind);

        if (objectKind == ObjectKind.Player)
        {
            using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Player, () => address, false).ConfigureAwait(false);
            tempHandler.CompareNameAndThrow(name);
            Logger.LogDebug("[{applicationId}] Restoring Customization and Equipment for {alias}/{name}: {data}", applicationId, OnlineUser.User.AliasOrUID, name, _originalGlamourerData);
            await _ipcManager.GlamourerApplyCustomizationAndEquipmentAsync(Logger, tempHandler, _originalGlamourerData, _lastGlamourerData, applicationId, cancelToken.Token, fireAndForget: false).ConfigureAwait(false);
            tempHandler.CompareNameAndThrow(name);
            Logger.LogDebug("[{applicationId}] Restoring Heels for {alias}/{name}", applicationId, OnlineUser.User.AliasOrUID, name);
            await _ipcManager.HeelsRestoreOffsetForPlayerAsync(address).ConfigureAwait(false);
            tempHandler.CompareNameAndThrow(name);
            Logger.LogDebug("[{applicationId}] Restoring C+ for {alias}/{name}", applicationId, OnlineUser.User.AliasOrUID, name);
            await _ipcManager.CustomizePlusRevertAsync(address).ConfigureAwait(false);
            tempHandler.CompareNameAndThrow(name);
            Logger.LogDebug("[{applicationId}] Restoring Palette+ for {alias}/{name}", applicationId, OnlineUser.User.AliasOrUID, name);
            await _ipcManager.PalettePlusRemovePaletteAsync(address).ConfigureAwait(false);
            tempHandler.CompareNameAndThrow(name);
            Logger.LogDebug("[{applicationId}] Restoring Honorific for {alias}/{name}", applicationId, OnlineUser.User.AliasOrUID, name);
            await _ipcManager.HonorificClearTitleAsync(address).ConfigureAwait(false);
        }
        else if (objectKind == ObjectKind.MinionOrMount)
        {
            var minionOrMount = await _dalamudUtil.GetMinionOrMountAsync(address).ConfigureAwait(false);
            if (minionOrMount != nint.Zero)
            {
                await _ipcManager.CustomizePlusRevertAsync(minionOrMount).ConfigureAwait(false);
                using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.MinionOrMount, () => minionOrMount, false).ConfigureAwait(false);
                await _ipcManager.PenumbraRedrawAsync(Logger, tempHandler, applicationId, cancelToken.Token).ConfigureAwait(false);
            }
        }
        else if (objectKind == ObjectKind.Pet)
        {
            var pet = await _dalamudUtil.GetPetAsync(address).ConfigureAwait(false);
            if (pet != nint.Zero)
            {
                await _ipcManager.CustomizePlusRevertAsync(pet).ConfigureAwait(false);
                using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Pet, () => pet, false).ConfigureAwait(false);
                await _ipcManager.PenumbraRedrawAsync(Logger, tempHandler, applicationId, cancelToken.Token).ConfigureAwait(false);
            }
        }
        else if (objectKind == ObjectKind.Companion)
        {
            var companion = await _dalamudUtil.GetCompanionAsync(address).ConfigureAwait(false);
            if (companion != nint.Zero)
            {
                await _ipcManager.CustomizePlusRevertAsync(companion).ConfigureAwait(false);
                using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Pet, () => companion, false).ConfigureAwait(false);
                await _ipcManager.PenumbraRedrawAsync(Logger, tempHandler, applicationId, cancelToken.Token).ConfigureAwait(false);
            }
        }
    }

    private List<FileReplacementData> TryCalculateModdedDictionary(Guid applicationBase, CharacterData charaData, out Dictionary<string, string> moddedDictionary, CancellationToken token)
    {
        Stopwatch st = Stopwatch.StartNew();
        List<FileReplacementData> missingFiles = new();
        moddedDictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        ConcurrentDictionary<string, string> outputDict = new(StringComparer.Ordinal);
        bool hasMigrationChanges = false;
        try
        {
            var replacementList = charaData.FileReplacements.SelectMany(k => k.Value.Where(v => string.IsNullOrEmpty(v.FileSwapPath))).ToList();
            Parallel.ForEach(replacementList, new ParallelOptions()
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = 4
            },
            (item) =>
            {
                token.ThrowIfCancellationRequested();
                var fileCache = _fileDbManager.GetFileCacheByHash(item.Hash);
                if (fileCache != null)
                {
                    if (string.IsNullOrEmpty(new FileInfo(fileCache.ResolvedFilepath).Extension))
                    {
                        hasMigrationChanges = true;
                        fileCache = _fileDbManager.MigrateFileHashToExtension(fileCache, item.GamePaths[0].Split(".").Last());
                    }

                    foreach (var gamePath in item.GamePaths)
                    {
                        outputDict[gamePath] = fileCache.ResolvedFilepath;
                    }
                }
                else
                {
                    Logger.LogTrace("Missing file: {hash}", item.Hash);
                    missingFiles.Add(item);
                }
            });

            moddedDictionary = outputDict.ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal);

            foreach (var item in charaData.FileReplacements.SelectMany(k => k.Value.Where(v => !string.IsNullOrEmpty(v.FileSwapPath))).ToList())
            {
                foreach (var gamePath in item.GamePaths)
                {
                    Logger.LogTrace("[BASE-{appBase}] Adding file swap for {path}: {fileSwap}", applicationBase, gamePath, item.FileSwapPath);
                    moddedDictionary[gamePath] = item.FileSwapPath;
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "[BASE-{appBase}] Something went wrong during calculation replacements", applicationBase);
        }
        if (hasMigrationChanges) _fileDbManager.WriteOutFullCsv();
        st.Stop();
        Logger.LogDebug("[BASE-{appBase}] ModdedPaths calculated in {time}ms, missing files: {count}, total files: {total}", applicationBase, st.ElapsedMilliseconds, missingFiles.Count, moddedDictionary.Keys.Count);
        return missingFiles;
    }
}