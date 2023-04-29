using MareSynchronos.API.Data;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Pairs;

public class OnlinePlayerManager : DisposableMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileUploadManager _fileTransferManager;
    private readonly HashSet<CachedPlayer> _newVisiblePlayers = new();
    private readonly PairManager _pairManager;
    private CharacterData? _lastSentData;

    public OnlinePlayerManager(ILogger<OnlinePlayerManager> logger, ApiController apiController, DalamudUtilService dalamudUtil,
        PairManager pairManager, MareMediator mediator, FileUploadManager fileTransferManager) : base(logger, mediator)
    {
        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _pairManager = pairManager;
        _fileTransferManager = fileTransferManager;
        Mediator.Subscribe<PlayerChangedMessage>(this, (_) => PlayerManagerOnPlayerHasChanged());
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => FrameworkOnUpdate());
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) =>
        {
            var newData = msg.CharacterData;
            if (_lastSentData == null || (!string.Equals(newData.DataHash.Value, _lastSentData.DataHash.Value, StringComparison.Ordinal)))
            {
                Logger.LogDebug("Pushing data for visible players");
                _lastSentData = newData;
                PushCharacterData(_pairManager.GetVisibleUsers());
            }
            else
            {
                Logger.LogDebug("Not sending data for {hash}", newData.DataHash.Value);
            }
        });
        Mediator.Subscribe<CachedPlayerVisibleMessage>(this, (msg) => _newVisiblePlayers.Add(msg.Player));
    }

    private void FrameworkOnUpdate()
    {
        if (!_dalamudUtil.IsPlayerPresent || !_apiController.IsConnected) return;

        if (!_newVisiblePlayers.Any()) return;
        var newVisiblePlayers = _newVisiblePlayers.ToList();
        _newVisiblePlayers.Clear();
        Logger.LogTrace("Has new visible players, pushing character data");
        PushCharacterData(newVisiblePlayers.Select(c => c.OnlineUser.User).ToList());
    }

    private void PlayerManagerOnPlayerHasChanged()
    {
        PushCharacterData(_pairManager.GetVisibleUsers());
    }

    private void PushCharacterData(List<UserData> visiblePlayers)
    {
        if (visiblePlayers.Any() && _lastSentData != null)
        {
            Task.Run(async () =>
            {
                var dataToSend = await _fileTransferManager.UploadFiles(_lastSentData.DeepClone(), visiblePlayers).ConfigureAwait(false);
                await _apiController.PushCharacterData(dataToSend, visiblePlayers).ConfigureAwait(false);
            });
        }
    }
}