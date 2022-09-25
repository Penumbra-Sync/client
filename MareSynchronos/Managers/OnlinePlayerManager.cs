using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MareSynchronos.API;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Utils;

namespace MareSynchronos.Managers;

public class OnlinePlayerManager : IDisposable
{
    private readonly ApiController _apiController;
    private readonly DalamudUtil _dalamudUtil;
    private readonly IpcManager _ipcManager;
    private readonly PlayerManager _playerManager;
    private readonly FileDbManager _fileDbManager;
    private readonly ConcurrentDictionary<string, CachedPlayer> _onlineCachedPlayers = new();
    private readonly ConcurrentDictionary<string, CharacterCacheDto> _temporaryStoredCharacterCache = new();
    private readonly ConcurrentDictionary<CachedPlayer, CancellationTokenSource> _playerTokenDisposal = new();

    private List<string> OnlineVisiblePlayerHashes => _onlineCachedPlayers.Select(p => p.Value).Where(p => p.PlayerCharacter != IntPtr.Zero)
        .Select(p => p.PlayerNameHash).ToList();

    public OnlinePlayerManager(ApiController apiController, DalamudUtil dalamudUtil, IpcManager ipcManager, PlayerManager playerManager, FileDbManager fileDbManager)
    {
        Logger.Verbose("Creating " + nameof(OnlinePlayerManager));

        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _ipcManager = ipcManager;
        _playerManager = playerManager;
        _fileDbManager = fileDbManager;
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
        _dalamudUtil.ZoneSwitchStart += DalamudUtilOnZoneSwitched;

        if (_dalamudUtil.IsLoggedIn)
        {
            DalamudUtilOnLogIn();
        }
    }

    private void DalamudUtilOnZoneSwitched()
    {
        DisposePlayers();
    }

    private void ApiControllerOnCharacterReceived(object? sender, CharacterReceivedEventArgs e)
    {
        if (_onlineCachedPlayers.TryGetValue(e.CharacterNameHash, out var visiblePlayer) && visiblePlayer.IsVisible)
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
        PushCharacterData(OnlineVisiblePlayerHashes);
    }

    private void ApiControllerOnConnected()
    {
        var apiTask = _apiController.GetOnlineCharacters();

        Task.WaitAll(apiTask);

        AddInitialPairs(apiTask.Result);

        _playerManager.PlayerHasChanged += PlayerManagerOnPlayerHasChanged;
    }

    private void DalamudUtilOnLogOut()
    {
        _dalamudUtil.DelayedFrameworkUpdate -= FrameworkOnUpdate;
    }

    private void DalamudUtilOnLogIn()
    {
        _dalamudUtil.DelayedFrameworkUpdate += FrameworkOnUpdate;
    }

    private void IpcManagerOnPenumbraDisposed()
    {
        DisposePlayers();
    }

    private void DisposePlayers()
    {
        foreach (var kvp in _onlineCachedPlayers)
        {
            kvp.Value.DisposePlayer();
        }
    }

    private void ApiControllerOnDisconnected()
    {
        RestoreAllCharacters();
        _playerManager.PlayerHasChanged -= PlayerManagerOnPlayerHasChanged;
    }

    public void AddInitialPairs(List<string> apiTaskResult)
    {
        _onlineCachedPlayers.Clear();
        foreach (var hash in apiTaskResult)
        {
            _onlineCachedPlayers.TryAdd(hash, CreateCachedPlayer(hash));
        }
        Logger.Verbose("Online and paired users: " + string.Join(Environment.NewLine, _onlineCachedPlayers.Select(k => k.Key)));
    }

