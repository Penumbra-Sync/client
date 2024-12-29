using MareSynchronos.API.Data.Enum;
using MareSynchronos.PlayerData.Data;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Services;

#pragma warning disable MA0040

public sealed class CacheCreationService : DisposableMediatorSubscriberBase
{
    private readonly SemaphoreSlim _cacheCreateLock = new(1);
    private readonly Dictionary<ObjectKind, GameObjectHandler> _cachesToCreate = [];
    private readonly PlayerDataFactory _characterDataFactory;
    private readonly CancellationTokenSource _cts = new();
    private readonly CharacterData _playerData = new();
    private readonly Dictionary<ObjectKind, GameObjectHandler> _playerRelatedObjects = [];
    private Task? _cacheCreationTask;
    private CancellationTokenSource _honorificCts = new();
    private CancellationTokenSource _moodlesCts = new();
    private CancellationTokenSource _petNicknamesCts = new();
    private bool _isZoning = false;
    private bool _haltCharaDataCreation;
    private readonly Dictionary<ObjectKind, CancellationTokenSource> _glamourerCts = new();

    public CacheCreationService(ILogger<CacheCreationService> logger, MareMediator mediator, GameObjectHandlerFactory gameObjectHandlerFactory,
        PlayerDataFactory characterDataFactory, DalamudUtilService dalamudUtil) : base(logger, mediator)
    {
        _characterDataFactory = characterDataFactory;

        Mediator.Subscribe<CreateCacheForObjectMessage>(this, (msg) =>
        {
            Logger.LogDebug("Received CreateCacheForObject for {handler}, updating", msg.ObjectToCreateFor);
            _cacheCreateLock.Wait();
            _cachesToCreate[msg.ObjectToCreateFor.ObjectKind] = msg.ObjectToCreateFor;
            _cacheCreateLock.Release();
        });

        Mediator.Subscribe<ZoneSwitchStartMessage>(this, (msg) => _isZoning = true);
        Mediator.Subscribe<ZoneSwitchEndMessage>(this, (msg) => _isZoning = false);

        Mediator.Subscribe<HaltCharaDataCreation>(this, (msg) =>
        {
            _haltCharaDataCreation = !msg.Resume;
        });

        _playerRelatedObjects[ObjectKind.Player] = gameObjectHandlerFactory.Create(ObjectKind.Player, dalamudUtil.GetPlayerPointer, isWatched: true)
            .GetAwaiter().GetResult();
        _playerRelatedObjects[ObjectKind.MinionOrMount] = gameObjectHandlerFactory.Create(ObjectKind.MinionOrMount, () => dalamudUtil.GetMinionOrMount(), isWatched: true)
            .GetAwaiter().GetResult();
        _playerRelatedObjects[ObjectKind.Pet] = gameObjectHandlerFactory.Create(ObjectKind.Pet, () => dalamudUtil.GetPet(), isWatched: true)
            .GetAwaiter().GetResult();
        _playerRelatedObjects[ObjectKind.Companion] = gameObjectHandlerFactory.Create(ObjectKind.Companion, () => dalamudUtil.GetCompanion(), isWatched: true)
            .GetAwaiter().GetResult();

        Mediator.Subscribe<ClassJobChangedMessage>(this, (msg) =>
        {
            if (msg.GameObjectHandler != _playerRelatedObjects[ObjectKind.Player]) return;

            Logger.LogTrace("Removing pet data for {obj}", msg.GameObjectHandler);
            _playerData.FileReplacements.Remove(ObjectKind.Pet);
            _playerData.GlamourerString.Remove(ObjectKind.Pet);
            _playerData.CustomizePlusScale.Remove(ObjectKind.Pet);
            Mediator.Publish(new CharacterDataCreatedMessage(_playerData.ToAPI()));
        });

        Mediator.Subscribe<ClearCacheForObjectMessage>(this, (msg) =>
        {
            // ignore pets
            if (msg.ObjectToCreateFor == _playerRelatedObjects[ObjectKind.Pet]) return;
            _ = Task.Run(() =>
            {
                Logger.LogTrace("Clearing cache for {obj}", msg.ObjectToCreateFor);
                _playerData.FileReplacements.Remove(msg.ObjectToCreateFor.ObjectKind);
                _playerData.GlamourerString.Remove(msg.ObjectToCreateFor.ObjectKind);
                _playerData.CustomizePlusScale.Remove(msg.ObjectToCreateFor.ObjectKind);
                Mediator.Publish(new CharacterDataCreatedMessage(_playerData.ToAPI()));
            });
        });

        Mediator.Subscribe<CustomizePlusMessage>(this, (msg) =>
        {
            if (_isZoning) return;
            _ = Task.Run(async () =>
            {

                foreach (var item in _playerRelatedObjects
                    .Where(item => msg.Address == null
                    || item.Value.Address == msg.Address).Select(k => k.Key))
                {
                    Logger.LogDebug("Received CustomizePlus change, updating {obj}", item);
                    await AddPlayerCacheToCreate(item).ConfigureAwait(false);
                }
            });
        });
        Mediator.Subscribe<HeelsOffsetMessage>(this, (msg) =>
        {
            if (_isZoning) return;
            Logger.LogDebug("Received Heels Offset change, updating player");
            _ = AddPlayerCacheToCreate();
        });
        Mediator.Subscribe<GlamourerChangedMessage>(this, (msg) =>
        {
            if (_isZoning) return;
            var changedType = _playerRelatedObjects.FirstOrDefault(f => f.Value.Address == msg.Address);
            if (!default(KeyValuePair<ObjectKind, GameObjectHandler>).Equals(changedType))
            {
                GlamourerChanged(changedType.Key);
            }
        });
        Mediator.Subscribe<HonorificMessage>(this, (msg) =>
        {
            if (_isZoning) return;
            if (!string.Equals(msg.NewHonorificTitle, _playerData.HonorificData, StringComparison.Ordinal))
            {
                Logger.LogDebug("Received Honorific change, updating player");
                HonorificChanged();
            }
        });
        Mediator.Subscribe<MoodlesMessage>(this, (msg) =>
        {
            if (_isZoning) return;
            var changedType = _playerRelatedObjects.FirstOrDefault(f => f.Value.Address == msg.Address);
            if (!default(KeyValuePair<ObjectKind, GameObjectHandler>).Equals(changedType) && changedType.Key == ObjectKind.Player)
            {
                Logger.LogDebug("Received Moodles change, updating player");
                MoodlesChanged();
            }
        });
        Mediator.Subscribe<PetNamesMessage>(this, (msg) =>
        {
            if (_isZoning) return;
            if (!string.Equals(msg.PetNicknamesData, _playerData.PetNamesData, StringComparison.Ordinal))
            {
                Logger.LogDebug("Received Pet Nicknames change, updating player");
                PetNicknamesChanged();
            }
        });
        Mediator.Subscribe<PenumbraModSettingChangedMessage>(this, (msg) =>
        {
            Logger.LogDebug("Received Penumbra Mod settings change, updating player");
            _ = AddPlayerCacheToCreate();
        });

        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (msg) => ProcessCacheCreation());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _playerRelatedObjects.Values.ToList().ForEach(p => p.Dispose());
        _cts.Dispose();
    }

    private async Task AddPlayerCacheToCreate(ObjectKind kind = ObjectKind.Player)
    {
        await _cacheCreateLock.WaitAsync().ConfigureAwait(false);
        _cachesToCreate[kind] = _playerRelatedObjects[kind];
        _cacheCreateLock.Release();
    }

    private void GlamourerChanged(ObjectKind kind)
    {
        if (_glamourerCts.TryGetValue(kind, out var cts))
        {
            _glamourerCts[kind]?.Cancel();
            _glamourerCts[kind]?.Dispose();
        }
        _glamourerCts[kind] = new();
        var token = _glamourerCts[kind].Token;

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            await AddPlayerCacheToCreate(kind).ConfigureAwait(false);
        });
    }

    private void HonorificChanged()
    {
        _honorificCts?.Cancel();
        _honorificCts?.Dispose();
        _honorificCts = new();
        var token = _honorificCts.Token;

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            await AddPlayerCacheToCreate().ConfigureAwait(false);
        }, token);
    }

    private void MoodlesChanged()
    {
        _moodlesCts?.Cancel();
        _moodlesCts?.Dispose();
        _moodlesCts = new();
        var token = _moodlesCts.Token;

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
            await AddPlayerCacheToCreate().ConfigureAwait(false);
        }, token);
    }

    private void PetNicknamesChanged()
    {
        _petNicknamesCts?.Cancel();
        _petNicknamesCts?.Dispose();
        _petNicknamesCts = new();
        var token = _petNicknamesCts.Token;

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            await AddPlayerCacheToCreate().ConfigureAwait(false);
        }, token);
    }

    private void ProcessCacheCreation()
    {
        if (_isZoning || _haltCharaDataCreation) return;

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
#pragma warning restore MA0040