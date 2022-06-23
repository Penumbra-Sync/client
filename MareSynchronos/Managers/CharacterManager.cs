using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using MareSynchronos.Factories;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Newtonsoft.Json;
using Penumbra.PlayerWatch;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using MareSynchronos.API;
using MareSynchronos.FileCacheDB;

namespace MareSynchronos.Managers
{
    public class CachedPlayer
    {
        public string? PlayerName { get; set; }
        public string? PlayerNameHash { get; set; }
        public int JobId { get; set; }
        public Dictionary<int, CharacterCacheDto>? CharacterCache { get; set; }
        public PlayerCharacter? PlayerCharacter { get; set; }
    }

    public class CharacterManager : IDisposable
    {
        private readonly ApiController _apiController;
        readonly Dictionary<string, string> _cachedLocalPlayers = new();
        private readonly Dictionary<(string, int), CharacterCacheDto> _characterCache = new();
        private readonly ClientState _clientState;
        private readonly Framework _framework;
        private readonly IpcManager _ipcManager;
        private readonly ObjectTable _objectTable;
        private readonly Configuration _pluginConfiguration;
        private readonly CharacterCacheFactory _characterCacheFactory;
        private readonly IPlayerWatcher _watcher;
        private DateTime _lastPlayerObjectCheck = DateTime.Now;
        private string _lastSentHash = string.Empty;
        private Task? _playerChangedTask = null;

        private List<CachedPlayer> _onlineCachedPlayers = new();

        private Dictionary<string, string> _onlinePairedUsers = new();

        public CharacterManager(ClientState clientState, Framework framework, ApiController apiController, ObjectTable objectTable, IpcManager ipcManager,
                    Configuration pluginConfiguration, CharacterCacheFactory characterCacheFactory)
        {
            this._clientState = clientState;
            this._framework = framework;
            this._apiController = apiController;
            this._objectTable = objectTable;
            this._ipcManager = ipcManager;
            _pluginConfiguration = pluginConfiguration;
            _characterCacheFactory = characterCacheFactory;
            _watcher = PlayerWatchFactory.Create(framework, clientState, objectTable);
        }

        public void Dispose()
        {
            _ipcManager.PenumbraRedrawEvent -= IpcManager_PenumbraRedrawEvent;
            _framework.Update -= Framework_Update;
            _clientState.TerritoryChanged -= ClientState_TerritoryChanged;
            _apiController.Connected -= ApiController_Connected;
            _apiController.Disconnected -= ApiController_Disconnected;
            _apiController.CharacterReceived -= ApiControllerOnCharacterReceived;
            _apiController.UnpairedFromOther -= ApiControllerOnUnpairedFromOther;
            _apiController.PairedWithOther -= ApiControllerOnPairedWithOther;
            _apiController.PairedClientOffline -= ApiControllerOnPairedClientOffline;
            _watcher.Disable();
            _watcher.PlayerChanged -= Watcher_PlayerChanged;
            _watcher?.Dispose();

            foreach (var character in _onlinePairedUsers)
            {
                RestoreCharacter(character);
            }
        }

        public async Task UpdatePlayersFromService(Dictionary<string, PlayerCharacter> currentLocalPlayers)
        {
            PluginLog.Debug("Updating local players from service");
            currentLocalPlayers = currentLocalPlayers.Where(k => _onlinePairedUsers.ContainsKey(k.Key))
                .ToDictionary(k => k.Key, k => k.Value);
            await _apiController.GetCharacterData(currentLocalPlayers
                .ToDictionary(
                    k => k.Key,
                    k => (int)k.Value.ClassJob.Id));
        }

        internal void StartWatchingPlayer()
        {
            _watcher.AddPlayerToWatch(GetPlayerName());
            _watcher.PlayerChanged += Watcher_PlayerChanged;
            _watcher.Enable();
            _apiController.Connected += ApiController_Connected;
            _apiController.Disconnected += ApiController_Disconnected;
            _apiController.CharacterReceived += ApiControllerOnCharacterReceived;
            _apiController.UnpairedFromOther += ApiControllerOnUnpairedFromOther;
            _apiController.PairedWithOther += ApiControllerOnPairedWithOther;
            _apiController.PairedClientOffline += ApiControllerOnPairedClientOffline;
            _apiController.PairedClientOnline += ApiControllerOnPairedClientOnline;

            PluginLog.Debug("Watching Player, ApiController is Connected: " + _apiController.IsConnected);
            if (_apiController.IsConnected)
            {
                ApiController_Connected(null, EventArgs.Empty);
            }
        }

