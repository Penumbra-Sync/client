using MareSynchronos.Factories;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Newtonsoft.Json;
using System;
using System.Threading;
using System.Threading.Tasks;
using MareSynchronos.API;
using Penumbra.GameData.Structs;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Penumbra.GameData.ByteString;
using System.Collections.Generic;
using System.Linq;
using MareSynchronos.Models;
using MareSynchronos.Interop;

namespace MareSynchronos.Managers
{
    public delegate void PlayerHasChanged(CharacterCacheDto characterCache, ObjectKind objectKind);

    public class PlayerManager : IDisposable
    {
        private readonly ApiController _apiController;
        private readonly CharacterDataFactory _characterDataFactory;
        private readonly DalamudUtil _dalamudUtil;
        private readonly IpcManager _ipcManager;
        public event PlayerHasChanged? PlayerHasChanged;
        public bool SendingData { get; private set; }
        public Dictionary<ObjectKind, CharacterCacheDto?> LastSentCharacterData { get; private set; } = new();

        private Dictionary<ObjectKind, CancellationTokenSource?> _playerChangedCts = new();
        private DateTime _lastPlayerObjectCheck;
        private Dictionary<ObjectKind, CharacterEquipment?> _currentCharacterEquipment = new();

        private PlayerAttachedObject Minion;
        private PlayerAttachedObject Pet;
        private PlayerAttachedObject Companion;
        private PlayerAttachedObject Mount;

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

            Logger.Debug("Watching Player, ApiController is Connected: " + _apiController.IsConnected);
            if (_apiController.IsConnected)
            {
                ApiControllerOnConnected();
            }

            Minion = new PlayerAttachedObject(ObjectKind.Minion, IntPtr.Zero, IntPtr.Zero, () => (IntPtr)((Character*)_dalamudUtil.PlayerPointer)->CompanionObject);
            Pet = new PlayerAttachedObject(ObjectKind.Pet, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.GetPet());
            Companion = new PlayerAttachedObject(ObjectKind.Companion, IntPtr.Zero, IntPtr.Zero, () => _dalamudUtil.GetCompanion());
            Mount = new PlayerAttachedObject(ObjectKind.Mount, IntPtr.Zero, IntPtr.Zero, () => (IntPtr)((CharaExt*)_dalamudUtil.PlayerPointer)->Mount);
        }

        public void Dispose()
        {
            Logger.Verbose("Disposing " + nameof(PlayerManager));

            _apiController.Connected -= ApiControllerOnConnected;
            _apiController.Disconnected -= ApiController_Disconnected;

            _ipcManager.PenumbraRedrawEvent -= IpcManager_PenumbraRedrawEvent;
            _dalamudUtil.FrameworkUpdate -= DalamudUtilOnFrameworkUpdate;
        }

        private unsafe void CheckAndUpdateObject(PlayerAttachedObject attachedObject)
        {
            var curPtr = attachedObject.CurrentAddress;
            if (curPtr != IntPtr.Zero)
            {
                var chara = (Character*)curPtr;
                if (attachedObject.Address == IntPtr.Zero || attachedObject.Address != curPtr
                    || attachedObject.CompareAndUpdateEquipment(chara->EquipSlotData, chara->CustomizeData)
                    || (chara->GameObject.DrawObject != null && (IntPtr)chara->GameObject.DrawObject != attachedObject.DrawObjectAddress))
                {
                    Logger.Verbose(attachedObject.ObjectKind + " Changed " + curPtr);

                    attachedObject.Address = curPtr;
                    attachedObject.DrawObjectAddress = (IntPtr)chara->GameObject.DrawObject;
                    attachedObject.CompareAndUpdateEquipment(chara->EquipSlotData, chara->CustomizeData);
                    OnPlayerChanged(attachedObject.ObjectKind);
                }
            }
            else
            {
                attachedObject.Address = IntPtr.Zero;
                LastSentCharacterData[attachedObject.ObjectKind] = null;
            }
        }

        private unsafe void DalamudUtilOnFrameworkUpdate()
        {
            if (!_dalamudUtil.IsPlayerPresent || !_ipcManager.Initialized || !_apiController.IsConnected) return;
            //_dalamudUtil.DebugPrintRenderFlags(_dalamudUtil.PlayerPointer);

            if (DateTime.Now < _lastPlayerObjectCheck.AddSeconds(0.25)) return;


            if (_dalamudUtil.IsPlayerPresent && !_currentCharacterEquipment[ObjectKind.Player]!.CompareAndUpdate(_dalamudUtil.PlayerCharacter))
            {
                OnPlayerChanged(ObjectKind.Player);
            }

            CheckAndUpdateObject(Minion);
            CheckAndUpdateObject(Pet);
            CheckAndUpdateObject(Companion);
            CheckAndUpdateObject(Mount);

            _lastPlayerObjectCheck = DateTime.Now;
        }

