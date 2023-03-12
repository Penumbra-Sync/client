using MareSynchronos.API.Data.Enum;
using MareSynchronos.PlayerData.Data;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Services;

public class CacheCreationService : MediatorSubscriberBase
{
    private readonly PlayerDataFactory _characterDataFactory;
    private Task? _cacheCreationTask;
    private readonly Dictionary<ObjectKind, GameObjectHandler> _cachesToCreate = new();
    private readonly CharacterData _playerData = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<GameObjectHandler> _playerRelatedObjects = new();
    private CancellationTokenSource _palettePlusCts = new();
    private readonly SemaphoreSlim _cacheCreateLock = new(1);

    public CacheCreationService(ILogger<CacheCreationService> logger, MareMediator mediator, GameObjectHandlerFactory gameObjectHandlerFactory,
        PlayerDataFactory characterDataFactory, DalamudUtil dalamudUtil) : base(logger, mediator)
    {
        _characterDataFactory = characterDataFactory;

        Mediator.Subscribe<CreateCacheForObjectMessage>(this, (msg) =>
        {
            var actualMsg = (CreateCacheForObjectMessage)msg;
            _cacheCreateLock.Wait();
            _cachesToCreate[actualMsg.ObjectToCreateFor.ObjectKind] = actualMsg.ObjectToCreateFor;
            _cacheCreateLock.Release();
        });

        _playerRelatedObjects.AddRange(new List<GameObjectHandler>()
        {
            gameObjectHandlerFactory.Create(ObjectKind.Player, () => dalamudUtil.PlayerPointer, isWatched: true),
            gameObjectHandlerFactory.Create(ObjectKind.MinionOrMount, () => dalamudUtil.GetMinionOrMount(), isWatched: true),
            gameObjectHandlerFactory.Create(ObjectKind.Pet, () => dalamudUtil.GetPet(), isWatched: true),
            gameObjectHandlerFactory.Create(ObjectKind.Companion, () => dalamudUtil.GetCompanion(), isWatched: true),
        });

        Mediator.Subscribe<ClearCacheForObjectMessage>(this, (msg) =>
        {
            Task.Run(() =>
            {
                var actualMsg = (ClearCacheForObjectMessage)msg;
                _playerData.FileReplacements.Remove(actualMsg.ObjectToCreateFor.ObjectKind);
                _playerData.GlamourerString.Remove(actualMsg.ObjectToCreateFor.ObjectKind);
                Mediator.Publish(new CharacterDataCreatedMessage(_playerData.ToAPI()));
            });
        });

        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (msg) => ProcessCacheCreation());
        Mediator.Subscribe<CustomizePlusMessage>(this, async (_) => await AddPlayerCacheToCreate().ConfigureAwait(false));
        Mediator.Subscribe<HeelsOffsetMessage>(this, async (_) => await AddPlayerCacheToCreate().ConfigureAwait(false));
        Mediator.Subscribe<PalettePlusMessage>(this, (_) => PalettePlusChanged());
        Mediator.Subscribe<PenumbraModSettingChangedMessage>(this, async (msg) => await AddPlayerCacheToCreate().ConfigureAwait(false));
    }

    private async Task AddPlayerCacheToCreate()
    {
        await _cacheCreateLock.WaitAsync().ConfigureAwait(false);
        _cachesToCreate[ObjectKind.Player] = _playerRelatedObjects.First(p => p.ObjectKind == ObjectKind.Player);
        _cacheCreateLock.Release();
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _playerRelatedObjects.ForEach(p => p.Dispose());
        _cts.Dispose();
    }
}