        private void ApiController_Connected(object? sender, EventArgs args)
        {
            PluginLog.Debug(nameof(ApiController_Connected));
            PluginLog.Debug("MyHashedName:" + Crypto.GetHash256(GetPlayerName() + _clientState.LocalPlayer!.HomeWorld.Id));
            _lastSentHash = string.Empty;
            var apiTask = _apiController.SendCharacterName(Crypto.GetHash256(GetPlayerName() + _clientState.LocalPlayer!.HomeWorld.Id));

            Task.WaitAll(apiTask);

            _onlinePairedUsers = apiTask.Result.ToDictionary(k => k, k => string.Empty);
            var assignTask = AssignLocalPlayersData();
            Task.WaitAll(assignTask);
            PluginLog.Debug("Online and paired users: " + string.Join(",", _onlinePairedUsers));

            _framework.Update += Framework_Update;
            _ipcManager.PenumbraRedrawEvent += IpcManager_PenumbraRedrawEvent;
            _clientState.TerritoryChanged += ClientState_TerritoryChanged;
        }

        private void ApiController_Disconnected(object? sender, EventArgs args)
        {
            PluginLog.Debug(nameof(ApiController_Disconnected));
            _framework.Update -= Framework_Update;
            _ipcManager.PenumbraRedrawEvent -= IpcManager_PenumbraRedrawEvent;
            _clientState.TerritoryChanged -= ClientState_TerritoryChanged;
            foreach (var character in _onlinePairedUsers)
            {
                RestoreCharacter(character);
            }
            _onlinePairedUsers.Clear();

            _lastSentHash = string.Empty;
        }

        private void ApiControllerOnPairedWithOther(object? sender, EventArgs e)
        {
            var characterHash = (string?)sender;
            if (string.IsNullOrEmpty(characterHash)) return;
            var players = GetLocalPlayers();
            if (players.ContainsKey(characterHash))
            {
                PluginLog.Debug("Removed pairing, restoring data for " + characterHash);
                _ = _apiController.GetCharacterData(new Dictionary<string, int> { { characterHash, (int)players[characterHash].ClassJob.Id } });
            }
        }

