using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.User;
using MareSynchronos.FileCache;
using MareSynchronos.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Managers;

public class OnlinePlayerManager : MediatorSubscriberBase, IDisposable
{
    private readonly ApiController _apiController;
    private readonly DalamudUtil _dalamudUtil;
    private readonly FileCacheManager _fileDbManager;
    private readonly PairManager _pairManager;
    private CharacterData? _lastSentData;

    public OnlinePlayerManager(ApiController apiController, DalamudUtil dalamudUtil,
        FileCacheManager fileDbManager, PairManager pairManager, MareMediator mediator) : base(mediator)
    {
        Logger.Verbose("Creating " + nameof(OnlinePlayerManager));

        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _fileDbManager = fileDbManager;
        _pairManager = pairManager;

        Mediator.Subscribe<PlayerChangedMessage>(this, (msg) => PlayerManagerOnPlayerHasChanged((PlayerChangedMessage)msg));
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) => DalamudUtilOnLogIn());
        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) => DalamudUtilOnLogOut());
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => FrameworkOnUpdate());
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) =>
        {
            var newData = ((CharacterDataCreatedMessage)msg).CharacterData.ToAPI();
            if (_lastSentData == null || _lastSentData != null && !string.Equals(newData.DataHash.Value, _lastSentData.DataHash.Value, StringComparison.Ordinal))
            {
                Logger.Debug("Pushing data for visible players");
                _lastSentData = newData;
                PushCharacterData(_pairManager.VisibleUsers);
            }
            else
            {
                Logger.Debug("Not sending data for " + newData.DataHash.Value);
            }
        });
    }

    private void PlayerManagerOnPlayerHasChanged(PlayerChangedMessage msg)
    {
        PushCharacterData(_pairManager.VisibleUsers);
    }

    private void DalamudUtilOnLogIn()
    {
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => FrameworkOnUpdate());
    }

    private void DalamudUtilOnLogOut()
    {
        Mediator.Unsubscribe<DelayedFrameworkUpdateMessage>(this);
    }

    public override void Dispose()
    {
        base.Dispose();
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
            .Where(p => p != null && p.PlayerCharacter != IntPtr.Zero && p.IsVisible && !p.WasVisible).Select(p => (UserDto)p!.OnlineUser)
            .ToList();
        if (newlyVisiblePlayers.Any())
        {
            Logger.Verbose("Has new visible players, pushing character data");
            PushCharacterData(newlyVisiblePlayers.Select(c => c.User).ToList());
        }
    }

    private void PushCharacterData(List<UserData> visiblePlayers)
    {
        if (visiblePlayers.Any() && _lastSentData != null)
        {
            Task.Run(async () =>
            {
                await _apiController.PushCharacterData(_lastSentData, visiblePlayers).ConfigureAwait(false);
            });
        }
    }
}