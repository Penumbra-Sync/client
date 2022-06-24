using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Logging;
using MareSynchronos.API;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Managers;

public class CharacterCacheManager : IDisposable
{
    private readonly ApiController _apiController;
    private readonly ClientState _clientState;
    private readonly DalamudUtil _dalamudUtil;
    private readonly Framework _framework;
    private readonly IpcManager _ipcManager;
    private readonly ObjectTable _objectTable;
    private readonly List<CachedPlayer> _onlineCachedPlayers = new();
    private readonly List<string> _localVisiblePlayers = new();
    private DateTime _lastPlayerObjectCheck = DateTime.Now;

    public CharacterCacheManager(ClientState clientState, Framework framework, ObjectTable objectTable, ApiController apiController, DalamudUtil dalamudUtil, IpcManager ipcManager)
    {
        Logger.Debug("Creating " + nameof(CharacterCacheManager));

        _clientState = clientState;
        _framework = framework;
        _objectTable = objectTable;
        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _ipcManager = ipcManager;
    }

    public void AddInitialPairs(List<string> apiTaskResult)
    {
        _onlineCachedPlayers.AddRange(apiTaskResult.Select(a => new CachedPlayer(a)));
        Logger.Debug("Online and paired users: " + string.Join(",", _onlineCachedPlayers));
    }

    public void Dispose()
    {
        Logger.Debug("Disposing " + nameof(CharacterCacheManager));

        _apiController.CharacterReceived -= ApiControllerOnCharacterReceived;
        _apiController.PairedClientOnline -= ApiControllerOnPairedClientOnline;
        _apiController.PairedClientOffline -= ApiControllerOnPairedClientOffline;
        _apiController.PairedWithOther -= ApiControllerOnPairedWithOther;
        _apiController.UnpairedFromOther -= ApiControllerOnUnpairedFromOther;
        _framework.Update -= FrameworkOnUpdate;

        foreach (var character in _onlineCachedPlayers.ToList())
        {
            RestoreCharacter(character);
        }

        _onlineCachedPlayers.Clear();
    }

    public void Initialize()
    {
        _onlineCachedPlayers.Clear();

        _apiController.CharacterReceived += ApiControllerOnCharacterReceived;
        _apiController.PairedClientOnline += ApiControllerOnPairedClientOnline;
        _apiController.PairedClientOffline += ApiControllerOnPairedClientOffline;
        _apiController.PairedWithOther += ApiControllerOnPairedWithOther;
        _apiController.UnpairedFromOther += ApiControllerOnUnpairedFromOther;
        _framework.Update += FrameworkOnUpdate;
    }

    public async Task UpdatePlayersFromService(Dictionary<string, int> playerJobIds)
    {
        await _apiController.GetCharacterData(playerJobIds);
    }

