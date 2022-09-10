using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using MareSynchronos.API;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Interop;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Newtonsoft.Json;

namespace MareSynchronos.Managers;

public class CachedPlayer
{
    private readonly DalamudUtil _dalamudUtil;
    private readonly IpcManager _ipcManager;
    private readonly ApiController _apiController;
    private bool _isVisible;

    public CachedPlayer(string nameHash, IpcManager ipcManager, ApiController apiController, DalamudUtil dalamudUtil)
    {
        PlayerNameHash = nameHash;
        _ipcManager = ipcManager;
        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            WasVisible = _isVisible;
            _isVisible = value;
        }
    }

    private bool _isDisposed = true;
    private CancellationTokenSource? _downloadCancellationTokenSource = new();

    private string _lastGlamourerData = string.Empty;

    private string _originalGlamourerData = string.Empty;

    public IntPtr PlayerCharacter { get; set; } = IntPtr.Zero;

    public string? PlayerName { get; private set; }

    public string PlayerNameHash { get; }

    public bool RequestedPenumbraRedraw { get; set; }

    public bool WasVisible { get; private set; }

    private CharacterCacheDto _cachedData = new();

    private PlayerRelatedObject? _currentCharacterEquipment;

    public void ApplyCharacterData(CharacterCacheDto characterData)
    {
        Logger.Debug("Received data for " + this);

        Logger.Debug("Checking for files to download for player " + PlayerName);
        Logger.Debug("Hash for data is " + characterData.GetHashCode());

        if (characterData.GetHashCode() == _cachedData.GetHashCode()) return;

        bool updateModdedPaths = false;
        List<ObjectKind> charaDataToUpdate = new List<ObjectKind>();
        foreach (var objectKind in Enum.GetValues<ObjectKind>())
        {
            _cachedData.FileReplacements.TryGetValue(objectKind, out var existingFileReplacements);
            characterData.FileReplacements.TryGetValue(objectKind, out var newFileReplacements);
            _cachedData.GlamourerData.TryGetValue(objectKind, out var existingGlamourerData);
            characterData.GlamourerData.TryGetValue(objectKind, out var newGlamourerData);

            bool hasNewButNotOldFileReplacements = newFileReplacements != null && existingFileReplacements == null;
            bool hasOldButNotNewFileReplacements = existingFileReplacements != null && newFileReplacements == null;
            bool hasNewButNotOldGlamourerData = newGlamourerData != null && existingGlamourerData == null;
            bool hasOldButNotNewGlamourerData = existingGlamourerData != null && newGlamourerData == null;
            bool hasNewAndOldFileReplacements = newFileReplacements != null && existingFileReplacements != null;
            bool hasNewAndOldGlamourerData = newGlamourerData != null && existingGlamourerData != null;

            if (hasNewButNotOldFileReplacements || hasOldButNotNewFileReplacements || hasNewButNotOldGlamourerData || hasOldButNotNewGlamourerData)
            {
                Logger.Debug("Updating " + objectKind);
                updateModdedPaths = true;
                charaDataToUpdate.Add(objectKind);
                continue;
            }

            if (hasNewAndOldFileReplacements)
            {
                bool listsAreEqual = Enumerable.SequenceEqual(_cachedData.FileReplacements[objectKind], characterData.FileReplacements[objectKind]);
                if (!listsAreEqual)
                {
                    Logger.Debug("Updating " + objectKind);
                    updateModdedPaths = true;
                    charaDataToUpdate.Add(objectKind);
                    continue;
                }
            }

            if (hasNewAndOldGlamourerData)
            {
                bool glamourerDataDifferent = _cachedData.GlamourerData[objectKind] != characterData.GlamourerData[objectKind];
                if (glamourerDataDifferent)
                {
                    Logger.Debug("Updating " + objectKind);
                    charaDataToUpdate.Add(objectKind);
                    continue;
                }
            }
        }

        _cachedData = characterData;

        DownloadAndApplyCharacter(charaDataToUpdate, updateModdedPaths);
    }

    private void DownloadAndApplyCharacter(List<ObjectKind> objectKind, bool updateModdedPaths)
    {
        if (!objectKind.Any())
        {
            Logger.Debug("Nothing to update for " + this);
        }

        _downloadCancellationTokenSource?.Cancel();
        _downloadCancellationTokenSource = new CancellationTokenSource();
        var downloadToken = _downloadCancellationTokenSource.Token;
        var downloadId = _apiController.GetDownloadId();
        Task.Run(async () =>
        {
            List<FileReplacementDto> toDownloadReplacements;

            if (updateModdedPaths)
            {
                Dictionary<string, string> moddedPaths;
                int attempts = 0;
                //Logger.Verbose(JsonConvert.SerializeObject(_cachedData, Formatting.Indented));
                while ((toDownloadReplacements = TryCalculateModdedDictionary(out moddedPaths)).Count > 0 && attempts++ <= 10)
                {
                    Logger.Debug("Downloading missing files for player " + PlayerName + ", kind: " + objectKind);
                    await _apiController.DownloadFiles(downloadId, toDownloadReplacements, downloadToken);
                    _apiController.CancelDownload(downloadId);
                    if (downloadToken.IsCancellationRequested)
                    {
                        Logger.Verbose("Detected cancellation");
                        return;
                    }

                    if ((TryCalculateModdedDictionary(out moddedPaths)).All(c => _apiController.ForbiddenTransfers.Any(f => f.Hash == c.Hash)))
                    {
                        break;
                    }
                }

                ApplyBaseData(moddedPaths);
            }

            foreach (var kind in objectKind)
            {
                ApplyCustomizationData(kind, downloadToken);
            }
        }, downloadToken).ContinueWith(task =>
        {
            if (!task.IsCanceled) return;

            Logger.Debug("Download Task was cancelled");
            _apiController.CancelDownload(downloadId);
        });

        _downloadCancellationTokenSource = null;
    }

    private List<FileReplacementDto> TryCalculateModdedDictionary(out Dictionary<string, string> moddedDictionary)
    {
        List<FileReplacementDto> missingFiles = new();
        moddedDictionary = new Dictionary<string, string>();
        try
        {
            using var db = new FileCacheContext();
            foreach (var item in _cachedData.FileReplacements.SelectMany(k => k.Value.Where(v => string.IsNullOrEmpty(v.FileSwapPath))).ToList())
            {
                foreach (var gamePath in item.GamePaths)
                {
                    var fileCache = db.FileCaches.FirstOrDefault(f => f.Hash == item.Hash);
                    if (fileCache != null)
                    {
                        moddedDictionary[gamePath] = fileCache.Filepath;
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

    private void ApplyBaseData(Dictionary<string, string> moddedPaths)
    {
        _ipcManager.PenumbraRemoveTemporaryCollection(PlayerName!);
        _ipcManager.PenumbraSetTemporaryMods(PlayerName!, moddedPaths, _cachedData.ManipulationData);
    }

    private unsafe void ApplyCustomizationData(ObjectKind objectKind, CancellationToken ct)
    {
        if (PlayerCharacter == IntPtr.Zero) return;
        _cachedData.GlamourerData.TryGetValue(objectKind, out var glamourerData);

        if (objectKind == ObjectKind.Player)
        {
            _dalamudUtil.WaitWhileCharacterIsDrawing(PlayerName!, PlayerCharacter, ct);
            ct.ThrowIfCancellationRequested();
            _ipcManager.HeelsSetOffsetForPlayer(_cachedData.HeelsOffset, PlayerCharacter);
            RequestedPenumbraRedraw = true;
            Logger.Debug(
                $"Request Redraw for {PlayerName}");
            if (_ipcManager.CheckGlamourerApi() && !string.IsNullOrEmpty(glamourerData))
            {
                _ipcManager.GlamourerApplyAll(glamourerData, PlayerCharacter);
            }
            else
            {
                _ipcManager.PenumbraRedraw(PlayerCharacter);
            }
        }
        else if (objectKind == ObjectKind.MinionOrMount)
        {
            var minionOrMount = ((Character*)PlayerCharacter)->CompanionObject;
            if (minionOrMount != null)
            {
                Logger.Debug($"Request Redraw for Minion/Mount");
                _dalamudUtil.WaitWhileCharacterIsDrawing(PlayerName! + " minion or mount", (IntPtr)minionOrMount, ct);
                ct.ThrowIfCancellationRequested();
                if (_ipcManager.CheckGlamourerApi() && !string.IsNullOrEmpty(glamourerData))
                {
                    _ipcManager.GlamourerApplyAll(glamourerData, obj: (IntPtr)minionOrMount);
                }
                else
                {
                    _ipcManager.PenumbraRedraw((IntPtr)minionOrMount);
                }
            }
        }
        else if (objectKind == ObjectKind.Pet)
        {
            int tick = 16;
            var pet = _dalamudUtil.GetPet(PlayerCharacter);
            if (pet != IntPtr.Zero)
            {
                var totalWait = 0;
                var newPet = IntPtr.Zero;
                const int maxWait = 3000;
                Logger.Debug($"Request Redraw for Pet, waiting {maxWait}ms");

                do
                {
                    Thread.Sleep(tick);
                    totalWait += tick;
                    newPet = _dalamudUtil.GetPet(PlayerCharacter);
                } while (newPet == pet && totalWait < maxWait);

                if (_ipcManager.CheckGlamourerApi() && !string.IsNullOrEmpty(glamourerData))
                {
                    _ipcManager.GlamourerApplyAll(glamourerData, newPet);
                }
                else
                {
                    _ipcManager.PenumbraRedraw(newPet);
                }
            }
        }
        else if (objectKind == ObjectKind.Companion)
        {
            var companion = _dalamudUtil.GetCompanion(PlayerCharacter);
            if (companion != IntPtr.Zero)
            {
                Logger.Debug("Request Redraw for Companion");
                _dalamudUtil.WaitWhileCharacterIsDrawing(PlayerName! + " companion", companion, ct);
                ct.ThrowIfCancellationRequested();
                if (_ipcManager.CheckGlamourerApi() && !string.IsNullOrEmpty(glamourerData))
                {
                    _ipcManager.GlamourerApplyAll(glamourerData, companion);
                }
                else
                {
                    _ipcManager.PenumbraRedraw(companion);
                }
            }
        }
    }

    private unsafe void RevertCustomizationData(ObjectKind objectKind)
    {
        if (PlayerCharacter == IntPtr.Zero) return;

        if (objectKind == ObjectKind.Player)
        {
            if (_ipcManager.CheckGlamourerApi())
            {
                _ipcManager.GlamourerApplyOnlyCustomization(_originalGlamourerData, PlayerCharacter);
                _ipcManager.GlamourerApplyOnlyEquipment(_lastGlamourerData, PlayerCharacter);
            }
            else
            {
                _ipcManager.PenumbraRedraw(PlayerCharacter);
            }
        }
        else if (objectKind == ObjectKind.MinionOrMount)
        {
            var minionOrMount = ((Character*)PlayerCharacter)->CompanionObject;
            if (minionOrMount != null)
            {
                _ipcManager.PenumbraRedraw((IntPtr)minionOrMount);
            }
        }
        else if (objectKind == ObjectKind.Pet)
        {
            var pet = _dalamudUtil.GetPet(PlayerCharacter);
            if (pet != IntPtr.Zero)
            {
                _ipcManager.PenumbraRedraw(pet);
            }
        }
        else if (objectKind == ObjectKind.Companion)
        {
            var companion = _dalamudUtil.GetCompanion(PlayerCharacter);
            if (companion != IntPtr.Zero)
            {
                _ipcManager.PenumbraRedraw(companion);
            }
        }
    }

    public void DisposePlayer()
    {
        if (_isDisposed) return;
        if (string.IsNullOrEmpty(PlayerName)) return;
        Logger.Debug("Disposing " + PlayerName + " (" + PlayerNameHash + ")");
        _isDisposed = true;
        try
        {
            Logger.Verbose("Restoring state for " + PlayerName);
            _dalamudUtil.DelayedFrameworkUpdate -= DalamudUtilOnDelayedFrameworkUpdate;
            _ipcManager.PenumbraRedrawEvent -= IpcManagerOnPenumbraRedrawEvent;
            _ipcManager.PenumbraRemoveTemporaryCollection(PlayerName);
            _downloadCancellationTokenSource?.Cancel();
            _downloadCancellationTokenSource?.Dispose();
            if (PlayerCharacter != IntPtr.Zero)
            {
                foreach (var item in _cachedData.FileReplacements)
                {
                    RevertCustomizationData(item.Key);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex.Message + Environment.NewLine + ex.StackTrace);
        }
        finally
        {
            _cachedData = new();
            PlayerName = string.Empty;
            PlayerCharacter = IntPtr.Zero;
            IsVisible = false;
        }
    }

    public void InitializePlayer(IntPtr character, string name, CharacterCacheDto? cache)
    {
        if (!_isDisposed) return;
        Logger.Debug("Initializing Player " + this + " has cache: " + (cache != null));
        IsVisible = true;
        PlayerName = name;
        PlayerCharacter = character;
        _dalamudUtil.DelayedFrameworkUpdate += DalamudUtilOnDelayedFrameworkUpdate;
        _ipcManager.PenumbraRedrawEvent += IpcManagerOnPenumbraRedrawEvent;
        _originalGlamourerData = _ipcManager.GlamourerGetCharacterCustomization(PlayerCharacter);
        _currentCharacterEquipment = new PlayerRelatedObject(ObjectKind.Player, IntPtr.Zero, IntPtr.Zero,
            () => _dalamudUtil.GetPlayerCharacterFromObjectTableByName(PlayerName)?.Address ?? IntPtr.Zero);
        _isDisposed = false;
        if (cache != null)
        {
            ApplyCharacterData(cache);
        }
    }

    private void DalamudUtilOnDelayedFrameworkUpdate()
    {
        if (!_dalamudUtil.IsPlayerPresent || !_ipcManager.Initialized || !_apiController.IsConnected) return;

        PlayerCharacter = _dalamudUtil.GetPlayerCharacterFromObjectTableByName(PlayerName!)?.Address ?? IntPtr.Zero;
        if (PlayerCharacter == IntPtr.Zero)
        {
            DisposePlayer();
            return;
        }

        _currentCharacterEquipment?.CheckAndUpdateObject();
        if (_currentCharacterEquipment?.HasUnprocessedUpdate ?? false)
        {
            OnPlayerChanged();
        }

        IsVisible = true;
    }

    public override string ToString()
    {
        return PlayerNameHash + ":" + PlayerName + ":HasChar " + (PlayerCharacter != IntPtr.Zero);
    }

    private Task? _penumbraRedrawEventTask;

    private void IpcManagerOnPenumbraRedrawEvent(IntPtr address, int idx)
    {
        var player = _dalamudUtil.GetCharacterFromObjectTableByIndex(idx);
        if (player == null || player.Name.ToString() != PlayerName) return;
        if (!_penumbraRedrawEventTask?.IsCompleted ?? false) return;

        _penumbraRedrawEventTask = Task.Run(() =>
        {
            PlayerCharacter = address;
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            _dalamudUtil.WaitWhileCharacterIsDrawing(PlayerName!, PlayerCharacter, cts.Token);
            cts.Dispose();
            cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            if (RequestedPenumbraRedraw == false)
            {
                Logger.Debug("Unauthorized character change detected");
                ApplyCustomizationData(ObjectKind.Player, cts.Token);
            }
            else
            {
                RequestedPenumbraRedraw = false;
                Logger.Debug(
                    $"Penumbra Redraw done for {PlayerName}");
            }
            cts.Dispose();
        });
    }

    private void OnPlayerChanged()
    {
        Logger.Debug($"Player {PlayerName} changed, PenumbraRedraw is {RequestedPenumbraRedraw}");
        _currentCharacterEquipment!.HasUnprocessedUpdate = false;
        if (!RequestedPenumbraRedraw && PlayerCharacter != IntPtr.Zero)
        {
            Logger.Debug($"Saving new Glamourer data");
            _lastGlamourerData = _ipcManager.GlamourerGetCharacterCustomization(PlayerCharacter);
        }
    }
}