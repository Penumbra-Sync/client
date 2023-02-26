using MareSynchronos.API.Data.Enum;
using MareSynchronos.Factories;
using MareSynchronos.Mediator;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.Managers;

public class CacheCreationService : MediatorSubscriberBase, IDisposable
{
    private readonly CharacterDataFactory _characterDataFactory;
    private Task? _cacheCreationTask;
    private readonly Dictionary<ObjectKind, GameObjectHandler> _cachesToCreate = new();
    private readonly CharacterData _lastCreatedData = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<GameObjectHandler> _playerRelatedObjects = new();
    private CancellationTokenSource _palettePlusCts = new();
    public ConcurrentDictionary<int, List<DownloadFileTransfer>> CurrentDownloads { get; } = new();

    public unsafe CacheCreationService(ILogger<CacheCreationService> logger, MareMediator mediator, GameObjectHandlerFactory gameObjectHandlerFactory,
        CharacterDataFactory characterDataFactory, DalamudUtil dalamudUtil) : base(logger, mediator)
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
                _lastCreatedData.FileReplacements.Remove(actualMsg.ObjectToCreateFor.ObjectKind);
                _lastCreatedData.GlamourerString.Remove(actualMsg.ObjectToCreateFor.ObjectKind);
                Mediator.Publish(new CharacterDataCreatedMessage(_lastCreatedData));
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
        if (!string.Equals(msg.Data, _lastCreatedData.PalettePlusPalette, StringComparison.Ordinal))
        {
            _lastCreatedData.PalettePlusPalette = msg.Data ?? string.Empty;

            _palettePlusCts?.Cancel();
            _palettePlusCts?.Dispose();
            _palettePlusCts = new();
            var token = _palettePlusCts.Token;

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                Mediator.Publish(new CharacterDataCreatedMessage(_lastCreatedData));
            }, token);
        }
    }

    private void HeelsOffsetChanged(HeelsOffsetMessage msg)
    {
        if (msg.Offset != _lastCreatedData.HeelsOffset)
        {
            _lastCreatedData.HeelsOffset = msg.Offset;
            Mediator.Publish(new CharacterDataCreatedMessage(_lastCreatedData));
        }
    }

    private void CustomizePlusChanged(CustomizePlusMessage msg)
    {
        if (!string.Equals(msg.Data, _lastCreatedData.CustomizePlusScale, StringComparison.Ordinal))
        {
            _lastCreatedData.CustomizePlusScale = msg.Data ?? string.Empty;
            Mediator.Publish(new CharacterDataCreatedMessage(_lastCreatedData));
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
                        var data = await _characterDataFactory.BuildCharacterData(_lastCreatedData, obj.Value, _cts.Token).ConfigureAwait(false);
                    }
                    Mediator.Publish(new CharacterDataCreatedMessage(_lastCreatedData));
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