    private void ApiControllerOnCharacterReceived(object? sender, CharacterReceivedEventArgs e)
    {
        Logger.Debug("Received hash for " + e.CharacterNameHash);
        string otherPlayerName;

        var localPlayers = _dalamudUtil.GetLocalPlayers();
        if (localPlayers.ContainsKey(e.CharacterNameHash))
        {
            _onlineCachedPlayers.Single(p => p.PlayerNameHash == e.CharacterNameHash).PlayerName = localPlayers[e.CharacterNameHash].Name.ToString();
            otherPlayerName = localPlayers[e.CharacterNameHash].Name.ToString();
        }
        else
        {
            Logger.Debug("Found no local player for " + e.CharacterNameHash);
            return;
        }

        _onlineCachedPlayers.Single(p => p.PlayerNameHash == e.CharacterNameHash)
            .CharacterCache[e.CharacterData.JobId] = e.CharacterData;

        List<FileReplacementDto> toDownloadReplacements;
        using (var db = new FileCacheContext())
        {
            Logger.Debug("Checking for files to download for player " + otherPlayerName);
            Logger.Debug("Received total " + e.CharacterData.FileReplacements.Count + " file replacement data");
            Logger.Debug("Hash for data is " + e.CharacterData.Hash);
            toDownloadReplacements =
                e.CharacterData.FileReplacements.Where(f => !db.FileCaches.Any(c => c.Hash == f.Hash))
                    .ToList();
        }

        Logger.Debug("Downloading missing files for player " + otherPlayerName);
        // todo: make this cancellable
        Task.Run(async () =>
        {
            await _apiController.DownloadFiles(toDownloadReplacements);

            Logger.Debug("Assigned hash to visible player: " + otherPlayerName);
            _ipcManager.PenumbraRemoveTemporaryCollection(otherPlayerName);
            var tempCollection = _ipcManager.PenumbraCreateTemporaryCollection(otherPlayerName);
            Dictionary<string, string> moddedPaths = new();
            try
            {
                using var db = new FileCacheContext();
                foreach (var item in e.CharacterData.FileReplacements)
                {
                    foreach (var gamePath in item.GamePaths)
                    {
                        var fileCache = db.FileCaches.FirstOrDefault(f => f.Hash == item.Hash);
                        if (fileCache != null)
                        {
                            moddedPaths.Add(gamePath, fileCache.Filepath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Something went wrong during calculation replacements");
            }

            _dalamudUtil.WaitWhileCharacterIsDrawing(localPlayers[e.CharacterNameHash].Address);

            _ipcManager.PenumbraSetTemporaryMods(tempCollection, moddedPaths, e.CharacterData.ManipulationData);
            _ipcManager.GlamourerApplyCharacterCustomization(e.CharacterData.GlamourerData, otherPlayerName);
        });
    }

    private void ApiControllerOnPairedClientOffline(object? sender, EventArgs e)
    {
        Logger.Debug("Player offline: " + sender!);
        RestoreCharacter(_onlineCachedPlayers.SingleOrDefault(f => f.PlayerNameHash == (string)sender!));
        _onlineCachedPlayers.RemoveAll(p => p.PlayerNameHash == ((string)sender!));
    }

    private void ApiControllerOnPairedClientOnline(object? sender, EventArgs e)
    {
        Logger.Debug("Player online: " + sender!);
        _onlineCachedPlayers.Add(new CachedPlayer((string)sender!));
    }

    private void ApiControllerOnPairedWithOther(object? sender, EventArgs e)
    {
        var characterHash = (string?)sender;
        if (string.IsNullOrEmpty(characterHash)) return;
        var players = _dalamudUtil.GetLocalPlayers();
        if (!players.ContainsKey(characterHash)) return;
        Logger.Debug("Getting data for " + characterHash);
        _ = _apiController.GetCharacterData(new Dictionary<string, int> { { characterHash, (int)players[characterHash].ClassJob.Id } });
    }

    private void ApiControllerOnUnpairedFromOther(object? sender, EventArgs e)
    {
        var characterHash = (string?)sender;
        if (string.IsNullOrEmpty(characterHash)) return;
        RestoreCharacter(_onlineCachedPlayers.Single(p => p.PlayerNameHash == (string)sender!));
    }

    private void FrameworkOnUpdate(Framework framework)
    {
        try
        {
            if (_clientState.LocalPlayer == null) return;

            if (DateTime.Now < _lastPlayerObjectCheck.AddSeconds(2)) return;

            _localVisiblePlayers.Clear();
            foreach (var obj in _objectTable)
            {
                if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;
                var playerName = obj.Name.ToString();
                if (playerName == _dalamudUtil.PlayerName) continue;
                var pObj = (PlayerCharacter)obj;
                _localVisiblePlayers.Add(pObj.Name.ToString());
                if (_onlineCachedPlayers.Any(p => p.PlayerName == pObj.Name.ToString()))
                {
                    _onlineCachedPlayers.Single(p => p.PlayerName == pObj.Name.ToString()).IsVisible = true;
                    continue;
                }

                var hashedName = Crypto.GetHash256(pObj.Name.ToString() + pObj.HomeWorld.Id.ToString());

                if (_onlineCachedPlayers.All(p => p.PlayerNameHash != hashedName)) continue;

                var cachedPlayer = _onlineCachedPlayers.Single(p => p.PlayerNameHash == hashedName);
                if (string.IsNullOrEmpty(cachedPlayer.PlayerName))
                {
                    cachedPlayer.PlayerName = pObj.Name.ToString();
                }
                cachedPlayer.PlayerCharacter = pObj;
                cachedPlayer.IsVisible = true;
            }

            foreach (var item in _onlineCachedPlayers.Where(p => !string.IsNullOrEmpty(p.PlayerName) && !_localVisiblePlayers.Contains(p.PlayerName!)))
            {
                item.IsVisible = false;
            }

            foreach (var item in _onlineCachedPlayers.Where(p => !string.IsNullOrEmpty(p.PlayerName) && !p.IsVisible && p.WasVisible))
            {
                Logger.Debug("Player not visible anymore: " + item.PlayerName);
                RestoreCharacter(item);
            }

            var newVisiblePlayers = _onlineCachedPlayers.Where(p => p.IsVisible && !p.WasVisible).ToList();
            if (newVisiblePlayers.Any())
            {
                Logger.Debug("Getting data for new players: " + string.Join(Environment.NewLine, newVisiblePlayers));
                Task.Run(async () => await UpdatePlayersFromService(newVisiblePlayers
                    .ToDictionary(k => k.PlayerNameHash, k => (int)k.PlayerCharacter!.ClassJob.Id)));
            }

            _lastPlayerObjectCheck = DateTime.Now;
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "error");
        }
    }

    private void RestoreCharacter(CachedPlayer? character)
    {
        if (character == null || string.IsNullOrEmpty(character.PlayerName)) return;

        Logger.Debug("Restoring state for " + character.PlayerName);
        _ipcManager.PenumbraRemoveTemporaryCollection(character.PlayerName);
        _ipcManager.GlamourerRevertCharacterCustomization(character.PlayerName);

        character.Reset();
    }
}