        private void ApiControllerOnConnected()
        {
            Logger.Debug("ApiController Connected");

            _ipcManager.PenumbraRedrawEvent += IpcManager_PenumbraRedrawEvent;
            _dalamudUtil.FrameworkUpdate += DalamudUtilOnFrameworkUpdate;

            _currentCharacterEquipment[ObjectKind.Player] = new CharacterEquipment(_dalamudUtil.PlayerCharacter);
            PlayerChanged(ObjectKind.Player);
        }

        private void ApiController_Disconnected()
        {
            Logger.Debug(nameof(ApiController_Disconnected));

            _ipcManager.PenumbraRedrawEvent -= IpcManager_PenumbraRedrawEvent;
            _dalamudUtil.FrameworkUpdate -= DalamudUtilOnFrameworkUpdate;
            LastSentCharacterData.Clear();
        }

        private async Task<CharacterCacheDto?> CreateFullCharacterCache(CancellationToken token, ObjectKind objectKind)
        {
            IntPtr pointer = objectKind switch
            {
                ObjectKind.Player => _dalamudUtil.PlayerPointer,
                ObjectKind.Minion => Minion.Address,
                ObjectKind.Pet => Pet.Address,
                ObjectKind.Companion => Companion.Address,
                ObjectKind.Mount => Mount.Address,
                _ => throw new NotImplementedException()
            };

            var cache = _characterDataFactory.BuildCharacterData(pointer);
            if (cache == null) return null;
            CharacterCacheDto? cacheDto = null;

            await Task.Run(async () =>
            {
                while (!cache.IsReady && !token.IsCancellationRequested)
                {
                    await Task.Delay(50, token);
                }

                if (token.IsCancellationRequested) return;
                cache.Kind = objectKind;
                cacheDto = cache.ToCharacterCacheDto();
                var json = JsonConvert.SerializeObject(cacheDto);

                cacheDto.Hash = Crypto.GetHash(json);
            }, token);

            return cacheDto;
        }

        private void IpcManager_PenumbraRedrawEvent(IntPtr address, int idx)
        {
            Logger.Verbose("RedrawEvent for addr " + address);

            if (address == _dalamudUtil.PlayerPointer)
            {
                Logger.Debug("Penumbra Redraw Event for " + _dalamudUtil.PlayerName);
                PlayerChanged(ObjectKind.Player);
            }

            if (address == Minion.Address)
            {
                Logger.Debug("Penumbra Redraw Event for Minion");
                PlayerChanged(ObjectKind.Minion);
            }

            if (address == Pet.Address)
            {
                Logger.Debug("Penumbra Redraw Event for Pet");
                PlayerChanged(ObjectKind.Pet);
            }

            if (address == Companion.Address)
            {
                Logger.Debug("Penumbra Redraw Event for Companion");
                PlayerChanged(ObjectKind.Companion);
            }
        }

        private void PlayerChanged(ObjectKind objectKind)
        {
            if (_dalamudUtil.IsInGpose) return;

            Logger.Debug("Object changed: " + objectKind.ToString());
            _playerChangedCts.TryGetValue(objectKind, out var cts);
            cts?.Cancel();
            _playerChangedCts[objectKind] = new CancellationTokenSource();
            var token = _playerChangedCts[objectKind]!.Token;

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
                SendingData = true;
                int attempts = 0;
                while (!_apiController.IsConnected && attempts < 10 && !token.IsCancellationRequested)
                {
                    Logger.Warn("No connection to the API");
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                    attempts++;
                }

                if (attempts == 10 || token.IsCancellationRequested) return;

                _dalamudUtil.WaitWhileSelfIsDrawing(token);

                var characterCache = (await CreateFullCharacterCache(token, objectKind));

                if (characterCache == null || token.IsCancellationRequested) return;

                LastSentCharacterData.TryGetValue(objectKind, out var lastSentPlayerData);
                if (characterCache.Hash == (lastSentPlayerData?.Hash ?? string.Empty))
                {
                    Logger.Debug("Not sending data, already sent");
                    return;
                }

                LastSentCharacterData[objectKind] = characterCache;
                PlayerHasChanged?.Invoke(characterCache, objectKind);
                SendingData = false;
            }, token);
        }

        private void OnPlayerChanged(ObjectKind objectKind)
        {
            Task.Run(() =>
            {
                Logger.Debug("Watcher: PlayerChanged");
                PlayerChanged(objectKind);
            });
        }


    }
}
