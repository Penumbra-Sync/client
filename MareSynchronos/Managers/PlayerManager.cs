using MareSynchronos.Factories;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using System;
using System.Threading;
using System.Threading.Tasks;
using MareSynchronos.API;
using Penumbra.GameData.Structs;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Collections.Generic;
using System.Linq;
using MareSynchronos.Models;
using MareSynchronos.Interop;

namespace MareSynchronos.Managers
{
    public delegate void PlayerHasChanged(CharacterCacheDto characterCache);

    public class PlayerManager : IDisposable
    {
        private readonly ApiController _apiController;
        private readonly CharacterDataFactory _characterDataFactory;
        private readonly DalamudUtil _dalamudUtil;
        private readonly IpcManager _ipcManager;
        public event PlayerHasChanged? PlayerHasChanged;
        public CharacterCacheDto? LastCreatedCharacterData { get; private set; }
        public CharacterData PermanentDataCache { get; private set; } = new();
        private readonly Dictionary<ObjectKind, Func<bool>> objectKindsToUpdate = new();

        private CancellationTokenSource? _playerChangedCts = new();
        private DateTime _lastPlayerObjectCheck;
        private CharacterEquipment? _currentCharacterEquipment = new();

        private List<PlayerRelatedObject> playerRelatedObjects = new List<PlayerRelatedObject>();

        public unsafe PlayerManager(ApiController apiController, IpcManager ipcManager,
            CharacterDataFactory characterDataFactory, DalamudUtil dalamudUtil)
        {
            Logger.Verbose("Creating " + nameof(PlayerManager));

            _apiController = apiController;
            _ipcManager = ipcManager;
            _characterDataFactory = characterDataFactory;
            _dalamudUtil = dalamudUtil;

            _apiController.Connected += ApiControllerOnConnected;
            _apiController.Disconnected += ApiController_Disconnected;
            _dalamudUtil.FrameworkUpdate += DalamudUtilOnFrameworkUpdate;

            Logger.Debug("Watching Player, ApiController is Connected: " + _apiController.IsConnected);
            if (_apiController.IsConnected)
            {
                ApiControllerOnConnected();
            }

            playerRelatedObjects = new List<PlayerRelatedObject>()
            {
                new PlayerRelatedObject(ObjectKind.Player, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.PlayerPointer),
                new PlayerRelatedObject(ObjectKind.Minion, IntPtr.Zero, IntPtr.Zero, () => (IntPtr)((Character*)_dalamudUtil.PlayerPointer)->CompanionObject),
                new PlayerRelatedObject(ObjectKind.Pet, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.GetPet()),
                new PlayerRelatedObject(ObjectKind.Companion, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.GetCompanion()),
                new PlayerRelatedObject(ObjectKind.Mount, IntPtr.Zero, IntPtr.Zero, () => (IntPtr)((CharaExt*)_dalamudUtil.PlayerPointer)->Mount),
            };
        }

        public void Dispose()
        {
            Logger.Verbose("Disposing " + nameof(PlayerManager));

            _apiController.Connected -= ApiControllerOnConnected;
            _apiController.Disconnected -= ApiController_Disconnected;

            _ipcManager.PenumbraRedrawEvent -= IpcManager_PenumbraRedrawEvent;
            _dalamudUtil.FrameworkUpdate -= DalamudUtilOnFrameworkUpdate;
        }

        private unsafe void DalamudUtilOnFrameworkUpdate()
        {
            if (!_dalamudUtil.IsPlayerPresent || !_ipcManager.Initialized) return;

            if (DateTime.Now < _lastPlayerObjectCheck.AddSeconds(0.25)) return;

            playerRelatedObjects.ForEach(k => k.CheckAndUpdateObject());
            if (playerRelatedObjects.Any(c => c.HasUnprocessedUpdate && !c.IsProcessing))
            {
                OnPlayerOrAttachedObjectsChanged();
            }

            _lastPlayerObjectCheck = DateTime.Now;
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
            foreach (var unprocessedObject in playerRelatedObjects.Where(c => c.HasUnprocessedUpdate).ToList())
            {
                Logger.Verbose("Building Cache for " + unprocessedObject.ObjectKind);
                PermanentDataCache = _characterDataFactory.BuildCharacterData(PermanentDataCache, unprocessedObject.ObjectKind, unprocessedObject.Address, token);
                if (!token.IsCancellationRequested)
                {
                    unprocessedObject.HasUnprocessedUpdate = false;
                    unprocessedObject.IsProcessing = false;
                }
                token.ThrowIfCancellationRequested();
            }

            while (!PermanentDataCache.IsReady && !token.IsCancellationRequested)
            {
                await Task.Delay(50, token);
            }

            if (token.IsCancellationRequested) return null;

            Logger.Verbose("Cache creation complete");

            return PermanentDataCache.ToCharacterCacheDto();
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

            if (playerRelatedObjects.Any(c => c.HasUnprocessedUpdate && (!c.IsProcessing || (c.IsProcessing && c.DoNotSendUpdate))))
            {
                OnPlayerOrAttachedObjectsChanged();
            }
        }

        private void OnPlayerOrAttachedObjectsChanged()
        {
            if (_dalamudUtil.IsInGpose) return;

            var unprocessedObjects = playerRelatedObjects.Where(c => c.HasUnprocessedUpdate).ToList();
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
                _dalamudUtil.WaitWhileSelfIsDrawing(token);

                CharacterCacheDto? cacheDto = (await CreateFullCharacterCacheDto(token));
                if (cacheDto == null || token.IsCancellationRequested) return;

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
}
