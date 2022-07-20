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
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Utils;
using Penumbra.GameData.ByteString;
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
    private CancellationTokenSource? _downloadCancellationTokenSource;

    private string _lastGlamourerData = string.Empty;

    private string _originalGlamourerData = string.Empty;

    public PlayerCharacter? PlayerCharacter { get; set; }

    public string? PlayerName { get; private set; }

    public string PlayerNameHash { get; }
    private string _lastAppliedEquipmentHash = string.Empty;

    public bool RequestedPenumbraRedraw { get; set; }

    public bool WasVisible { get; private set; }

    private readonly Dictionary<string, CharacterCacheDto> _cache = new();

    private CharacterEquipment? _currentCharacterEquipment;

    public void ApplyCharacterData(CharacterCacheDto characterData)
    {
        Logger.Debug("Received data for " + this);

        Logger.Debug("Checking for files to download for player " + PlayerName);
        Logger.Debug("Hash for data is " + characterData.Hash);
        if (!_cache.ContainsKey(characterData.Hash))
        {
            Logger.Debug("Received total " + characterData.FileReplacements.Count + " file replacement data");
            _cache[characterData.Hash] = characterData;
        }
        else
        {
            Logger.Debug("Had valid local cache for " + PlayerName);
        }
        _lastAppliedEquipmentHash = characterData.Hash;

        DownloadAndApplyCharacter();
    }

    private void ApiControllerOnCharacterReceived(object? sender, CharacterReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(PlayerName) || e.CharacterNameHash != PlayerNameHash) return;
        Logger.Debug("Received data for " + this);

        Logger.Debug("Checking for files to download for player " + PlayerName);
        Logger.Debug("Hash for data is " + e.CharacterData.Hash);
        if (!_cache.ContainsKey(e.CharacterData.Hash))
        {
            Logger.Debug("Received total " + e.CharacterData.FileReplacements.Count + " file replacement data");
            _cache[e.CharacterData.Hash] = e.CharacterData;
        }
        else
        {
            Logger.Debug("Had valid local cache for " + PlayerName);
        }
        _lastAppliedEquipmentHash = e.CharacterData.Hash;

        DownloadAndApplyCharacter();
    }

    private void DownloadAndApplyCharacter()
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
            while ((toDownloadReplacements = TryCalculateModdedDictionary(_cache[_lastAppliedEquipmentHash], out moddedPaths)).Count > 0 && attempts++ <= 10)
            {
                Logger.Debug("Downloading missing files for player " + PlayerName);
                await _apiController.DownloadFiles(downloadId, toDownloadReplacements, downloadToken);
                if (downloadToken.IsCancellationRequested)
                {
                    Logger.Verbose("Detected cancellation");
                    return;
                }

                if ((TryCalculateModdedDictionary(_cache[_lastAppliedEquipmentHash], out moddedPaths)).All(c => _apiController.ForbiddenTransfers.Any(f => f.Hash == c.Hash)))
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

            ApplyCharacterData(_cache[_lastAppliedEquipmentHash], moddedPaths);
        }, downloadToken).ContinueWith(task =>
        {
            if (!task.IsCanceled) return;

            Logger.Debug("Download Task was cancelled");
            _apiController.CancelDownload(downloadId);
        });
    }

    private List<FileReplacementDto> TryCalculateModdedDictionary(CharacterCacheDto cache,
        out Dictionary<string, string> moddedDictionary)
    {
        List<FileReplacementDto> missingFiles = new();
        moddedDictionary = new Dictionary<string, string>();
        try
        {
            using var db = new FileCacheContext();
            foreach (var item in cache.FileReplacements)
            {
                foreach (var gamePath in item.GamePaths)
                {
                    var fileCache = db.FileCaches.FirstOrDefault(f => f.Hash == item.Hash);
                    if (fileCache != null)
                    {
                        moddedDictionary.Add(gamePath, fileCache.Filepath);
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

    private unsafe void ApplyCharacterData(CharacterCacheDto cache, Dictionary<string, string> moddedPaths)
    {
        if (PlayerCharacter is null) return;
        _ipcManager.PenumbraRemoveTemporaryCollection(PlayerName!);
        var tempCollection = _ipcManager.PenumbraCreateTemporaryCollection(PlayerName!);
        _dalamudUtil.WaitWhileCharacterIsDrawing(PlayerCharacter!.Address);
        RequestedPenumbraRedraw = true;
        Logger.Debug(
            $"Request Redraw for {PlayerName}");
        _ipcManager.PenumbraSetTemporaryMods(tempCollection, moddedPaths, cache.ManipulationData);
        _ipcManager.GlamourerApplyAll(cache.GlamourerData, PlayerCharacter!);
        var minion = ((Character*)PlayerCharacter.Address)->CompanionObject;
        if (minion != null)
        {
            var compName = new Utf8String(minion->Character.GameObject.GetName()).ToString();
            if (compName != null)
            {
                _ipcManager.PenumbraRedraw(compName);
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
            _apiController.CharacterReceived -= ApiControllerOnCharacterReceived;
            _downloadCancellationTokenSource?.Cancel();
            _downloadCancellationTokenSource?.Dispose();
            _downloadCancellationTokenSource = null;
            _ipcManager.PenumbraRemoveTemporaryCollection(PlayerName);
            if (PlayerCharacter != null && PlayerCharacter.IsValid())
            {
                _ipcManager.GlamourerApplyOnlyCustomization(_originalGlamourerData, PlayerCharacter);
                _ipcManager.GlamourerApplyOnlyEquipment(_lastGlamourerData, PlayerCharacter);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex.Message + Environment.NewLine + ex.StackTrace);
        }
        finally
        {
            PlayerName = string.Empty;
            PlayerCharacter = null;
            IsVisible = false;
        }
    }

    public void InitializePlayer(PlayerCharacter character, CharacterCacheDto? cache)
    {
        Logger.Debug("Initializing Player " + this);
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

    private void IpcManagerOnPenumbraRedrawEvent(object? sender, EventArgs e)
    {
        var player = _dalamudUtil.GetPlayerCharacterFromObjectTableByIndex((int)sender!);
        if (player == null || player.Name.ToString() != PlayerName) return;
        if (!_penumbraRedrawEventTask?.IsCompleted ?? false) return;

        _penumbraRedrawEventTask = Task.Run(() =>
        {
            PlayerCharacter = player;
            _dalamudUtil.WaitWhileCharacterIsDrawing(PlayerCharacter.Address);

            if (RequestedPenumbraRedraw == false && !string.IsNullOrEmpty(_lastAppliedEquipmentHash))
            {
                Logger.Warn("Unauthorized character change detected");
                DownloadAndApplyCharacter();
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