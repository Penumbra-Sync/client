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
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Penumbra.GameData.Structs;

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

    private bool _isDisposed = false;
    private CancellationTokenSource? _downloadCancellationTokenSource = new();

    private string _lastGlamourerData = string.Empty;

    private string _originalGlamourerData = string.Empty;

    public Dalamud.Game.ClientState.Objects.Types.Character? PlayerCharacter { get; set; }

    public string? PlayerName { get; private set; }

    public string PlayerNameHash { get; }

    public bool RequestedPenumbraRedraw { get; set; }

    public bool WasVisible { get; private set; }

    private CharacterCacheDto _cachedData = new();

    private CharacterEquipment? _currentCharacterEquipment;

    public void ApplyCharacterData(CharacterCacheDto characterData)
    {
        Logger.Debug("Received data for " + this);

        Logger.Debug("Checking for files to download for player " + PlayerName);
        Logger.Debug("Hash for data is " + characterData.GetHashCode());

        if (characterData.GetHashCode() == _cachedData.GetHashCode()) return;

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
                charaDataToUpdate.Add(objectKind);
                continue;
            }

            if (hasNewAndOldFileReplacements)
            {
                bool listsAreEqual = Enumerable.SequenceEqual(_cachedData.FileReplacements[objectKind], characterData.FileReplacements[objectKind]);
                if (!listsAreEqual)
                {
                    Logger.Debug("Updating " + objectKind);
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

        DownloadAndApplyCharacter(charaDataToUpdate);
    }

    private void DownloadAndApplyCharacter(List<ObjectKind> objectKind)
    {
        _downloadCancellationTokenSource?.Cancel();
        _downloadCancellationTokenSource = new CancellationTokenSource();
        var downloadToken = _downloadCancellationTokenSource.Token;
        var downloadId = _apiController.GetDownloadId();
        Task.Run(async () =>
        {
            List<FileReplacementDto> toDownloadReplacements;

            Dictionary<string, string> moddedPaths;
            int attempts = 0;
            while ((toDownloadReplacements = TryCalculateModdedDictionary(out moddedPaths)).Count > 0 && attempts++ <= 10)
            {
                Logger.Debug("Downloading missing files for player " + PlayerName + ", kind: " + objectKind);
                await _apiController.DownloadFiles(downloadId, toDownloadReplacements, downloadToken);
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

            if (_dalamudUtil.IsInGpose)
            {
                Logger.Verbose("Player is in GPose, waiting");
                while (_dalamudUtil.IsInGpose)
                {
                    await Task.Delay(TimeSpan.FromSeconds(0.5));
                    downloadToken.ThrowIfCancellationRequested();
                }
            }

            ApplyBaseData(moddedPaths);

            foreach (var kind in objectKind)
            {
                ApplyCustomizationData(kind);
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
            foreach (var item in _cachedData.FileReplacements.SelectMany(k => k.Value).ToList())
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
                        missingFiles.Add(item);
                    }
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
        var tempCollection = _ipcManager.PenumbraCreateTemporaryCollection(PlayerName!);
        _ipcManager.PenumbraSetTemporaryMods(tempCollection, moddedPaths, _cachedData.ManipulationData);
    }

    private unsafe void ApplyCustomizationData(ObjectKind objectKind)
    {
        if (PlayerCharacter is null) return;
        _cachedData.GlamourerData.TryGetValue(objectKind, out var glamourerData);

        if (objectKind == ObjectKind.Player)
        {
            _dalamudUtil.WaitWhileCharacterIsDrawing(PlayerCharacter.Address);
            RequestedPenumbraRedraw = true;
            Logger.Debug(
                $"Request Redraw for {PlayerName}");
            _ipcManager.GlamourerApplyAll(glamourerData, PlayerCharacter.Address);
        }
        else if (objectKind == ObjectKind.Minion)
        {
            var minion = ((Character*)PlayerCharacter.Address)->CompanionObject;
            if (minion != null)
            {
                Logger.Debug($"Request Redraw for Minion");
                _ipcManager.GlamourerApplyAll(glamourerData, obj: (IntPtr)minion);
            }
        }
        else if (objectKind == ObjectKind.Pet)
        {
            var pet = _dalamudUtil.GetPet(PlayerCharacter.Address);
            if (pet != IntPtr.Zero)
            {
                Logger.Debug("Request Redraw for Pet");
                _ipcManager.GlamourerApplyAll(glamourerData, pet);
            }
        }
        else if (objectKind == ObjectKind.Companion)
        {
            var companion = _dalamudUtil.GetCompanion(PlayerCharacter.Address);
            if (companion != IntPtr.Zero)
            {
                Logger.Debug("Request Redraw for Companion");
                _ipcManager.GlamourerApplyAll(glamourerData, companion);
            }
        }
        else if (objectKind == ObjectKind.Mount)
        {
            var mount = ((CharaExt*)PlayerCharacter.Address)->Mount;
            if (mount != null)
            {
                Logger.Debug($"Request Redraw for Mount");
                _ipcManager.PenumbraRedraw((IntPtr)mount);
            }
        }
    }

    private unsafe void RevertCustomizationData(ObjectKind objectKind)
    {
        if (PlayerCharacter is null) return;

        if (objectKind == ObjectKind.Player)
        {
            _ipcManager.GlamourerApplyOnlyCustomization(_originalGlamourerData, PlayerCharacter);
            _ipcManager.GlamourerApplyOnlyEquipment(_lastGlamourerData, PlayerCharacter);
        }
        else if (objectKind == ObjectKind.Minion)
        {
            var minion = ((Character*)PlayerCharacter.Address)->CompanionObject;
            if (minion != null)
            {
                _ipcManager.PenumbraRedraw((IntPtr)minion);
            }
        }
        else if (objectKind == ObjectKind.Pet)
        {
            var pet = _dalamudUtil.GetPet(PlayerCharacter.Address);
            if (pet != IntPtr.Zero)
            {
                _ipcManager.PenumbraRedraw(pet);
            }
        }
        else if (objectKind == ObjectKind.Companion)
        {
            var companion = _dalamudUtil.GetCompanion(PlayerCharacter.Address);
            if (companion != IntPtr.Zero)
            {
                _ipcManager.PenumbraRedraw(companion);
            }
        }
        else if (objectKind == ObjectKind.Mount)
        {
            var mount = ((CharaExt*)PlayerCharacter.Address)->Mount;
            if (mount != null)
            {
                _ipcManager.PenumbraRedraw((IntPtr)mount);
            }
        }
    }

    public void DisposePlayer()
    {
        if (_isDisposed) return;
        if (string.IsNullOrEmpty(PlayerName)) return;
        Logger.Verbose("Disposing " + PlayerName + " (" + PlayerNameHash + ")");
        _isDisposed = true;
        try
        {
            Logger.Verbose("Restoring state for " + PlayerName);
            _dalamudUtil.FrameworkUpdate -= DalamudUtilOnFrameworkUpdate;
            _ipcManager.PenumbraRedrawEvent -= IpcManagerOnPenumbraRedrawEvent;
            _downloadCancellationTokenSource?.Cancel();
            _downloadCancellationTokenSource?.Dispose();
            _ipcManager.PenumbraRemoveTemporaryCollection(PlayerName);
            if (PlayerCharacter != null && PlayerCharacter.IsValid())
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
            PlayerCharacter = null;
            IsVisible = false;
        }
    }

    public void InitializePlayer(PlayerCharacter character, CharacterCacheDto? cache)
    {
        Logger.Debug("Initializing Player " + this + " has cache: " + (cache != null));
        IsVisible = true;
        PlayerName = character.Name.ToString();
        PlayerCharacter = character;
        _dalamudUtil.FrameworkUpdate += DalamudUtilOnFrameworkUpdate;
        _ipcManager.PenumbraRedrawEvent += IpcManagerOnPenumbraRedrawEvent;
        _originalGlamourerData = _ipcManager.GlamourerGetCharacterCustomization(PlayerCharacter);
        _currentCharacterEquipment = new CharacterEquipment(PlayerCharacter);
        _isDisposed = false;
        if (cache != null)
        {
            ApplyCharacterData(cache);
        }
    }

    private void DalamudUtilOnFrameworkUpdate()
    {
        if (!_dalamudUtil.IsPlayerPresent || !_ipcManager.Initialized || !_apiController.IsConnected) return;

        PlayerCharacter = _dalamudUtil.GetPlayerCharacterFromObjectTableByName(PlayerName!);
        if (PlayerCharacter == null)
        {
            DisposePlayer();
            return;
        }

        if (!_currentCharacterEquipment!.CompareAndUpdate(PlayerCharacter))
        {
            OnPlayerChanged();
        }

        IsVisible = true;
    }

    public override string ToString()
    {
        return PlayerNameHash + ":" + PlayerName + ":HasChar " + (PlayerCharacter != null);
    }

    private Task? _penumbraRedrawEventTask;

    private void IpcManagerOnPenumbraRedrawEvent(IntPtr address, int idx)
    {
        var player = _dalamudUtil.GetCharacterFromObjectTableByIndex(idx);
        if (player == null || player.Name.ToString() != PlayerName) return;
        if (!_penumbraRedrawEventTask?.IsCompleted ?? false) return;

        _penumbraRedrawEventTask = Task.Run(() =>
        {
            PlayerCharacter = player;
            _dalamudUtil.WaitWhileCharacterIsDrawing(PlayerCharacter.Address);

            if (RequestedPenumbraRedraw == false)
            {
                Logger.Debug("Unauthorized character change detected");
                ApplyCustomizationData(ObjectKind.Player);
            }
            else
            {
                RequestedPenumbraRedraw = false;
                Logger.Debug(
                    $"Penumbra Redraw done for {PlayerName}");
            }
        });
    }

    private void OnPlayerChanged()
    {
        Logger.Debug($"Player {PlayerName} changed, PenumbraRedraw is {RequestedPenumbraRedraw}");
        if (!RequestedPenumbraRedraw && PlayerCharacter is not null)
        {
            Logger.Debug($"Saving new Glamourer data");
            _lastGlamourerData = _ipcManager.GlamourerGetCharacterCustomization(PlayerCharacter!);
        }
    }
}