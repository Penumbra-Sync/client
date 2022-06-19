using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using MareSynchronos.Factories;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Newtonsoft.Json;
using Penumbra.GameData.ByteString;
using Penumbra.Interop.Structs;
using Penumbra.PlayerWatch;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.Objects.SubKinds;
using MareSynchronos.API;
using MareSynchronos.FileCacheDB;

namespace MareSynchronos.Managers
{
    public class CharacterManager : IDisposable
    {
        private readonly ApiController _apiController;
        readonly Dictionary<string, string> _cachedLocalPlayers = new();
        private readonly Dictionary<(string, int), CharacterCacheDto> _characterCache = new();
        private readonly ClientState _clientState;
        private readonly FileReplacementFactory _factory;
        private readonly Framework _framework;
        private readonly IpcManager _ipcManager;
        private readonly ObjectTable _objectTable;
        private readonly Configuration _pluginConfiguration;
        private readonly IPlayerWatcher _watcher;
        private DateTime _lastPlayerObjectCheck = DateTime.Now;
        private string _lastSentHash = string.Empty;
        private Task? _playerChangedTask = null;

        private HashSet<string> onlineWhitelistedUsers = new();

        public CharacterManager(ClientState clientState, Framework framework, ApiController apiController, ObjectTable objectTable, IpcManager ipcManager, FileReplacementFactory factory,
                    Configuration pluginConfiguration)
        {
            this._clientState = clientState;
            this._framework = framework;
            this._apiController = apiController;
            this._objectTable = objectTable;
            this._ipcManager = ipcManager;
            this._factory = factory;
            _pluginConfiguration = pluginConfiguration;
            _watcher = PlayerWatchFactory.Create(framework, clientState, objectTable);
        }

        public unsafe CharacterCache BuildCharacterCache()
        {
            var cache = new CharacterCache();

            while (_clientState.LocalPlayer == null)
            {
                PluginLog.Debug("Character is null but it shouldn't be, waiting");
                Thread.Sleep(50);
            }
            var model = (CharacterBase*)((Character*)_clientState.LocalPlayer!.Address)->GameObject.GetDrawObject();
            for (var idx = 0; idx < model->SlotCount; ++idx)
            {
                var mdl = (RenderModel*)model->ModelArray[idx];
                if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
                {
                    continue;
                }

                var mdlPath = new Utf8String(mdl->ResourceHandle->FileName()).ToString();

                FileReplacement cachedMdlResource = _factory.Create();
                cachedMdlResource.GamePaths = _ipcManager.PenumbraReverseResolvePath(mdlPath, GetPlayerName());
                cachedMdlResource.SetResolvedPath(mdlPath);
                PluginLog.Verbose("Resolving for model " + mdlPath);

                cache.AddAssociatedResource(cachedMdlResource, null!, null!);

                var imc = (ResourceHandle*)model->IMCArray[idx];
                if (imc != null)
                {
                    byte[] imcData = new byte[imc->Data->DataLength / sizeof(long)];
                    Marshal.Copy((IntPtr)imc->Data->DataPtr, imcData, 0, (int)imc->Data->DataLength / sizeof(long));
                    string imcDataStr = BitConverter.ToString(imcData).Replace("-", "");
                    cachedMdlResource.ImcData = imcDataStr;
                }
                cache.AddAssociatedResource(cachedMdlResource, null!, null!);

                for (int mtrlIdx = 0; mtrlIdx < mdl->MaterialCount; mtrlIdx++)
                {
                    var mtrl = (Material*)mdl->Materials[mtrlIdx];
                    if (mtrl == null) continue;

                    //var mtrlFileResource = factory.Create();
                    var mtrlPath = new Utf8String(mtrl->ResourceHandle->FileName()).ToString().Split("|")[2];
                    PluginLog.Verbose("Resolving for material " + mtrlPath);
                    var cachedMtrlResource = _factory.Create();
                    cachedMtrlResource.GamePaths = _ipcManager.PenumbraReverseResolvePath(mtrlPath, GetPlayerName());
                    cachedMtrlResource.SetResolvedPath(mtrlPath);
                    cache.AddAssociatedResource(cachedMtrlResource, cachedMdlResource, null!);

                    var mtrlResource = (MtrlResource*)mtrl->ResourceHandle;
                    for (int resIdx = 0; resIdx < mtrlResource->NumTex; resIdx++)
                    {
                        var texPath = new Utf8String(mtrlResource->TexString(resIdx)).ToString();

                        if (string.IsNullOrEmpty(texPath.ToString())) continue;
                        PluginLog.Verbose("Resolving for texture " + texPath);

                        var cachedTexResource = _factory.Create();
                        cachedTexResource.GamePaths = new[] { texPath };
                        cachedTexResource.SetResolvedPath(_ipcManager.PenumbraResolvePath(texPath, GetPlayerName())!);
                        cache.AddAssociatedResource(cachedTexResource, cachedMdlResource, cachedMtrlResource);
                    }
                }
            }

            return cache;
        }

