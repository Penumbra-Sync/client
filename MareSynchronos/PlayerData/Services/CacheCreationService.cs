using MareSynchronos.API.Data.Enum;
using MareSynchronos.PlayerData.Data;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Services;

public sealed class CacheCreationService : DisposableMediatorSubscriberBase
{
    private readonly SemaphoreSlim _cacheCreateLock = new(1);
    private readonly Dictionary<ObjectKind, GameObjectHandler> _cachesToCreate = new();
    private readonly PlayerDataFactory _characterDataFactory;
    private readonly CancellationTokenSource _cts = new();
    private readonly CharacterData _playerData = new();
    private readonly Dictionary<ObjectKind, GameObjectHandler> _playerRelatedObjects = new();
    private Task? _cacheCreationTask;
    private CancellationTokenSource _honorificCts = new();
    private CancellationTokenSource _palettePlusCts = new();

    public CacheCreationService(ILogger<CacheCreationService> logger, MareMediator mediator, Func<ObjectKind, Func<IntPtr>, bool, GameObjectHandler> gameObjectHandlerFactory,
        PlayerDataFactory characterDataFactory, DalamudUtilService dalamudUtil) : base(logger, mediator)
    {
        _characterDataFactory = characterDataFactory;

        Mediator.Subscribe<CreateCacheForObjectMessage>(this, (msg) =>
        {
            Logger.LogDebug("Received CreateCacheForObject for {handler}, updating player", msg.ObjectToCreateFor);
            _cacheCreateLock.Wait();
            _cachesToCreate[msg.ObjectToCreateFor.ObjectKind] = msg.ObjectToCreateFor;
            _cacheCreateLock.Release();
        });

        Mediator.Subscribe<ClearCacheForObjectMessage>(this, (msg) =>
        {
            Task.Run(() =>
            {
                _playerData.FileReplacements.Remove(msg.ObjectToCreateFor.ObjectKind);
                _playerData.GlamourerString.Remove(msg.ObjectToCreateFor.ObjectKind);
                Mediator.Publish(new CharacterDataCreatedMessage(_playerData.ToAPI()));
            });
        });

        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (msg) => ProcessCacheCreation());
        Mediator.Subscribe<CustomizePlusMessage>(this, async (_) =>
        {
            Logger.LogDebug("Received CustomizePlus change, updating player");
            await AddPlayerCacheToCreate().ConfigureAwait(false);
        });
        Mediator.Subscribe<HeelsOffsetMessage>(this, async (_) =>
        {
            Logger.LogDebug("Received Heels Offset change, updating player");
            await AddPlayerCacheToCreate().ConfigureAwait(false);
        });
        Mediator.Subscribe<PalettePlusMessage>(this, (msg) =>
        {
            if (msg.Character.Address == _playerRelatedObjects[ObjectKind.Player].Address)
            {
                Logger.LogDebug("Received PalettePlus change, updating player");
                PalettePlusChanged();
            }
        });
        Mediator.Subscribe<HonorificMessage>(this, (msg) =>
        {
            if (!string.Equals(msg.NewHonorificTitle, _playerData.HonorificData, StringComparison.Ordinal))
            {
                Logger.LogDebug("Received Honorific change, updating player");
                HonorificChanged();
            }
        });
        Mediator.Subscribe<PenumbraModSettingChangedMessage>(this, async (msg) =>
        {
            Logger.LogDebug("Received Penumbra Mod settings change, updating player");
            await AddPlayerCacheToCreate().ConfigureAwait(false);
        });

        _playerRelatedObjects[ObjectKind.Player] =
            gameObjectHandlerFactory(ObjectKind.Player, () => dalamudUtil.PlayerPointer, true);
        _playerRelatedObjects[ObjectKind.MinionOrMount] =
            gameObjectHandlerFactory(ObjectKind.MinionOrMount, () => dalamudUtil.GetMinionOrMount(), true);
        _playerRelatedObjects[ObjectKind.Pet] =
            gameObjectHandlerFactory(ObjectKind.Pet, () => dalamudUtil.GetPet().GetAwaiter().GetResult(), true);
        _playerRelatedObjects[ObjectKind.Companion] =
            gameObjectHandlerFactory(ObjectKind.Companion, () => dalamudUtil.GetCompanion().GetAwaiter().GetResult(), true);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _playerRelatedObjects.Values.ToList().ForEach(p => p.Dispose());
        _cts.Dispose();
    }

    private async Task AddPlayerCacheToCreate()
    {
        await _cacheCreateLock.WaitAsync().ConfigureAwait(false);
        _cachesToCreate[ObjectKind.Player] = _playerRelatedObjects[ObjectKind.Player];
        _cacheCreateLock.Release();
    }

    private void HonorificChanged()
    {
        _honorificCts?.Cancel();
        _honorificCts?.Dispose();
        _honorificCts = new();
        var token = _honorificCts.Token;

        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            await AddPlayerCacheToCreate().ConfigureAwait(false);
        }, token);
    }

    private void PalettePlusChanged()
    {
        _palettePlusCts?.Cancel();
        _palettePlusCts?.Dispose();
        _palettePlusCts = new();
        var token = _palettePlusCts.Token;

        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            await AddPlayerCacheToCreate().ConfigureAwait(false);
        }, token);
    }

    private void ProcessCacheCreation()
    {
        if (_cachesToCreate.Any() && (_cacheCreationTask?.IsCompleted ?? true))
        {
            _cacheCreateLock.Wait();
            var toCreate = _cachesToCreate.ToList();
            _cachesToCreate.Clear();
            _cacheCreateLock.Release();

            _cacheCreationTask = Task.Run(async () =>
            {
                try
                {
                    foreach (var obj in toCreate)
                    {
                        await _characterDataFactory.BuildCharacterData(_playerData, obj.Value, _cts.Token).ConfigureAwait(false);
                    }

                    int maxWaitingTime = 10000;
                    while (!_playerData.IsReady && maxWaitingTime > 0)
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                        maxWaitingTime -= 100;
                        Logger.LogTrace("Waiting for Cache to be ready");
                    }

                    Mediator.Publish(new CharacterDataCreatedMessage(_playerData.ToAPI()));
                }
                catch (Exception ex)
                {
                    Logger.LogCritical(ex, "Error during Cache Creation Processing");
                }
                finally
                {
                    Logger.LogDebug("Cache Creation complete");
                }
            }, _cts.Token);
        }
        else if (_cachesToCreate.Any())
        {
            Logger.LogDebug("Cache Creation stored until previous creation finished");
        }
    }
}