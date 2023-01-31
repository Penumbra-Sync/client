using MareSynchronos.Factories;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using MareSynchronos.Models;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.Mediator;
#if DEBUG
#endif

namespace MareSynchronos.Managers;

public class PlayerManager : MediatorSubscriberBase, IDisposable
{
    private readonly ApiController _apiController;
    private readonly CharacterDataFactory _characterDataFactory;
    private readonly DalamudUtil _dalamudUtil;
    private readonly IpcManager _ipcManager;
    public API.Data.CharacterData? LastCreatedCharacterData { get; private set; }
    public Models.CharacterData PermanentDataCache { get; private set; } = new();
    private readonly Dictionary<ObjectKind, Func<bool>> _objectKindsToUpdate = new();

    private CancellationTokenSource? _playerChangedCts = new();
    private CancellationTokenSource _transientUpdateCts = new();

    private readonly List<PlayerRelatedObject> _playerRelatedObjects = new();

    public unsafe PlayerManager(ApiController apiController, IpcManager ipcManager,
        CharacterDataFactory characterDataFactory, DalamudUtil dalamudUtil,
        MareMediator mediator) : base(mediator)
    {
        Logger.Verbose("Creating " + nameof(PlayerManager));

        _apiController = apiController;
        _ipcManager = ipcManager;
        _characterDataFactory = characterDataFactory;
        _dalamudUtil = dalamudUtil;
        
        Mediator.Subscribe<CustomizePlusMessage>(this, (msg) => CustomizePlusChanged((CustomizePlusMessage)msg));
        Mediator.Subscribe<HeelsOffsetMessage>(this, (msg) => HeelsOffsetChanged((HeelsOffsetMessage)msg));
        Mediator.Subscribe<PalettePlusMessage>(this, (msg) => PalettePlusChanged((PalettePlusMessage)msg));
        Mediator.Subscribe<ConnectedMessage>(this, (_) => ApiControllerOnConnected());
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => ApiController_Disconnected());
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => DalamudUtilOnDelayedFrameworkUpdate());
        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => DalamudUtilOnFrameworkUpdate());
        Mediator.Subscribe<TransientResourceChangedMessage>(this, (msg) => HandleTransientResourceLoad((TransientResourceChangedMessage)msg));

        Logger.Debug("Watching Player, ApiController is Connected: " + _apiController.IsConnected);
        if (_apiController.IsConnected)
        {
            ApiControllerOnConnected();
        }

        _playerRelatedObjects = new List<PlayerRelatedObject>()
        {
            new(ObjectKind.Player, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.PlayerPointer),
            new(ObjectKind.MinionOrMount, IntPtr.Zero, IntPtr.Zero, () => (IntPtr)((Character*)_dalamudUtil.PlayerPointer)->CompanionObject),
            new(ObjectKind.Pet, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.GetPet()),
            new(ObjectKind.Companion, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.GetCompanion()),
        };
    }

    private void DalamudUtilOnFrameworkUpdate()
    {
        Mediator.Publish(new PlayerRelatedObjectPointerUpdateMessage(_playerRelatedObjects.Select(f => f.CurrentAddress).ToArray()));
    }

    public void HandleTransientResourceLoad(TransientResourceChangedMessage msg)
    {
        foreach (var obj in _playerRelatedObjects)
        {
            if (obj.Address == msg.Address && !obj.HasUnprocessedUpdate)
            {
                _transientUpdateCts.Cancel();
                _transientUpdateCts = new CancellationTokenSource();
                var token = _transientUpdateCts.Token;
                Task.Run(async () =>
                {
                    Logger.Debug("Delaying transient resource load update");
                    await Task.Delay(750, token).ConfigureAwait(false);
                    if (obj.HasUnprocessedUpdate || token.IsCancellationRequested) return;
                    Logger.Debug("Firing transient resource load update");
                    obj.HasTransientsUpdate = true;
                }, token);

                return;
            }
        }
    }

    private void HeelsOffsetChanged(HeelsOffsetMessage change)
    {
        var player = _playerRelatedObjects.First(f => f.ObjectKind == ObjectKind.Player);
        if (LastCreatedCharacterData != null && LastCreatedCharacterData.HeelsOffset != change.Offset && !player.IsProcessing)
        {
            Logger.Debug("Heels offset changed to " + change.Offset);
            player.HasTransientsUpdate = true;
        }
    }

    private void CustomizePlusChanged(CustomizePlusMessage msg)
    {
        var change = msg.Data ?? string.Empty;
        var player = _playerRelatedObjects.First(f => f.ObjectKind == ObjectKind.Player);
        if (LastCreatedCharacterData != null && !string.Equals(LastCreatedCharacterData.CustomizePlusData, change, StringComparison.Ordinal) && !player.IsProcessing)
        {
            Logger.Debug("CustomizePlus data changed to " + change);
            player.HasTransientsUpdate = true;
        }
    }

    private void PalettePlusChanged(PalettePlusMessage msg)
    {
        var change = msg.Data ?? string.Empty;
        var player = _playerRelatedObjects.First(f => f.ObjectKind == ObjectKind.Player);
        if (LastCreatedCharacterData != null && !string.Equals(LastCreatedCharacterData.PalettePlusData, change, StringComparison.Ordinal) && !player.IsProcessing)
        {
            Logger.Debug("PalettePlus data changed to " + change);
            player.HasTransientsUpdate = true;
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        _playerChangedCts?.Cancel();
    }

    private unsafe void DalamudUtilOnDelayedFrameworkUpdate()
    {
        if (!_dalamudUtil.IsPlayerPresent || !_ipcManager.Initialized) return;

        _playerRelatedObjects.ForEach(k => k.CheckAndUpdateObject());
        if (_playerRelatedObjects.Any(c => (c.HasUnprocessedUpdate || c.HasTransientsUpdate) && !c.IsProcessing))
        {
            OnPlayerOrAttachedObjectsChanged();
        }
    }

    private void ApiControllerOnConnected()
    {
        Logger.Debug("ApiController Connected");

        Mediator.Subscribe<PenumbraRedrawMessage>(this, (msg) => IpcManager_PenumbraRedrawEvent((PenumbraRedrawMessage)msg));
    }

    private void ApiController_Disconnected()
    {
        Logger.Debug(nameof(ApiController_Disconnected));

        Mediator.Unsubscribe<PenumbraRedrawMessage>(this);
    }

    private async Task<API.Data.CharacterData?> CreateFullCharacterCacheDto(CancellationToken token)
    {
        foreach (var unprocessedObject in _playerRelatedObjects.Where(c => c.HasUnprocessedUpdate || c.HasTransientsUpdate).ToList())
        {
            Logger.Verbose("Building Cache for " + unprocessedObject.ObjectKind);
            PermanentDataCache = _characterDataFactory.BuildCharacterData(PermanentDataCache, unprocessedObject, token);
            if (!token.IsCancellationRequested)
            {
                unprocessedObject.HasUnprocessedUpdate = false;
                unprocessedObject.IsProcessing = false;
                unprocessedObject.HasTransientsUpdate = false;
            }
            token.ThrowIfCancellationRequested();
        }

        int timeOut = 10000;
        while (!PermanentDataCache.IsReady && !token.IsCancellationRequested && timeOut >= 0)
        {
            Logger.Verbose("Waiting until cache is ready (Timeout: " + TimeSpan.FromMilliseconds(timeOut) + ")");
            await Task.Delay(50, token).ConfigureAwait(false);
            timeOut -= 50;
        }

        if (token.IsCancellationRequested || timeOut <= 0) return null;

        Logger.Verbose("Cache creation complete");

        var cache = PermanentDataCache.ToAPI();
        //Logger.Verbose(JsonConvert.SerializeObject(cache, Formatting.Indented));
        return cache;
    }

    private void IpcManager_PenumbraRedrawEvent(PenumbraRedrawMessage msg)
    {
        Logger.Verbose("RedrawEvent for addr " + msg.Address);

        foreach (var item in _playerRelatedObjects)
        {
            if (msg.Address == item.Address)
            {
                Logger.Debug("Penumbra redraw Event for " + item.ObjectKind);
                item.HasUnprocessedUpdate = true;
            }
        }

        if (_playerRelatedObjects.Any(c => (c.HasUnprocessedUpdate || c.HasTransientsUpdate) && (!c.IsProcessing || (c.IsProcessing && c.DoNotSendUpdate))))
        {
            OnPlayerOrAttachedObjectsChanged();
        }
    }

    private void OnPlayerOrAttachedObjectsChanged()
    {
        var unprocessedObjects = _playerRelatedObjects.Where(c => c.HasUnprocessedUpdate || c.HasTransientsUpdate).ToList();
        foreach (var unprocessedObject in unprocessedObjects)
        {
            unprocessedObject.IsProcessing = true;
        }
        Logger.Debug("Object(s) changed: " + string.Join(", ", unprocessedObjects.Select(c => c.ObjectKind)));
        bool doNotSendUpdate = unprocessedObjects.All(c => c.DoNotSendUpdate);
        unprocessedObjects.ForEach(p => p.DoNotSendUpdate = false);
        _playerChangedCts?.Cancel();
        _playerChangedCts = new CancellationTokenSource();
        var token = _playerChangedCts.Token;

        // fix for redraw from anamnesis
        while ((!_dalamudUtil.IsPlayerPresent || string.Equals(_dalamudUtil.PlayerName, "--", StringComparison.Ordinal)) && !token.IsCancellationRequested)
        {
            Logger.Debug("Waiting Until Player is Present");
            Thread.Sleep(100);
        }

        if (token.IsCancellationRequested)
        {
            Logger.Debug("Cancelled");
            return;
        }

        if (!_ipcManager.Initialized)
        {
            Logger.Warn("Penumbra not active, doing nothing.");
            return;
        }

        Task.Run(async () =>
        {
            API.Data.CharacterData? cacheData = null;
            try
            {
                Mediator.Publish(new HaltScanMessage("Character creation"));
                foreach (var item in unprocessedObjects)
                {
                    _dalamudUtil.WaitWhileCharacterIsDrawing("self " + item.ObjectKind.ToString(), item.Address, item.ObjectKind == ObjectKind.MinionOrMount ? 1000 : 10000, token);
                }

                cacheData = (await CreateFullCharacterCacheDto(token).ConfigureAwait(false));
            }
            catch { }
            finally
            {
                Mediator.Publish(new ResumeScanMessage("Character creation"));
            }
            if (cacheData == null || token.IsCancellationRequested) return;

#if DEBUG
            //var json = JsonConvert.SerializeObject(cacheDto, Formatting.Indented);
            //Logger.Verbose(json);
#endif

            if (string.Equals(LastCreatedCharacterData?.DataHash.Value ?? string.Empty, cacheData.DataHash.Value, StringComparison.Ordinal))
            {
                Logger.Debug("Not sending data, already sent");
                return;
            }

            LastCreatedCharacterData = cacheData;

            if (_apiController.IsConnected && !token.IsCancellationRequested && !doNotSendUpdate)
            {
                Logger.Verbose("Invoking PlayerHasChanged");
                Mediator.Publish(new PlayerChangedMessage(cacheData));
            }
        }, token);
    }
}
