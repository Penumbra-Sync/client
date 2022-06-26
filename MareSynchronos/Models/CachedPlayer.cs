using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Logging;
using MareSynchronos.API;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Managers;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Models;

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
            _lastAppliedEquipmentHash = e.CharacterData.Hash;
        }
        else
        {
            Logger.Debug("Had valid local cache for " + PlayerName);
        }

        DownloadAndApplyCharacter();
    }

    private void DownloadAndApplyCharacter()
    {
        _downloadCancellationTokenSource?.Cancel();
        _downloadCancellationTokenSource = new CancellationTokenSource();
        var downloadToken = _downloadCancellationTokenSource.Token;

        Task.Run(async () =>
        {
            List<FileReplacementDto> toDownloadReplacements;

            Dictionary<string, string> moddedPaths;
            while ((toDownloadReplacements = TryCalculateModdedDictionary(_cache[_lastAppliedEquipmentHash], out moddedPaths)).Count > 0)
            {
                Logger.Debug("Downloading missing files for player " + PlayerName);
                await _apiController.DownloadFiles(toDownloadReplacements, downloadToken);
            }

            if (downloadToken.IsCancellationRequested)
            {
                return;
            }

            ApplyCharacterData(_cache[_lastAppliedEquipmentHash], moddedPaths);
        }, downloadToken);
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

    private void ApplyCharacterData(CharacterCacheDto cache, Dictionary<string, string> moddedPaths)
    {
        _ipcManager.PenumbraRemoveTemporaryCollection(PlayerName!);
        var tempCollection = _ipcManager.PenumbraCreateTemporaryCollection(PlayerName!);
        _dalamudUtil.WaitWhileCharacterIsDrawing(PlayerCharacter!.Address);
        RequestedPenumbraRedraw = true;
        Logger.Debug(
            $"Request Redraw for {PlayerName}");
        _ipcManager.PenumbraSetTemporaryMods(tempCollection, moddedPaths, cache.ManipulationData);
        _ipcManager.GlamourerRevertCharacterCustomization(PlayerName!);
        _ipcManager.GlamourerApplyAll(cache.GlamourerData, PlayerName!);
    }

    public void DisposePlayer()
    {
        Logger.Debug("Disposing " + PlayerNameHash);
        if (string.IsNullOrEmpty(PlayerName)) return;
        try
        {
            Logger.Debug("Restoring state for " + PlayerName);
            IsVisible = false;
            _downloadCancellationTokenSource?.Cancel();
            _downloadCancellationTokenSource?.Dispose();
            _downloadCancellationTokenSource = null;
            _dalamudUtil.RemovePlayerFromWatch(PlayerName);
            _ipcManager.PenumbraRemoveTemporaryCollection(PlayerName);
            _ipcManager.GlamourerRevertCharacterCustomization(PlayerName);
            _ipcManager.GlamourerApplyOnlyCustomization(_originalGlamourerData, PlayerName);
            _ipcManager.GlamourerApplyOnlyEquipment(_lastGlamourerData, PlayerName);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex.Message + Environment.NewLine + ex.StackTrace);
        }
        finally
        {
            _dalamudUtil.PlayerChanged -= WatcherOnPlayerChanged;
            _ipcManager.PenumbraRedrawEvent -= IpcManagerOnPenumbraRedrawEvent;
            _apiController.CharacterReceived -= ApiControllerOnCharacterReceived;
            PlayerName = string.Empty;
            PlayerCharacter = null;
        }
    }

    public void InitializePlayer(PlayerCharacter character)
    {
        IsVisible = true;
        PlayerName = character.Name.ToString();
        PlayerCharacter = character;
        Logger.Debug("Initializing Player " + this);
        _dalamudUtil.AddPlayerToWatch(PlayerName!);
        _dalamudUtil.PlayerChanged += WatcherOnPlayerChanged;
        _ipcManager.PenumbraRedrawEvent += IpcManagerOnPenumbraRedrawEvent;
        _apiController.CharacterReceived += ApiControllerOnCharacterReceived;
        _originalGlamourerData = _ipcManager.GlamourerGetCharacterCustomization(PlayerName);
    }

    public override string ToString()
    {
        return PlayerNameHash + ":" + PlayerName + ":HasChar " + (PlayerCharacter != null);
    }

    private Task? _penumbraRedrawEventTask;

    private void IpcManagerOnPenumbraRedrawEvent(object? sender, EventArgs e)
    {
        var player = _dalamudUtil.GetPlayerCharacterFromObjectTableIndex((int)sender!);
        if (player == null || player.Name.ToString() != PlayerName) return;
        if (!_penumbraRedrawEventTask?.IsCompleted ?? false) return;

        _penumbraRedrawEventTask = Task.Run(() =>
        {
            PlayerCharacter = player;
            _dalamudUtil.WaitWhileCharacterIsDrawing(PlayerCharacter.Address);

            if (RequestedPenumbraRedraw == false && !string.IsNullOrEmpty(_lastAppliedEquipmentHash))
            {
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

    private void WatcherOnPlayerChanged(Character actor)
    {
        if (actor.Name.ToString() != PlayerName) return;
        Logger.Debug($"Player {PlayerName} changed, PenumbraRedraw is {RequestedPenumbraRedraw}");
        PlayerCharacter = _dalamudUtil.GetPlayerCharacterFromObjectTableByName(PlayerName!);
        if (PlayerCharacter is null)
        {
            Logger.Debug($"Invalid PlayerCharacter for {PlayerName}");
        }
        else if (!RequestedPenumbraRedraw && PlayerCharacter is not null)
        {
            Logger.Debug($"Saving new Glamourer data");
            _lastGlamourerData = _ipcManager.GlamourerGetCharacterCustomization(PlayerName!);
        }
    }
}