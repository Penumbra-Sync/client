using System.Collections.Concurrent;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.API.Dto.User;
using MareSynchronos.FileCache;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Managers;

public class OnlinePlayerManager : IDisposable
{
    private readonly ApiController _apiController;
    private readonly DalamudUtil _dalamudUtil;
    private readonly IpcManager _ipcManager;
    private readonly PlayerManager _playerManager;
    private readonly FileCacheManager _fileDbManager;
    private readonly Configuration _configuration;
    private readonly ConcurrentDictionary<UserDto, CachedPlayer> _onlineCachedPlayers = new(new UserDtoComparer());
    private readonly ConcurrentDictionary<UserDto, OnlineUserCharaDataDto> _temporaryStoredCharacterCache = new(new UserDtoComparer());
    private readonly ConcurrentDictionary<UserDto, OptionalPluginWarning> _shownWarnings = new(new UserDtoComparer());

    private List<UserDto> OnlineVisiblePlayerHashes => _onlineCachedPlayers.Select(p => p.Value).Where(p => p.PlayerCharacter != IntPtr.Zero)
        .Select(p => (UserDto)p.OnlineUser).ToList();

    public OnlinePlayerManager(ApiController apiController, DalamudUtil dalamudUtil, IpcManager ipcManager, PlayerManager playerManager, FileCacheManager fileDbManager, Configuration configuration)
    {
        Logger.Verbose("Creating " + nameof(OnlinePlayerManager));

        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _ipcManager = ipcManager;
        _playerManager = playerManager;
        _fileDbManager = fileDbManager;
        _configuration = configuration;
        _apiController.PairedClientOnline += ApiControllerOnPairedClientOnline;
        _apiController.PairedClientOffline += ApiControllerOnPairedClientOffline;
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

    private void ApiControllerOnCharacterReceived(OnlineUserCharaDataDto dto)
    {
        if (!_shownWarnings.ContainsKey(dto)) _shownWarnings[dto] = new()
        {
            ShownCustomizePlusWarning = _configuration.DisableOptionalPluginWarnings,
            ShownHeelsWarning = _configuration.DisableOptionalPluginWarnings,
        };
        if (_onlineCachedPlayers.TryGetValue(dto, out var visiblePlayer) && visiblePlayer.IsVisible)
        {
            Logger.Debug("Received data and applying to " + dto.User.AliasOrUID);
            visiblePlayer.ApplyCharacterData(dto.CharaData, _shownWarnings[dto]);
        }
        else
        {
            Logger.Debug("Received data but no fitting character visible for " + dto.User.AliasOrUID);
            _temporaryStoredCharacterCache[dto] = dto;
        }
    }

    private void PlayerManagerOnPlayerHasChanged(API.Data.CharacterData characterCache)
    {
        PushCharacterData(OnlineVisiblePlayerHashes);
    }

    private void ApiControllerOnConnected()
    {
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

    public void Dispose()
    {
        Logger.Verbose("Disposing " + nameof(OnlinePlayerManager));

        RestoreAllCharacters();

        _apiController.PairedClientOnline -= ApiControllerOnPairedClientOnline;
        _apiController.PairedClientOffline -= ApiControllerOnPairedClientOffline;
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

    private void ApiControllerOnPairedClientOffline(UserDto dto)
    {
        Logger.Debug("Player offline: " + dto);

        if (!_onlineCachedPlayers.TryGetValue(dto, out var cachedPlayer))
        {
            return;
        }

        cachedPlayer.DisposePlayer();
        _onlineCachedPlayers.TryRemove(dto, out _);
    }

    private void ApiControllerOnPairedClientOnline(OnlineUserIdentDto dto)
    {
        Logger.Debug("Player online: " + dto);

        if (_onlineCachedPlayers.ContainsKey(dto))
        {
            PushCharacterData(new List<UserDto>() { new(dto.User) });
            return;
        }

        _onlineCachedPlayers.TryAdd(dto, CreateCachedPlayer(dto));
    }

    private void FrameworkOnUpdate()
    {
        if (!_dalamudUtil.IsPlayerPresent || !_ipcManager.Initialized || !_apiController.IsConnected) return;

        var playerCharacters = _dalamudUtil.GetPlayerCharacters();
        foreach (var pChar in playerCharacters)
        {
            var hashedName = Crypto.GetHash256(pChar);
            var existingPlayer = _onlineCachedPlayers.FirstOrDefault(c => string.Equals(c.Value.PlayerNameHash, hashedName, StringComparison.Ordinal)).Value;
            if (existingPlayer == null) continue;

            if (!string.IsNullOrEmpty(existingPlayer.PlayerName))
            {
                existingPlayer.IsVisible = true;
                continue;
            }

            _temporaryStoredCharacterCache.TryRemove(existingPlayer.OnlineUser, out var cache);
            if (!_shownWarnings.ContainsKey(existingPlayer.OnlineUser)) _shownWarnings[existingPlayer.OnlineUser] = new()
            {
                ShownCustomizePlusWarning = _configuration.DisableOptionalPluginWarnings,
                ShownHeelsWarning = _configuration.DisableOptionalPluginWarnings,
            };
            existingPlayer.InitializePlayer(pChar.Address, pChar.Name.ToString(), cache?.CharaData ?? null, _shownWarnings[existingPlayer.OnlineUser]);
        }

        var newlyVisiblePlayers = _onlineCachedPlayers.Select(v => v.Value)
            .Where(p => p.PlayerCharacter != IntPtr.Zero && p.IsVisible && !p.WasVisible).Select(p => (UserDto)p.OnlineUser)
            .ToList();
        if (newlyVisiblePlayers.Any())
        {
            Logger.Verbose("Has new visible players, pushing character data");
            PushCharacterData(newlyVisiblePlayers);
        }
    }

    private void PushCharacterData(List<UserDto> visiblePlayers)
    {
        if (visiblePlayers.Any() && _playerManager.LastCreatedCharacterData != null)
        {
            Task.Run(async () =>
            {
                await _apiController.PushCharacterData(_playerManager.LastCreatedCharacterData, visiblePlayers.Select(c => c.User).ToList()).ConfigureAwait(false);
            });
        }
    }

    private CachedPlayer CreateCachedPlayer(OnlineUserIdentDto onlineUser)
    {
        return new CachedPlayer(onlineUser, _ipcManager, _apiController, _dalamudUtil, _fileDbManager);
    }
}