    public void Dispose()
    {
        Logger.Verbose("Disposing " + nameof(OnlinePlayerManager));

        RestoreAllCharacters();

        _apiController.PairedClientOnline -= ApiControllerOnPairedClientOnline;
        _apiController.PairedClientOffline -= ApiControllerOnPairedClientOffline;
        _apiController.PairedWithOther -= ApiControllerOnPairedWithOther;
        _apiController.UnpairedFromOther -= ApiControllerOnUnpairedFromOther;
        _apiController.Disconnected -= ApiControllerOnDisconnected;
        _apiController.Connected -= ApiControllerOnConnected;

        _ipcManager.PenumbraDisposed -= ApiControllerOnDisconnected;

        _dalamudUtil.LogIn -= DalamudUtilOnLogIn;
        _dalamudUtil.LogOut -= DalamudUtilOnLogOut;
        _dalamudUtil.ZoneSwitchStart -= DalamudUtilOnZoneSwitched;
        _dalamudUtil.DelayedFrameworkUpdate -= FrameworkOnUpdate;
    }

    private void RestoreAllCharacters()
    {
        DisposePlayers();
        _onlineCachedPlayers.Clear();
    }

    private void ApiControllerOnPairedClientOffline(string charHash)
    {
        Logger.Debug("Player offline: " + charHash);
        RemovePlayer(charHash);
    }

    private void ApiControllerOnPairedClientOnline(string charHash)
    {
        Logger.Debug("Player online: " + charHash);
        AddPlayer(charHash);
        return;
    }

    private void ApiControllerOnPairedWithOther(string charHash)
    {
        if (string.IsNullOrEmpty(charHash)) return;
        Logger.Debug("Pairing with " + charHash);
        AddPlayer(charHash);
    }

    private void ApiControllerOnUnpairedFromOther(string? characterHash)
    {
        if (string.IsNullOrEmpty(characterHash)) return;
        Logger.Debug("Unpairing from " + characterHash);
        RemovePlayer(characterHash);
    }

    private void AddPlayer(string characterNameHash)
    {
        if (_onlineCachedPlayers.TryGetValue(characterNameHash, out var cachedPlayer))
        {
            PushCharacterData(new List<string>() { characterNameHash });
            _playerTokenDisposal.TryGetValue(cachedPlayer, out var cancellationTokenSource);
            cancellationTokenSource?.Cancel();
            return;
        }
        _onlineCachedPlayers.TryAdd(characterNameHash, CreateCachedPlayer(characterNameHash));
    }

    private void RemovePlayer(string characterHash)
    {
        if (!_onlineCachedPlayers.TryGetValue(characterHash, out var cachedPlayer))
        {
            return;
        }

        cachedPlayer.DisposePlayer();
        _onlineCachedPlayers.TryRemove(characterHash, out _);
    }

    private void FrameworkOnUpdate()
    {
        if (!_dalamudUtil.IsPlayerPresent || !_ipcManager.Initialized || !_apiController.IsConnected) return;

        var playerCharacters = _dalamudUtil.GetPlayerCharacters();
        foreach (var pChar in playerCharacters)
        {
            var hashedName = Crypto.GetHash256(pChar);
            if (_onlineCachedPlayers.TryGetValue(hashedName, out var existingPlayer) && !string.IsNullOrEmpty(existingPlayer.PlayerName))
            {
                existingPlayer.IsVisible = true;
                continue;
            }

            if (existingPlayer != null)
            {
                _temporaryStoredCharacterCache.TryRemove(hashedName, out var cache);
                existingPlayer.InitializePlayer(pChar.Address, pChar.Name.ToString(), cache);
            }
        }

        var newlyVisiblePlayers = _onlineCachedPlayers.Select(v => v.Value)
            .Where(p => p.PlayerCharacter != IntPtr.Zero && p.IsVisible && !p.WasVisible).Select(p => p.PlayerNameHash)
            .ToList();
        if (newlyVisiblePlayers.Any())
        {
            Logger.Verbose("Has new visible players, pushing character data");
            PushCharacterData(newlyVisiblePlayers);
        }
    }

    private void PushCharacterData(List<string> visiblePlayers)
    {
        if (visiblePlayers.Any() && _playerManager.LastCreatedCharacterData != null)
        {
            Task.Run(async () =>
            {
                await _apiController.PushCharacterData(_playerManager.LastCreatedCharacterData,
                    visiblePlayers);
            });
        }
    }

    private CachedPlayer CreateCachedPlayer(string hashedName)
    {
        return new CachedPlayer(hashedName, _ipcManager, _apiController, _dalamudUtil, _fileDbManager);
    }
}