        private void ApiControllerOnCharacterReceived(object? sender, CharacterReceivedEventArgs e)
        {
            PluginLog.Debug("Received hash for " + e.CharacterNameHash);
            string otherPlayerName;

            var localPlayers = GetLocalPlayers();
            if (localPlayers.ContainsKey(e.CharacterNameHash))
            {
                _onlinePairedUsers[e.CharacterNameHash] = localPlayers[e.CharacterNameHash].Name.ToString();
                otherPlayerName = _onlinePairedUsers[e.CharacterNameHash];
            }
            else
            {
                PluginLog.Debug("Found no local player for " + e.CharacterNameHash);
                return;
            }

            _characterCache[(e.CharacterNameHash, e.CharacterData.JobId)] = e.CharacterData;

            List<FileReplacementDto> toDownloadReplacements;
            using (var db = new FileCacheContext())
            {
                PluginLog.Debug("Checking for files to download for player " + otherPlayerName);
                PluginLog.Debug("Received total " + e.CharacterData.FileReplacements.Count + " file replacement data");
                PluginLog.Debug("Hash for data is " + e.CharacterData.Hash);
                toDownloadReplacements =
                    e.CharacterData.FileReplacements.Where(f => !db.FileCaches.Any(c => c.Hash == f.Hash))
                        .ToList();
            }

            PluginLog.Debug("Downloading missing files for player " + otherPlayerName);
            // todo: make this cancellable
            var downloadTask = _apiController.DownloadFiles(toDownloadReplacements, _pluginConfiguration.CacheFolder);
            while (!downloadTask.IsCompleted)
            {
                Thread.Sleep(100);
            }

            PluginLog.Debug("Assigned hash to visible player: " + otherPlayerName);
            _ipcManager.PenumbraRemoveTemporaryCollection(otherPlayerName);
            var tempCollection = _ipcManager.PenumbraCreateTemporaryCollection(otherPlayerName);
            Dictionary<string, string> moddedPaths = new();
            try
            {
                using var db = new FileCacheContext();
                foreach (var item in e.CharacterData.FileReplacements)
                {
                    foreach (var gamePath in item.GamePaths)
                    {
                        var fileCache = db.FileCaches.FirstOrDefault(f => f.Hash == item.Hash);
                        if (fileCache != null)
                        {
                            moddedPaths.Add(gamePath, fileCache.Filepath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Something went wrong during calculation replacements");
            }

            WaitWhileCharacterIsDrawing(localPlayers[e.CharacterNameHash].Address);

            _ipcManager.PenumbraSetTemporaryMods(tempCollection, moddedPaths, e.CharacterData.ManipulationData);
            _ipcManager.GlamourerApplyCharacterCustomization(e.CharacterData.GlamourerData, otherPlayerName);
        }

        private void ApiControllerOnUnpairedFromOther(object? sender, EventArgs e)
        {
            var characterHash = (string?)sender;
            if (string.IsNullOrEmpty(characterHash)) return;
            RestoreCharacter(new KeyValuePair<string, string>(characterHash, _onlinePairedUsers[characterHash]));
        }

        private void RestoreCharacter(KeyValuePair<string, string> character)
        {
            if (string.IsNullOrEmpty(character.Value)) return;

            foreach (var entry in _characterCache.Where(c => c.Key.Item1 == character.Key))
            {
                _characterCache.Remove(entry.Key);
            }

            RestorePreviousCharacter(character.Value);
            PluginLog.Debug("Removed from pairing, restoring state for " + character.Value);
            _ipcManager.PenumbraRemoveTemporaryCollection(character.Value);
            _ipcManager.GlamourerRevertCharacterCustomization(character.Value);
        }

        private void ApiControllerOnPairedClientOffline(object? sender, EventArgs e)
        {
            PluginLog.Debug("Player offline: " + sender!);
            _onlinePairedUsers.Remove((string)sender!);
        }

        private void ApiControllerOnPairedClientOnline(object? sender, EventArgs e)
        {
            PluginLog.Debug("Player online: " + sender!);
            _onlinePairedUsers.Add((string)sender!, string.Empty);
        }

        private async Task AssignLocalPlayersData()
        {
            PluginLog.Debug("Temp assigning local players from cache");
            var currentLocalPlayers = GetLocalPlayers();
            foreach (var player in _characterCache)
            {
                if (currentLocalPlayers.ContainsKey(player.Key.Item1))
                {
                    await Task.Run(() => ApiControllerOnCharacterReceived(null, new CharacterReceivedEventArgs(player.Key.Item1, player.Value)));
                }
            }

            await UpdatePlayersFromService(currentLocalPlayers);
        }

        private void ClientState_TerritoryChanged(object? sender, ushort e)
        {
            _ = Task.Run(async () =>
            {
                while (_clientState.LocalPlayer == null)
                {
                    await Task.Delay(250);
                }

                await AssignLocalPlayersData();
            });
        }

        private async Task<CharacterCache> CreateFullCharacterCache()
        {
            var cache = _characterCacheFactory.BuildCharacterCache();
            cache.GlamourerString = _ipcManager.GlamourerGetCharacterCustomization()!;
            cache.ManipulationString = _ipcManager.PenumbraGetMetaManipulations(_clientState.LocalPlayer!.Name.ToString());
            cache.JobId = _clientState.LocalPlayer!.ClassJob.Id;
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

        private unsafe void Framework_Update(Framework framework)
        {
            try
            {
                if (_clientState.LocalPlayer == null) return;

                if (DateTime.Now < _lastPlayerObjectCheck.AddSeconds(2)) return;

                List<string> localPlayersList = new();
                Dictionary<string, PlayerCharacter> newPlayers = new();
                foreach (var obj in _objectTable)
                {
                    if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;
                    string playerName = obj.Name.ToString();
                    if (playerName == GetPlayerName()) continue;
                    var pObj = (PlayerCharacter)obj;
                    var hashedName = Crypto.GetHash256(pObj.Name.ToString() + pObj.HomeWorld.Id.ToString());

                    if (!_onlinePairedUsers.ContainsKey(hashedName)) continue;

                    _onlinePairedUsers[hashedName] = pObj.Name.ToString();
                    localPlayersList.Add(hashedName);
                    if (!_cachedLocalPlayers.ContainsKey(hashedName)) newPlayers[hashedName] = pObj;
                    _cachedLocalPlayers[hashedName] = pObj.Name.ToString();
                }

                foreach (var item in _cachedLocalPlayers.ToList().Where(item => !localPlayersList.Contains(item.Key)))
                {
                    foreach (var cachedPlayerNameJobId in _characterCache.Keys.ToList().Where(cachedPlayerNameJobId => cachedPlayerNameJobId.Item1 == item.Key))
                    {
                        PluginLog.Debug("Player not visible anymore: " + cachedPlayerNameJobId.Item1);
                        RestorePreviousCharacter(_cachedLocalPlayers[cachedPlayerNameJobId.Item1]);
                        _characterCache.Remove(cachedPlayerNameJobId);
                    }

                    _cachedLocalPlayers.Remove(item.Key);
                }

                if (newPlayers.Any())
                {
                    PluginLog.Debug("Getting data for new players: " + string.Join(Environment.NewLine, newPlayers));
                    _ = UpdatePlayersFromService(newPlayers);
                }

                _lastPlayerObjectCheck = DateTime.Now;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "error");
            }
        }

        private Dictionary<string, PlayerCharacter> GetLocalPlayers()
        {
            Dictionary<string, PlayerCharacter> allLocalPlayers = new();
            foreach (var obj in _objectTable)
            {
                if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;
                string playerName = obj.Name.ToString();
                if (playerName == GetPlayerName()) continue;
                var playerObject = (PlayerCharacter)obj;
                allLocalPlayers[Crypto.GetHash256(playerObject.Name.ToString() + playerObject.HomeWorld.Id.ToString())] = playerObject;
            }

            return allLocalPlayers;
        }

        private string GetPlayerName()
        {
            return _clientState.LocalPlayer!.Name.ToString();
        }

        private void IpcManager_PenumbraRedrawEvent(object? objectTableIndex, EventArgs e)
        {
            var objTableObj = _objectTable[(int)objectTableIndex!];
            if (objTableObj!.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
            {
                if (objTableObj.Name.ToString() == GetPlayerName())
                {
                    PluginLog.Debug("Penumbra Redraw Event");
                    PlayerChanged(GetPlayerName());
                }
            }
        }

        private unsafe void PlayerChanged(string name)
        {
            //if (sender == null) return;
            PluginLog.Debug("Player changed: " + name);
            if (_playerChangedTask is { IsCompleted: false })
            {
                PluginLog.Warning("PlayerChanged Task still running");
                return;
            }

            _playerChangedTask = Task.Run(() =>
            {
                WaitWhileCharacterIsDrawing(_clientState.LocalPlayer!.Address);

                var characterCacheTask = CreateFullCharacterCache();
                Task.WaitAll(characterCacheTask);

                var cacheDto = characterCacheTask.Result.ToCharacterCacheDto();
                if (cacheDto.Hash == _lastSentHash)
                {
                    PluginLog.Debug("Not sending data, already sent");
                    return;
                }
                Task.WaitAll(_apiController.SendCharacterData(cacheDto, GetLocalPlayers().Select(d => d.Key).ToList()));
                _lastSentHash = cacheDto.Hash;
            });
        }

        public unsafe void WaitWhileCharacterIsDrawing(IntPtr characterAddress)
        {
            var obj = (GameObject*)characterAddress;

            while ((obj->RenderFlags & 0b100000000000) == 0b100000000000) // 0b100000000000 is "still rendering" or something
            {
                //PluginLog.Debug("Waiting for character to finish drawing");
                Thread.Sleep(100);
            }

            // wait half a second just in case
            Thread.Sleep(500);
        }

        private void RestorePreviousCharacter(string playerName)
        {
            PluginLog.Debug("Restoring state for " + playerName);
            _ipcManager.PenumbraRemoveTemporaryCollection(playerName);
            _ipcManager.GlamourerRevertCharacterCustomization(playerName);
        }

        private void Watcher_PlayerChanged(Dalamud.Game.ClientState.Objects.Types.Character actor)
        {
            try
            {
                // fix for redraw from anamnesis
                while (_clientState.LocalPlayer == null)
                {
                    Thread.Sleep(100);
                }
                if (actor.Name.ToString() == _clientState.LocalPlayer!.Name.ToString())
                {
                    PluginLog.Debug("Watcher: PlayerChanged");
                    PlayerChanged(actor.Name.ToString());
                }
                else
                {
                    PluginLog.Debug("PlayerChanged: " + actor.Name.ToString());
                }
            }
            catch(Exception ex) 
            {
                PluginLog.Error(ex, "Actor was null or broken " + actor);
            }
        }
    }
}
