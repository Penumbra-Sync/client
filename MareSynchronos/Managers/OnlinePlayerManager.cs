using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.User;
using MareSynchronos.FileCache;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Managers;

public class OnlinePlayerManager : IDisposable
{
    private readonly ApiController _apiController;
    private readonly DalamudUtil _dalamudUtil;
    private readonly PlayerManager _playerManager;
    private readonly FileCacheManager _fileDbManager;
    private readonly PairManager _pairManager;

    public OnlinePlayerManager(ApiController apiController, DalamudUtil dalamudUtil, PlayerManager playerManager, FileCacheManager fileDbManager, PairManager pairManager)
    {
        Logger.Verbose("Creating " + nameof(OnlinePlayerManager));

        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _playerManager = playerManager;
        _fileDbManager = fileDbManager;
        _pairManager = pairManager;

        _playerManager.PlayerHasChanged += PlayerManagerOnPlayerHasChanged;

        _dalamudUtil.LogIn += DalamudUtilOnLogIn;
        _dalamudUtil.LogOut += DalamudUtilOnLogOut;

        if (_dalamudUtil.IsLoggedIn)
        {
            DalamudUtilOnLogIn();
        }
    }

    private void PlayerManagerOnPlayerHasChanged(CharacterData characterCache)
    {
        PushCharacterData(_pairManager.VisibleUsers);
    }

    private void DalamudUtilOnLogIn()
    {
        _dalamudUtil.DelayedFrameworkUpdate += FrameworkOnUpdate;
    }

    private void DalamudUtilOnLogOut()
    {
        _dalamudUtil.DelayedFrameworkUpdate -= FrameworkOnUpdate;
    }

    public void Dispose()
    {
        Logger.Verbose("Disposing " + nameof(OnlinePlayerManager));

        _playerManager.PlayerHasChanged -= PlayerManagerOnPlayerHasChanged;
        _dalamudUtil.LogIn -= DalamudUtilOnLogIn;
        _dalamudUtil.LogOut -= DalamudUtilOnLogOut;
        _dalamudUtil.DelayedFrameworkUpdate -= FrameworkOnUpdate;
    }

    private void FrameworkOnUpdate()
    {
        if (!_dalamudUtil.IsPlayerPresent || !_apiController.IsConnected) return;

        var playerCharacters = _dalamudUtil.GetPlayerCharacters();
        var onlinePairs = _pairManager.OnlineUserPairs;
        foreach (var pChar in playerCharacters)
        {
            var pair = _pairManager.FindPair(pChar);
            if (pair == null) continue;

            pair.InitializePair(pChar.Address, pChar.Name.ToString());
        }

        var newlyVisiblePlayers = onlinePairs.Select(v => v.CachedPlayer)
            .Where(p => p.PlayerCharacter != IntPtr.Zero && p.IsVisible && !p.WasVisible).Select(p => (UserDto)p.OnlineUser)
            .ToList();
        if (newlyVisiblePlayers.Any())
        {
            Logger.Verbose("Has new visible players, pushing character data");
            PushCharacterData(newlyVisiblePlayers.Select(c => c.User).ToList());
        }
    }

    private void PushCharacterData(List<UserData> visiblePlayers)
    {
        if (visiblePlayers.Any() && _playerManager.LastCreatedCharacterData != null)
        {
            Task.Run(async () =>
            {
                await _apiController.PushCharacterData(_playerManager.LastCreatedCharacterData, visiblePlayers).ConfigureAwait(false);
            });
        }
    }
}