        public async Task DebugJson()
        {
            var cache = CreateFullCharacterCache();
            while (!cache.IsCompleted)
            {
                await Task.Delay(50);
            }

            PluginLog.Debug(JsonConvert.SerializeObject(cache.Result, Formatting.Indented));
        }

        public void Dispose()
        {
            _ipcManager.PenumbraRedrawEvent -= IpcManager_PenumbraRedrawEvent;
            _framework.Update -= Framework_Update;
            _clientState.TerritoryChanged -= ClientState_TerritoryChanged;
            _apiController.Connected -= ApiController_Connected;
            _apiController.Disconnected -= ApiController_Disconnected;
            _apiController.CharacterReceived -= ApiControllerOnCharacterReceived;
            _apiController.RemovedFromWhitelist -= ApiControllerOnRemovedFromWhitelist;
            _apiController.AddedToWhitelist -= ApiControllerOnAddedToWhitelist;
            _apiController.WhitelistedPlayerOffline -= ApiControllerOnWhitelistedPlayerOffline;
            _watcher.Disable();
            _watcher.PlayerChanged -= Watcher_PlayerChanged;
            _watcher?.Dispose();
        }

        public void StopWatchPlayer(string name)
        {
            _watcher.RemovePlayerFromWatch(name);
        }

        public async Task UpdatePlayersFromService(Dictionary<string, PlayerCharacter> currentLocalPlayers)
        {
            PluginLog.Debug("Updating local players from service");
            currentLocalPlayers = currentLocalPlayers.Where(k => onlineWhitelistedUsers.Contains(k.Key))
                .ToDictionary(k => k.Key, k => k.Value);
            await _apiController.GetCharacterData(currentLocalPlayers
                .ToDictionary(
                    k => k.Key,
                    k => (int)k.Value.ClassJob.Id));
        }

        public void WatchPlayer(string name)
        {
            _watcher.AddPlayerToWatch(name);
        }

        internal void StartWatchingPlayer()
        {
            _watcher.AddPlayerToWatch(GetPlayerName());
            _watcher.PlayerChanged += Watcher_PlayerChanged;
            _watcher.Enable();
            _apiController.Connected += ApiController_Connected;
            _apiController.Disconnected += ApiController_Disconnected;
            _apiController.CharacterReceived += ApiControllerOnCharacterReceived;
            _apiController.RemovedFromWhitelist += ApiControllerOnRemovedFromWhitelist;
            _apiController.AddedToWhitelist += ApiControllerOnAddedToWhitelist;
            _apiController.WhitelistedPlayerOffline += ApiControllerOnWhitelistedPlayerOffline;
            _apiController.WhitelistedPlayerOnline += ApiControllerOnWhitelistedPlayerOnline;

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
            var apiTask = _apiController.SendCharacterName(Crypto.GetHash256(GetPlayerName() + _clientState.LocalPlayer!.HomeWorld.Id));

            Task.WaitAll(apiTask);

            onlineWhitelistedUsers = new HashSet<string>(apiTask.Result);
            var assignTask = AssignLocalPlayersData();
            Task.WaitAll(assignTask);
            PluginLog.Debug("Online and whitelisted users: " + string.Join(",", onlineWhitelistedUsers));

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
        }

        private void ApiControllerOnAddedToWhitelist(object? sender, EventArgs e)
        {
            var characterHash = (string?)sender;
            if (string.IsNullOrEmpty(characterHash)) return;
            var players = GetLocalPlayers();
            if (players.ContainsKey(characterHash))
            {
                PluginLog.Debug("You got added to a whitelist, restoring data for " + characterHash);
                _ = _apiController.GetCharacterData(new Dictionary<string, int> { { characterHash, (int)players[characterHash].ClassJob.Id } });
            }
        }

