using MareSynchronos.API.Data.Enum;
using MareSynchronos.PlayerData.Data;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Services;

public class CacheCreationService : MediatorSubscriberBase, IDisposable
{
    private readonly PlayerDataFactory _characterDataFactory;
    private Task? _cacheCreationTask;
    private readonly Dictionary<ObjectKind, GameObjectHandler> _cachesToCreate = new();
    private readonly CharacterData _playerData = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<GameObjectHandler> _playerRelatedObjects = new();
    private CancellationTokenSource _palettePlusCts = new();

    public unsafe CacheCreationService(ILogger<CacheCreationService> logger, MareMediator mediator, GameObjectHandlerFactory gameObjectHandlerFactory,
        PlayerDataFactory characterDataFactory, DalamudUtil dalamudUtil) : base(logger, mediator)
    {
        _characterDataFactory = characterDataFactory;

        Mediator.Subscribe<CreateCacheForObjectMessage>(this, (msg) =>
        {
            var actualMsg = (CreateCacheForObjectMessage)msg;
            _cachesToCreate[actualMsg.ObjectToCreateFor.ObjectKind] = actualMsg.ObjectToCreateFor;
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
        Mediator.Subscribe<CustomizePlusMessage>(this, (msg) => CustomizePlusChanged((CustomizePlusMessage)msg));
        Mediator.Subscribe<HeelsOffsetMessage>(this, (msg) => HeelsOffsetChanged((HeelsOffsetMessage)msg));
        Mediator.Subscribe<PalettePlusMessage>(this, (msg) => PalettePlusChanged((PalettePlusMessage)msg));
        Mediator.Subscribe<PenumbraModSettingChangedMessage>(this, (msg) => _cachesToCreate[ObjectKind.Player] = _playerRelatedObjects.First(p => p.ObjectKind == ObjectKind.Player));
    }

    private void PalettePlusChanged(PalettePlusMessage msg)
    {
        if (!string.Equals(msg.Data, _playerData.PalettePlusPalette, StringComparison.Ordinal))
        {
            _playerData.PalettePlusPalette = msg.Data ?? string.Empty;

            _palettePlusCts?.Cancel();
            _palettePlusCts?.Dispose();
            _palettePlusCts = new();
            var token = _palettePlusCts.Token;

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                Mediator.Publish(new CharacterDataCreatedMessage(_playerData.ToAPI()));
            }, token);
        }
    }

    private void HeelsOffsetChanged(HeelsOffsetMessage msg)
    {
        if (msg.Offset != _playerData.HeelsOffset)
        {
            _playerData.HeelsOffset = msg.Offset;
            Mediator.Publish(new CharacterDataCreatedMessage(_playerData.ToAPI()));
        }
    }

    private void CustomizePlusChanged(CustomizePlusMessage msg)
    {
        if (!string.Equals(msg.Data, _playerData.CustomizePlusScale, StringComparison.Ordinal))
        {
            _playerData.CustomizePlusScale = msg.Data ?? string.Empty;
            Mediator.Publish(new CharacterDataCreatedMessage(_playerData.ToAPI()));
        }
    }

    private void ProcessCacheCreation()
    {
        if (_cachesToCreate.Any() && (_cacheCreationTask?.IsCompleted ?? true))
        {
            var toCreate = _cachesToCreate.ToList();
            _cachesToCreate.Clear();
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
                        _logger.LogTrace("Waiting for Cache to be ready");
                    }

                    Mediator.Publish(new CharacterDataCreatedMessage(_playerData.ToAPI()));
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Error during Cache Creation Processing");
                }
                finally
                {
                    _logger.LogDebug("Cache Creation complete");
                }
            }, _cts.Token);
        }
        else if (_cachesToCreate.Any())
        {
            _logger.LogDebug("Cache Creation stored until previous creation finished");
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _playerRelatedObjects.ForEach(p => p.Dispose());
        _cts.Dispose();
    }
}
