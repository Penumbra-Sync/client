using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using MareSynchronos.API;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Utils;
using Newtonsoft.Json;

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
    private readonly Dictionary<CachedPlayer, CancellationTokenSource> _playerTokenDisposal = new();

    private List<string> OnlineVisiblePlayerHashes => _onlineCachedPlayers.Where(p => p.PlayerCharacter != null)
        .Select(p => p.PlayerNameHash).ToList();
    private DateTime _lastPlayerObjectCheck = DateTime.Now;

    public OnlinePlayerManager(Framework framework, ApiController apiController, DalamudUtil dalamudUtil, IpcManager ipcManager, PlayerManager playerManager)
    {
        Logger.Verbose("Creating " + nameof(OnlinePlayerManager));

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
        _framework.Update -= FrameworkOnUpdate;
    }

    private void DalamudUtilOnLogIn()
    {
        _framework.Update += FrameworkOnUpdate;
    }

    private void IpcManagerOnPenumbraDisposed()
    {
        _onlineCachedPlayers.ForEach(p => p.DisposePlayer());
    }

    private void ApiControllerOnDisconnected()
    {
        RestoreAllCharacters();
        _playerManager.PlayerHasChanged -= PlayerManagerOnPlayerHasChanged;
    }

    public void AddInitialPairs(List<string> apiTaskResult)
    {
        _onlineCachedPlayers.Clear();
        _onlineCachedPlayers.AddRange(apiTaskResult.Select(CreateCachedPlayer));
        Logger.Verbose("Online and paired users: " + string.Join(Environment.NewLine, _onlineCachedPlayers));
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

        _framework.Update -= FrameworkOnUpdate;

        _dalamudUtil.LogIn -= DalamudUtilOnLogIn;
        _dalamudUtil.LogOut -= DalamudUtilOnLogOut;
    }

    private void RestoreAllCharacters()
    {
        _onlineCachedPlayers.ForEach(p => p.DisposePlayer());
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
        if (_onlineCachedPlayers.Any(p => p.PlayerNameHash == characterNameHash))
        {
            PushCharacterData(new List<string>() { characterNameHash });
            _playerTokenDisposal.TryGetValue(_onlineCachedPlayers.Single(p => p.PlayerNameHash == characterNameHash), out var cancellationTokenSource);
            cancellationTokenSource?.Cancel();
            return;
        }
        _onlineCachedPlayers.Add(CreateCachedPlayer(characterNameHash));
    }

    private void RemovePlayer(string characterHash)
    {
        var cachedPlayer = _onlineCachedPlayers.First(p => p.PlayerNameHash == characterHash);
        if (_dalamudUtil.IsInGpose)
        {
            _playerTokenDisposal.TryGetValue(cachedPlayer, out var cancellationTokenSource);
            cancellationTokenSource?.Cancel();
            cachedPlayer.IsVisible = false;
            _playerTokenDisposal[cachedPlayer] = new CancellationTokenSource();
            cancellationTokenSource = _playerTokenDisposal[cachedPlayer];
            var token = cancellationTokenSource.Token;
            Task.Run(async () =>
            {
                Logger.Verbose("Cannot dispose Player, in GPose");
                while (_dalamudUtil.IsInGpose)
                {
                    await Task.Delay(TimeSpan.FromSeconds(0.5));
                    if (token.IsCancellationRequested) return;
                }

                cachedPlayer.DisposePlayer();
                _onlineCachedPlayers.RemoveAll(c => c.PlayerNameHash == cachedPlayer.PlayerNameHash);
            }, token);

            return;
        }

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
        if (newlyVisiblePlayers.Any())
        {
            Logger.Verbose("Has new visible players, pushing character data");
            PushCharacterData(newlyVisiblePlayers);
        }

        _lastPlayerObjectCheck = DateTime.Now;
    }

    private void PushCharacterData(List<string> visiblePlayers)
    {
        if (visiblePlayers.Any() && _playerManager.LastCreatedCharacterData != null)
        {
            Task.Run(async () =>
            {
                await _apiController.PushCharacterData(_playerManager.LastCreatedCharacterData!,
                    visiblePlayers);
            });
        }
    }

    private CachedPlayer CreateCachedPlayer(string hashedName)
    {
        return new CachedPlayer(hashedName, _ipcManager, _apiController, _dalamudUtil);
    }
}