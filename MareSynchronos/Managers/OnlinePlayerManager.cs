using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game;
using MareSynchronos.API;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Utils;

namespace MareSynchronos.Managers;

public class OnlinePlayerManager : IDisposable
{
    private readonly ApiController _apiController;
    private readonly DalamudUtil _dalamudUtil;
    private readonly Framework _framework;
    private readonly IpcManager _ipcManager;
    private readonly PlayerManager _playerManager;
    private readonly List<CachedPlayer> _onlineCachedPlayers = new();
    private readonly Dictionary<string, CharacterCacheDto> _temporaryStoredCharacterCache = new();

    private List<string> OnlineVisiblePlayerHashes => _onlineCachedPlayers.Where(p => p.PlayerCharacter != null)
        .Select(p => p.PlayerNameHash).ToList();
    private DateTime _lastPlayerObjectCheck = DateTime.Now;

    public OnlinePlayerManager(Framework framework, ApiController apiController, DalamudUtil dalamudUtil, IpcManager ipcManager, PlayerManager playerManager)
    {
        Logger.Debug("Creating " + nameof(OnlinePlayerManager));

        _framework = framework;
        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _ipcManager = ipcManager;
        _playerManager = playerManager;

        _apiController.PairedClientOnline += ApiControllerOnPairedClientOnline;
        _apiController.PairedClientOffline += ApiControllerOnPairedClientOffline;
        _apiController.PairedWithOther += ApiControllerOnPairedWithOther;
        _apiController.UnpairedFromOther += ApiControllerOnUnpairedFromOther;
        _apiController.Connected += ApiControllerOnConnected;
        _apiController.Disconnected += ApiControllerOnDisconnected;
        _apiController.CharacterReceived += ApiControllerOnCharacterReceived;

        _ipcManager.PenumbraDisposed += IpcManagerOnPenumbraDisposed;

        _dalamudUtil.LogIn += DalamudUtilOnLogIn;
        _dalamudUtil.LogOut += DalamudUtilOnLogOut;

        if (_dalamudUtil.IsLoggedIn)
        {
            DalamudUtilOnLogIn();
        }
    }

    private void ApiControllerOnCharacterReceived(object? sender, CharacterReceivedEventArgs e)
    {
        var visiblePlayer = _onlineCachedPlayers.SingleOrDefault(c => c.IsVisible && c.PlayerNameHash == e.CharacterNameHash);
        if (visiblePlayer != null)
        {
            Logger.Debug("Received data and applying to " + e.CharacterNameHash);
            visiblePlayer.ApplyCharacterData(e.CharacterData);
        }
        else
        {
            Logger.Debug("Received data but no fitting character visible for " + e.CharacterNameHash);
            _temporaryStoredCharacterCache[e.CharacterNameHash] = e.CharacterData;
        }
    }

    private void PlayerManagerOnPlayerHasChanged(CharacterCacheDto characterCache)
    {
        _ = _apiController.PushCharacterData(characterCache, OnlineVisiblePlayerHashes);
    }

    private void ApiControllerOnConnected(object? sender, EventArgs e)
    {
        var apiTask = _apiController.GetOnlineCharacters();

        Task.WaitAll(apiTask);

        AddInitialPairs(apiTask.Result);

        _playerManager.PlayerHasChanged += PlayerManagerOnPlayerHasChanged;
    }

    private void DalamudUtilOnLogOut()
    {
        _framework.Update -= FrameworkOnUpdate;
    }

    private void DalamudUtilOnLogIn()
    {
        _framework.Update += FrameworkOnUpdate;
    }

    private void IpcManagerOnPenumbraDisposed(object? sender, EventArgs e)
    {
        _onlineCachedPlayers.ForEach(p => p.DisposePlayer());
    }

    private void ApiControllerOnDisconnected(object? sender, EventArgs e)
    {
        RestoreAllCharacters();
        _playerManager.PlayerHasChanged -= PlayerManagerOnPlayerHasChanged;
    }

    public void AddInitialPairs(List<string> apiTaskResult)
    {
        _onlineCachedPlayers.Clear();
        _onlineCachedPlayers.AddRange(apiTaskResult.Select(CreateCachedPlayer));
        Logger.Debug("Online and paired users: " + string.Join(",", _onlineCachedPlayers));
    }

