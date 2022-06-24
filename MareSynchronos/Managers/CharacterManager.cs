using Dalamud.Game.ClientState.Objects;
using Dalamud.Logging;
using MareSynchronos.Factories;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Newtonsoft.Json;
using Penumbra.PlayerWatch;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MareSynchronos.Managers
{
    public class CharacterManager : IDisposable
    {
        private readonly ApiController _apiController;
        private readonly CharacterCacheManager _characterCacheManager;
        private readonly CharacterDataFactory _characterDataFactory;
        private readonly DalamudUtil _dalamudUtil;
        private readonly IpcManager _ipcManager;
        private readonly ObjectTable _objectTable;
        private readonly IPlayerWatcher _watcher;
        private string _lastSentHash = string.Empty;
        private Task? _playerChangedTask;

        public CharacterManager(ApiController apiController, ObjectTable objectTable, IpcManager ipcManager,
            CharacterDataFactory characterDataFactory, CharacterCacheManager characterCacheManager, DalamudUtil dalamudUtil, IPlayerWatcher watcher)
        {
            Logger.Debug("Creating " + nameof(CharacterManager));

            _apiController = apiController;
            _objectTable = objectTable;
            _ipcManager = ipcManager;
            _characterDataFactory = characterDataFactory;
            _characterCacheManager = characterCacheManager;
            _dalamudUtil = dalamudUtil;
            _watcher = watcher;
        }

        public void Dispose()
        {
            Logger.Debug("Disposing " + nameof(CharacterManager));

            _ipcManager.PenumbraRedrawEvent -= IpcManager_PenumbraRedrawEvent;
            _apiController.Connected -= ApiController_Connected;
            _apiController.Disconnected -= ApiController_Disconnected;
            _watcher.PlayerChanged -= Watcher_PlayerChanged;
        }

        internal void StartWatchingPlayer()
        {
            _watcher.AddPlayerToWatch(_dalamudUtil.PlayerName);
            _watcher.PlayerChanged += Watcher_PlayerChanged;
            _apiController.Connected += ApiController_Connected;
            _apiController.Disconnected += ApiController_Disconnected;

            Logger.Debug("Watching Player, ApiController is Connected: " + _apiController.IsConnected);
            if (_apiController.IsConnected)
            {
                ApiController_Connected(null, EventArgs.Empty);
            }

            _ipcManager.PenumbraRedraw(_dalamudUtil.PlayerName);
        }

        private void ApiController_Connected(object? sender, EventArgs args)
        {
            var apiTask = _apiController.SendCharacterName(_dalamudUtil.PlayerNameHashed);
            _lastSentHash = string.Empty;
            _characterCacheManager.Initialize();

            Task.WaitAll(apiTask);

            _characterCacheManager.AddInitialPairs(apiTask.Result);

            _ipcManager.PenumbraRedrawEvent += IpcManager_PenumbraRedrawEvent;
        }

        private void ApiController_Disconnected(object? sender, EventArgs args)
        {
            _characterCacheManager.Dispose();

            Logger.Debug(nameof(ApiController_Disconnected));

            _ipcManager.PenumbraRedrawEvent -= IpcManager_PenumbraRedrawEvent;
        }

        private async Task<CharacterData> CreateFullCharacterCache()
        {
            var cache = _characterDataFactory.BuildCharacterData();

            await Task.Run(async () =>
            {
                while (!cache.IsReady)
                {
                    await Task.Delay(50);
                }

                var json = JsonConvert.SerializeObject(cache, Formatting.Indented);

                cache.CacheHash = Crypto.GetHash(json);
            });

            return cache;
        }

        private void IpcManager_PenumbraRedrawEvent(object? objectTableIndex, EventArgs e)
        {
            var objTableObj = _objectTable[(int)objectTableIndex!];
            if (objTableObj!.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) return;
            if (objTableObj.Name.ToString() != _dalamudUtil.PlayerName) return;
            Logger.Debug("Penumbra Redraw Event");
            PlayerChanged(_dalamudUtil.PlayerName);
        }

        private void PlayerChanged(string name)
        {
            //if (sender == null) return;
            Logger.Debug("Player changed: " + name);
            if (_playerChangedTask is { IsCompleted: false })
            {
                PluginLog.Warning("PlayerChanged Task still running");
                return;
            }

            _playerChangedTask = Task.Run(async () =>
            {
                Stopwatch st = Stopwatch.StartNew();
                _dalamudUtil.WaitWhileSelfIsDrawing();

                var characterCacheTask = await CreateFullCharacterCache();

                var cacheDto = characterCacheTask.ToCharacterCacheDto();

                st.Stop();
                Logger.Debug("Elapsed time PlayerChangedTask: " + st.Elapsed);
                if (cacheDto.Hash == _lastSentHash)
                {
                    Logger.Debug("Not sending data, already sent");
                    return;
                }
                _ = _apiController.SendCharacterData(cacheDto, _dalamudUtil.GetLocalPlayers().Select(d => d.Key).ToList());
                _lastSentHash = cacheDto.Hash;
            });
        }

        private void Watcher_PlayerChanged(Dalamud.Game.ClientState.Objects.Types.Character actor)
        {
            Logger.Debug("Watcher Player Changed");
            Task.Run(() =>
            {
                try
                {
                    // fix for redraw from anamnesis
                    while (!_dalamudUtil.IsPlayerPresent)
                    {
                        Logger.Debug("Waiting Until Player is Present");
                        Thread.Sleep(100);
                    }

                    if (actor.Name.ToString() == _dalamudUtil.PlayerName)
                    {
                        Logger.Debug("Watcher: PlayerChanged");
                        PlayerChanged(actor.Name.ToString());
                    }
                    else
                    {
                        Logger.Debug("PlayerChanged: " + actor.Name.ToString());
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Actor was null or broken " + actor);
                }
            });
        }
    }
}
