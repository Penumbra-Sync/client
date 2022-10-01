using MareSynchronos.Factories;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using System;
using System.Threading;
using System.Threading.Tasks;
using MareSynchronos.API;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Collections.Generic;
using System.Linq;
using MareSynchronos.Models;
using MareSynchronos.FileCache;
#if DEBUG
using Newtonsoft.Json;
#endif

namespace MareSynchronos.Managers;

public delegate void PlayerHasChanged(CharacterCacheDto characterCache);

public class PlayerManager : IDisposable
{
    private readonly ApiController _apiController;
    private readonly CharacterDataFactory _characterDataFactory;
    private readonly DalamudUtil _dalamudUtil;
    private readonly TransientResourceManager _transientResourceManager;
    private readonly PeriodicFileScanner _periodicFileScanner;
    private readonly IpcManager _ipcManager;
    public event PlayerHasChanged? PlayerHasChanged;
    public CharacterCacheDto? LastCreatedCharacterData { get; private set; }
    public CharacterData PermanentDataCache { get; private set; } = new();
    private readonly Dictionary<ObjectKind, Func<bool>> objectKindsToUpdate = new();

    private CancellationTokenSource? _playerChangedCts = new();
    private CancellationTokenSource _transientUpdateCts = new();

    private List<PlayerRelatedObject> playerRelatedObjects = new List<PlayerRelatedObject>();

    public unsafe PlayerManager(ApiController apiController, IpcManager ipcManager,
        CharacterDataFactory characterDataFactory, DalamudUtil dalamudUtil, TransientResourceManager transientResourceManager,
        PeriodicFileScanner periodicFileScanner)
    {
        Logger.Verbose("Creating " + nameof(PlayerManager));

        _apiController = apiController;
        _ipcManager = ipcManager;
        _characterDataFactory = characterDataFactory;
        _dalamudUtil = dalamudUtil;
        _transientResourceManager = transientResourceManager;
        _periodicFileScanner = periodicFileScanner;
        _apiController.Connected += ApiControllerOnConnected;
        _apiController.Disconnected += ApiController_Disconnected;
        _transientResourceManager.TransientResourceLoaded += HandleTransientResourceLoad;
        _dalamudUtil.DelayedFrameworkUpdate += DalamudUtilOnDelayedFrameworkUpdate;
        _ipcManager.HeelsOffsetChangeEvent += HeelsOffsetChanged;
        _dalamudUtil.FrameworkUpdate += DalamudUtilOnFrameworkUpdate;


        Logger.Debug("Watching Player, ApiController is Connected: " + _apiController.IsConnected);
        if (_apiController.IsConnected)
        {
            ApiControllerOnConnected();
        }

        playerRelatedObjects = new List<PlayerRelatedObject>()
        {
            new PlayerRelatedObject(ObjectKind.Player, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.PlayerPointer),
            new PlayerRelatedObject(ObjectKind.MinionOrMount, IntPtr.Zero, IntPtr.Zero, () => (IntPtr)((Character*)_dalamudUtil.PlayerPointer)->CompanionObject),
            new PlayerRelatedObject(ObjectKind.Pet, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.GetPet()),
            new PlayerRelatedObject(ObjectKind.Companion, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.GetCompanion()),
        };
    }

    private void DalamudUtilOnFrameworkUpdate()
    {
        _transientResourceManager.PlayerRelatedPointers = playerRelatedObjects.Select(f => f.CurrentAddress).ToArray();
    }