    public void Dispose()
    {
        Logger.Debug("Disposing " + nameof(OnlinePlayerManager));

        RestoreAllCharacters();

        _apiController.PairedClientOnline -= ApiControllerOnPairedClientOnline;
        _apiController.PairedClientOffline -= ApiControllerOnPairedClientOffline;
        _apiController.PairedWithOther -= ApiControllerOnPairedWithOther;
        _apiController.UnpairedFromOther -= ApiControllerOnUnpairedFromOther;
        _apiController.Disconnected -= ApiControllerOnDisconnected;

        _ipcManager.PenumbraDisposed -= ApiControllerOnDisconnected;

        _framework.Update -= FrameworkOnUpdate;

        _dalamudUtil.LogIn -= DalamudUtilOnLogIn;
        _dalamudUtil.LogOut -= DalamudUtilOnLogOut;
    }

    private void RestoreAllCharacters()
    {
        _onlineCachedPlayers.ForEach(p => p.DisposePlayer());
        _onlineCachedPlayers.Clear();
    }

    public async Task UpdatePlayersFromService(Dictionary<string, int> playerJobIds)
    {
        if (!playerJobIds.Any()) return;
        Logger.Debug("Getting data for new players: " + string.Join(Environment.NewLine, playerJobIds));
        await _apiController.GetCharacterData(playerJobIds);
    }

    private void ApiControllerOnPairedClientOffline(object? sender, EventArgs e)
    {
        Logger.Debug("Player offline: " + sender!);
        RemovePlayer((string)sender!);
    }

    private void ApiControllerOnPairedClientOnline(object? sender, EventArgs e)
    {
        Logger.Debug("Player online: " + sender!);
        AddPlayer((string)sender!);
        return;
    }

    private void ApiControllerOnPairedWithOther(object? sender, EventArgs e)
    {
        var characterHash = (string?)sender;
        if (string.IsNullOrEmpty(characterHash)) return;
        Logger.Debug("Pairing with " + characterHash);
        AddPlayer(characterHash);
    }

    private void ApiControllerOnUnpairedFromOther(object? sender, EventArgs e)
    {
        var characterHash = (string?)sender;
        if (string.IsNullOrEmpty(characterHash)) return;
        Logger.Debug("Unpairing from " + characterHash);
        RemovePlayer(characterHash);
    }

    private void AddPlayer(string characterNameHash)
    {
        if (_onlineCachedPlayers.Any(p => p.PlayerNameHash == characterNameHash)) return;
        _onlineCachedPlayers.Add(CreateCachedPlayer(characterNameHash));
    }

    private void RemovePlayer(string characterHash)
    {
        var cachedPlayer = _onlineCachedPlayers.First(p => p.PlayerNameHash == characterHash);
        cachedPlayer.DisposePlayer();
        _onlineCachedPlayers.RemoveAll(c => c.PlayerNameHash == cachedPlayer.PlayerNameHash);
    }

    private void FrameworkOnUpdate(Framework framework)
    {
        if (!_dalamudUtil.IsPlayerPresent || !_ipcManager.Initialized || !_apiController.IsConnected) return;

        if (DateTime.Now < _lastPlayerObjectCheck.AddSeconds(0.25)) return;

        var playerCharacters = _dalamudUtil.GetPlayerCharacters();
        foreach (var pChar in playerCharacters)
        {
            var pObjName = pChar.Name.ToString();
            var hashedName = Crypto.GetHash256(pChar);
            var existingCachedPlayer = _onlineCachedPlayers.SingleOrDefault(p => p.PlayerNameHash == hashedName && !string.IsNullOrEmpty(p.PlayerName));
            if (existingCachedPlayer != null)
            {
                existingCachedPlayer.IsVisible = true;
                continue;
            }

            if (_temporaryStoredCharacterCache.TryGetValue(hashedName, out var cache))
            {
                _temporaryStoredCharacterCache.Remove(hashedName);
            }
            _onlineCachedPlayers.SingleOrDefault(p => p.PlayerNameHash == hashedName)?.InitializePlayer(pChar, cache);
        }

        var newlyVisiblePlayers = _onlineCachedPlayers
            .Where(p => p.PlayerCharacter != null && p.IsVisible && !p.WasVisible).Select(p => p.PlayerNameHash)
            .ToList();
        if (newlyVisiblePlayers.Any() && _playerManager.LastSentCharacterData != null)
        {
            Task.Run(async () =>
            {
                await _apiController.PushCharacterData(_playerManager.LastSentCharacterData.ToCharacterCacheDto(),
                    newlyVisiblePlayers);
            });
        }

        _lastPlayerObjectCheck = DateTime.Now;
    }

    private CachedPlayer CreateCachedPlayer(string hashedName)
    {
        return new CachedPlayer(hashedName, _ipcManager, _apiController, _dalamudUtil);
    }
}