        private void ApiControllerOnCharacterReceived(object? sender, CharacterReceivedEventArgs e)
        {
            PlayerCharacter? playerObject = null;
            PluginLog.Debug("Received hash for " + e.CharacterNameHash);
            foreach (var obj in _objectTable)
            {
                if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;
                string playerName = obj.Name.ToString();
                if (playerName == GetPlayerName()) continue;
                playerObject = (PlayerCharacter)obj;
                var hashedName = Crypto.GetHash256(playerObject.Name.ToString() + playerObject.HomeWorld.Id.ToString());
                if (e.CharacterNameHash == hashedName)
                {
                    break;
                }

                playerObject = null;
            }

            if (playerObject == null)
            {
                PluginLog.Debug("Found no suitable hash for " + e.CharacterNameHash);
                return;
            }
            else
            {
                PluginLog.Debug("Found suitable player for hash: " + playerObject.Name.ToString());
            }

            var otherPlayerName = playerObject.Name.ToString();

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
            var downloadTask = _apiController.DownloadFiles(toDownloadReplacements, _pluginConfiguration.CacheFolder);
            while (!downloadTask.IsCompleted)
            {
                Thread.Sleep(100);
            }

            PluginLog.Debug("Assigned hash to visible player: " + otherPlayerName);
            /*ipcManager.PenumbraRemoveTemporaryCollection(otherPlayerName);
            ipcManager.PenumbraCreateTemporaryCollection(otherPlayerName);
            Dictionary<string, string> moddedPaths = new();
            using (var db = new FileCacheContext())
            {
                foreach (var item in e.CharacterData.FileReplacements)
                {
                    foreach (var gamePath in item.GamePaths)
                    {
                        var fileCache = db.FileCaches.FirstOrDefault(f => f.Hash == item.Hash);
                        if (fileCache != null)
                        {
                            PluginLog.Debug("Modifying: " + gamePath + " => " + fileCache.Filepath);
                            moddedPaths.Add(gamePath, fileCache.Filepath);
                        }
                    }
                }
            }

            ipcManager.PenumbraSetTemporaryMods(otherPlayerName, moddedPaths);*/
            _ipcManager.GlamourerApplyCharacterCustomization(e.CharacterData.GlamourerData, otherPlayerName);
            //ipcManager.PenumbraRedraw(otherPlayerName);
        }

        private void ApiControllerOnRemovedFromWhitelist(object? sender, EventArgs e)
        {
            var characterHash = (string?)sender;
            if (string.IsNullOrEmpty(characterHash)) return;
            var players = GetLocalPlayers();
            foreach (var entry in _characterCache.Where(c => c.Key.Item1 == characterHash))
            {
                _characterCache.Remove(entry.Key);
            }

            var playerName = players.SingleOrDefault(p => p.Key == characterHash).Value.Name.ToString() ?? null;
            if (playerName != null)
            {
                RestorePreviousCharacter(playerName);
                PluginLog.Debug("Removed from whitelist, restoring glamourer state for " + playerName);
                _ipcManager.PenumbraRemoveTemporaryCollection(playerName);
                _ipcManager.GlamourerRevertCharacterCustomization(playerName);
            }
        }

        private void ApiControllerOnWhitelistedPlayerOffline(object? sender, EventArgs e)
        {
            PluginLog.Debug("Player offline: " + sender!);
            onlineWhitelistedUsers.Remove((string)sender!);
        }

        private void ApiControllerOnWhitelistedPlayerOnline(object? sender, EventArgs e)
        {
            PluginLog.Debug("Player online: " + sender!);
            onlineWhitelistedUsers.Add((string)sender!);
        }

        private async Task AssignLocalPlayersData()
        {
            PluginLog.Debug("Temp assigning local players from cache");
            var currentLocalPlayers = GetLocalPlayers();
            foreach (var player in _characterCache)
            {
                if (currentLocalPlayers.ContainsKey(player.Key.Item1))
                {
                    await Task.Run(() => ApiControllerOnCharacterReceived(null, new CharacterReceivedEventArgs
                    {
                        CharacterNameHash = player.Key.Item1,
                        CharacterData = player.Value
                    }));
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
            var cache = BuildCharacterCache();
            cache.SetGlamourerData(_ipcManager.GlamourerGetCharacterCustomization()!);
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

                    if (!onlineWhitelistedUsers.Contains(hashedName)) continue;

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
                allLocalPlayers.Add(Crypto.GetHash256(playerObject.Name.ToString() + playerObject.HomeWorld.Id.ToString()), playerObject);
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
                var obj = (GameObject*)_clientState.LocalPlayer!.Address;

                PluginLog.Debug("Waiting for charater to be drawn");
                while ((obj->RenderFlags & 0b100000000000) == 0b100000000000) // 0b100000000000 is "still rendering" or something
                {
                    //PluginLog.Debug("Waiting for character to finish drawing");
                    Thread.Sleep(10);
                }
                PluginLog.Debug("Character finished drawing");

                // wait half a second just in case
                Thread.Sleep(500);

                var characterCacheTask = CreateFullCharacterCache();
                Task.WaitAll(characterCacheTask);

                var cacheDto = characterCacheTask.Result.ToCharacterCacheDto();
                if (cacheDto.Hash == _lastSentHash)
                {
                    PluginLog.Warning("Not sending data, already sent");
                    return;
                }
                Task.WaitAll(_apiController.SendCharacterData(cacheDto, GetLocalPlayers().Select(d => d.Key).ToList()));
                _lastSentHash = cacheDto.Hash;
            });
        }

        private void RestorePreviousCharacter(string playerName)
        {
            PluginLog.Debug("Restoring state for " + playerName);
            _ipcManager.PenumbraRemoveTemporaryCollection(playerName);
            _ipcManager.GlamourerRevertCharacterCustomization(playerName);
        }

        private void Watcher_PlayerChanged(Dalamud.Game.ClientState.Objects.Types.Character actor)
        {
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
    }
}