    public void HandleTransientResourceLoad(IntPtr gameObj)
    {
        foreach (var obj in playerRelatedObjects)
        {
            if (obj.Address == gameObj && !obj.HasUnprocessedUpdate)
            {
                _transientUpdateCts.Cancel();
                _transientUpdateCts = new CancellationTokenSource();
                var token = _transientUpdateCts.Token;
                Task.Run(async () =>
                {
                    Logger.Debug("Delaying transient resource load update");
                    await Task.Delay(750, token);
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
        var player = playerRelatedObjects.First(f => f.ObjectKind == ObjectKind.Player);
        if (LastCreatedCharacterData != null && LastCreatedCharacterData.HeelsOffset != change && !player.IsProcessing)
        {
            Logger.Debug("Heels offset changed to " + change);
            playerRelatedObjects.First(f => f.ObjectKind == ObjectKind.Player).HasTransientsUpdate = true;
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
    }

    private unsafe void DalamudUtilOnDelayedFrameworkUpdate()
    {
        if (!_dalamudUtil.IsPlayerPresent || !_ipcManager.Initialized) return;

        playerRelatedObjects.ForEach(k => k.CheckAndUpdateObject());
        if (playerRelatedObjects.Any(c => (c.HasUnprocessedUpdate || c.HasTransientsUpdate) && !c.IsProcessing))
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

    private async Task<CharacterCacheDto?> CreateFullCharacterCacheDto(CancellationToken token)
    {
        foreach (var unprocessedObject in playerRelatedObjects.Where(c => c.HasUnprocessedUpdate || c.HasTransientsUpdate).ToList())
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

        while (!PermanentDataCache.IsReady && !token.IsCancellationRequested)
        {
            Logger.Verbose("Waiting until cache is ready");
            await Task.Delay(50, token);
        }

        if (token.IsCancellationRequested) return null;

        Logger.Verbose("Cache creation complete");

        var cache = PermanentDataCache.ToCharacterCacheDto();
        //Logger.Verbose(JsonConvert.SerializeObject(cache, Formatting.Indented));
        return cache;
    }

    private void IpcManager_PenumbraRedrawEvent(IntPtr address, int idx)
    {
        Logger.Verbose("RedrawEvent for addr " + address);

        foreach (var item in playerRelatedObjects)
        {
            if (address == item.Address)
            {
                Logger.Debug("Penumbra redraw Event for " + item.ObjectKind);
                item.HasUnprocessedUpdate = true;
            }
        }

        if (playerRelatedObjects.Any(c => (c.HasUnprocessedUpdate || c.HasTransientsUpdate) && (!c.IsProcessing || (c.IsProcessing && c.DoNotSendUpdate))))
        {
            OnPlayerOrAttachedObjectsChanged();
        }
    }

    private void OnPlayerOrAttachedObjectsChanged()
    {
        var unprocessedObjects = playerRelatedObjects.Where(c => c.HasUnprocessedUpdate || c.HasTransientsUpdate).ToList();
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
        while ((!_dalamudUtil.IsPlayerPresent || _dalamudUtil.PlayerName == "--") && !token.IsCancellationRequested)
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
            CharacterCacheDto? cacheDto = null;
            try
            {
                _periodicFileScanner.HaltScan("Character creation");
                foreach (var item in unprocessedObjects)
                {
                    _dalamudUtil.WaitWhileCharacterIsDrawing("self " + item.ObjectKind.ToString(), item.Address, 10000, token);
                }

                cacheDto = (await CreateFullCharacterCacheDto(token));
            }
            catch { }
            finally
            {
                _periodicFileScanner.ResumeScan("Character creation");
            }
            if (cacheDto == null || token.IsCancellationRequested) return;

#if DEBUG
            //var json = JsonConvert.SerializeObject(cacheDto, Formatting.Indented);
            //Logger.Verbose(json);
#endif

            if ((LastCreatedCharacterData?.GetHashCode() ?? 0) == cacheDto.GetHashCode())
            {
                Logger.Debug("Not sending data, already sent");
                return;
            }
            else
            {
                LastCreatedCharacterData = cacheDto;
            }

            if (_apiController.IsConnected && !token.IsCancellationRequested && !doNotSendUpdate)
            {
                Logger.Verbose("Invoking PlayerHasChanged");
                PlayerHasChanged?.Invoke(cacheDto);
            }
        }, token);
    }
}
