using MareSynchronos.Factories;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using MareSynchronos.Models;
using MareSynchronos.FileCache;
using MareSynchronos.UI;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.Delegates;
#if DEBUG
#endif

namespace MareSynchronos.Managers;


public class PlayerManager : IDisposable
{
    private readonly ApiController _apiController;
    private readonly CharacterDataFactory _characterDataFactory;
    private readonly DalamudUtil _dalamudUtil;
    private readonly TransientResourceManager _transientResourceManager;
    private readonly PeriodicFileScanner _periodicFileScanner;
    private readonly SettingsUi _settingsUi;
    private readonly IpcManager _ipcManager;
    public event CharacterDataDelegate? PlayerHasChanged;
    public API.Data.CharacterData? LastCreatedCharacterData { get; private set; }
    public Models.CharacterData PermanentDataCache { get; private set; } = new();
    private readonly Dictionary<ObjectKind, Func<bool>> _objectKindsToUpdate = new();

    private CancellationTokenSource? _playerChangedCts = new();
    private CancellationTokenSource _transientUpdateCts = new();

    private readonly List<PlayerRelatedObject> _playerRelatedObjects = new();

    public unsafe PlayerManager(ApiController apiController, IpcManager ipcManager,
        CharacterDataFactory characterDataFactory, DalamudUtil dalamudUtil, TransientResourceManager transientResourceManager,
        PeriodicFileScanner periodicFileScanner, SettingsUi settingsUi)
    {
        Logger.Verbose("Creating " + nameof(PlayerManager));

        _apiController = apiController;
        _ipcManager = ipcManager;
        _characterDataFactory = characterDataFactory;
        _dalamudUtil = dalamudUtil;
        _transientResourceManager = transientResourceManager;
        _periodicFileScanner = periodicFileScanner;
        _settingsUi = settingsUi;
        _apiController.Connected += ApiControllerOnConnected;
        _apiController.Disconnected += ApiController_Disconnected;
        _transientResourceManager.TransientResourceLoaded += HandleTransientResourceLoad;
        _dalamudUtil.DelayedFrameworkUpdate += DalamudUtilOnDelayedFrameworkUpdate;
        _ipcManager.HeelsOffsetChangeEvent += HeelsOffsetChanged;
        _ipcManager.CustomizePlusScaleChange += CustomizePlusChanged;
        _dalamudUtil.FrameworkUpdate += DalamudUtilOnFrameworkUpdate;


        Logger.Debug("Watching Player, ApiController is Connected: " + _apiController.IsConnected);
        if (_apiController.IsConnected)
        {
            ApiControllerOnConnected();
        }

        _playerRelatedObjects = new List<PlayerRelatedObject>()
        {
            new PlayerRelatedObject(ObjectKind.Player, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.PlayerPointer),
            new PlayerRelatedObject(ObjectKind.MinionOrMount, IntPtr.Zero, IntPtr.Zero, () => (IntPtr)((Character*)_dalamudUtil.PlayerPointer)->CompanionObject),
            new PlayerRelatedObject(ObjectKind.Pet, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.GetPet()),
            new PlayerRelatedObject(ObjectKind.Companion, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.GetCompanion()),
        };
    }

    private void DalamudUtilOnFrameworkUpdate()
    {
        _transientResourceManager.PlayerRelatedPointers = _playerRelatedObjects.Select(f => f.CurrentAddress).ToArray();
    }

    public void HandleTransientResourceLoad(IntPtr gameObj, int idx)
    {
        foreach (var obj in _playerRelatedObjects)
        {
            if (obj.Address == gameObj && !obj.HasUnprocessedUpdate)
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

    private void HeelsOffsetChanged(float change)
    {
        var player = _playerRelatedObjects.First(f => f.ObjectKind == ObjectKind.Player);
        if (LastCreatedCharacterData != null && LastCreatedCharacterData.HeelsOffset != change && !player.IsProcessing)
        {
            Logger.Debug("Heels offset changed to " + change);
            player.HasTransientsUpdate = true;
        }
    }

    private void CustomizePlusChanged(string? change)
    {
        change ??= string.Empty;
        var player = _playerRelatedObjects.First(f => f.ObjectKind == ObjectKind.Player);
        if (LastCreatedCharacterData != null && !string.Equals(LastCreatedCharacterData.CustomizePlusData, change, StringComparison.Ordinal) && !player.IsProcessing)
        {
            Logger.Debug("CustomizePlus data changed to " + change);
            player.HasTransientsUpdate = true;
        }
    }

    public void Dispose()
    {
        Logger.Verbose("Disposing " + nameof(PlayerManager));

        _apiController.Connected -= ApiControllerOnConnected;
        _apiController.Disconnected -= ApiController_Disconnected;

        _ipcManager.PenumbraRedrawEvent -= IpcManager_PenumbraRedrawEvent;
        _dalamudUtil.DelayedFrameworkUpdate -= DalamudUtilOnDelayedFrameworkUpdate;
        _dalamudUtil.FrameworkUpdate -= DalamudUtilOnFrameworkUpdate;

        _transientResourceManager.TransientResourceLoaded -= HandleTransientResourceLoad;

        _playerChangedCts?.Cancel();
        _ipcManager.HeelsOffsetChangeEvent -= HeelsOffsetChanged;
        _ipcManager.CustomizePlusScaleChange -= CustomizePlusChanged;
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

        _ipcManager.PenumbraRedrawEvent += IpcManager_PenumbraRedrawEvent;
    }

    private void ApiController_Disconnected()
    {
        Logger.Debug(nameof(ApiController_Disconnected));

        _ipcManager.PenumbraRedrawEvent -= IpcManager_PenumbraRedrawEvent;
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

    private void IpcManager_PenumbraRedrawEvent(IntPtr address, int idx)
    {
        Logger.Verbose("RedrawEvent for addr " + address);

        foreach (var item in _playerRelatedObjects)
        {
            if (address == item.Address)
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
            API.Data.CharacterData? cacheDto = null;
            try
            {
                _periodicFileScanner.HaltScan("Character creation");
                foreach (var item in unprocessedObjects)
                {
                    _dalamudUtil.WaitWhileCharacterIsDrawing("self " + item.ObjectKind.ToString(), item.Address, item.ObjectKind == ObjectKind.MinionOrMount ? 1000 : 10000, token);
                }

                cacheDto = (await CreateFullCharacterCacheDto(token).ConfigureAwait(false));
            }
            catch { }
            finally
            {
                _periodicFileScanner.ResumeScan("Character creation");
            }
            if (cacheDto == null || token.IsCancellationRequested) return;

            _settingsUi.LastCreatedCharacterData = cacheDto;

#if DEBUG
            //var json = JsonConvert.SerializeObject(cacheDto, Formatting.Indented);
            //Logger.Verbose(json);
#endif

            if (string.Equals(LastCreatedCharacterData?.DataHash.Value ?? string.Empty, cacheDto.DataHash.Value, StringComparison.Ordinal))
            {
                Logger.Debug("Not sending data, already sent");
                return;
            }

            LastCreatedCharacterData = cacheDto;

            if (_apiController.IsConnected && !token.IsCancellationRequested && !doNotSendUpdate)
            {
                Logger.Verbose("Invoking PlayerHasChanged");
                PlayerHasChanged?.Invoke(cacheDto);
            }
        }, token);
    }
}
