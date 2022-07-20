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
        public bool SendingData { get; private set; }
        public CharacterCacheDto? LastSentCharacterData { get; private set; }

        private CancellationTokenSource? _playerChangedCts;
        private DateTime _lastPlayerObjectCheck;
        private CharacterEquipment? _currentCharacterEquipment;
        private string _lastMinionName = string.Empty;

        public PlayerManager(ApiController apiController, IpcManager ipcManager,
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
            if (!_dalamudUtil.IsPlayerPresent || !_ipcManager.Initialized || !_apiController.IsConnected) return;

            if (DateTime.Now < _lastPlayerObjectCheck.AddSeconds(0.25)) return;

            var minion = ((Character*)_dalamudUtil.PlayerPointer)->CompanionObject;
            string minionName = "";
            if (minion != null)
            {
                minionName = new Utf8String(minion->Character.GameObject.GetName()).ToString();
            }

            if (_dalamudUtil.IsPlayerPresent
                && (!_currentCharacterEquipment!.CompareAndUpdate(_dalamudUtil.PlayerCharacter) || minionName != _lastMinionName))
            {
                _lastMinionName = minionName;
                OnPlayerChanged();
            }

            _lastPlayerObjectCheck = DateTime.Now;
        }

        private void ApiControllerOnConnected()
        {
            Logger.Debug("ApiController Connected");

            _ipcManager.PenumbraRedrawEvent += IpcManager_PenumbraRedrawEvent;
            _dalamudUtil.FrameworkUpdate += DalamudUtilOnFrameworkUpdate;

            _currentCharacterEquipment = new CharacterEquipment(_dalamudUtil.PlayerCharacter);
            PlayerChanged();
        }

        private void ApiController_Disconnected()
        {
            Logger.Debug(nameof(ApiController_Disconnected));

            _ipcManager.PenumbraRedrawEvent -= IpcManager_PenumbraRedrawEvent;
            _dalamudUtil.FrameworkUpdate -= DalamudUtilOnFrameworkUpdate;
            LastSentCharacterData = null;
        }

        private async Task<CharacterCacheDto?> CreateFullCharacterCache(CancellationToken token)
        {
            var cache = _characterDataFactory.BuildCharacterData();
            if (cache == null) return null;
            CharacterCacheDto? cacheDto = null;

            await Task.Run(async () =>
            {
                while (!cache.IsReady && !token.IsCancellationRequested)
                {
                    await Task.Delay(50, token);
                }

                if (token.IsCancellationRequested) return;

                cacheDto = cache.ToCharacterCacheDto();
                var json = JsonConvert.SerializeObject(cacheDto);

                cacheDto.Hash = Crypto.GetHash(json);
            }, token);

            return cacheDto;
        }

        private void IpcManager_PenumbraRedrawEvent(object? objectTableIndex, EventArgs e)
        {
            var player = _dalamudUtil.GetPlayerCharacterFromObjectTableByIndex((int)objectTableIndex!);
            if (player != null && player.Name.ToString() != _dalamudUtil.PlayerName) return;
            Logger.Debug("Penumbra Redraw Event for " + _dalamudUtil.PlayerName);
            PlayerChanged();
        }

        private void PlayerChanged()
        {
            if (_dalamudUtil.IsInGpose) return;

            Logger.Debug("Player changed: " + _dalamudUtil.PlayerName);
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

                var characterCache = (await CreateFullCharacterCache(token));

                if (characterCache == null || token.IsCancellationRequested) return;

                if (characterCache.Hash == (LastSentCharacterData?.Hash ?? "-"))
                {
                    Logger.Debug("Not sending data, already sent");
                    return;
                }

                LastSentCharacterData = characterCache;
                PlayerHasChanged?.Invoke(characterCache);
                SendingData = false;
            }, token);
        }

        private void OnPlayerChanged()
        {
            Task.Run(() =>
            {
                Logger.Debug("Watcher: PlayerChanged");
                PlayerChanged();
            });
        }


    }
}
