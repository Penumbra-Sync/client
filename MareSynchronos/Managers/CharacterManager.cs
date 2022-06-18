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
        private readonly ClientState clientState;
        private readonly Framework _framework;
        private readonly ApiController apiController;
        private readonly ObjectTable objectTable;
        private readonly IpcManager ipcManager;
        private readonly FileReplacementFactory factory;
        private readonly Configuration _pluginConfiguration;
        private readonly IPlayerWatcher watcher;
        private Task? playerChangedTask = null;

        public CharacterManager(ClientState clientState, Framework framework, ApiController apiController, ObjectTable objectTable, IpcManager ipcManager, FileReplacementFactory factory,
            Configuration pluginConfiguration)
        {
            this.clientState = clientState;
            this._framework = framework;
            this.apiController = apiController;
            this.objectTable = objectTable;
            this.ipcManager = ipcManager;
            this.factory = factory;
            _pluginConfiguration = pluginConfiguration;
            watcher = PlayerWatchFactory.Create(framework, clientState, objectTable);
        }

        private void IpcManager_PenumbraRedrawEvent(object? objectTableIndex, EventArgs e)
        {
            var objTableObj = objectTable[(int)objectTableIndex!];
            if (objTableObj!.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
            {
                if (objTableObj.Name.ToString() == GetPlayerName())
                {
                    PluginLog.Debug("Penumbra Redraw Event");
                    PlayerChanged(GetPlayerName());
                }
            }
        }

        private readonly Dictionary<(string, int), CharacterCacheDto> _characterCache = new();

        Dictionary<string, string> localPlayers = new();
        private DateTime lastCheck = DateTime.Now;

        private unsafe void Framework_Update(Framework framework)
        {
            try
            {
                if (clientState.LocalPlayer == null) return;

                if (DateTime.Now < lastCheck.AddSeconds(5)) return;

                List<string> localPlayersList = new();
                foreach (var obj in objectTable)
                {
                    if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;
                    string playerName = obj.Name.ToString();
                    if (playerName == GetPlayerName()) continue;
                    var pObj = (PlayerCharacter)obj;
                    var hashedName = Crypto.GetHash256(pObj.Name.ToString() + pObj.HomeWorld.Id.ToString());
                    localPlayersList.Add(hashedName);
                    localPlayers[hashedName] = pObj.Name.ToString();
                }

                foreach (var item in localPlayers.ToList())
                {
                    if (!localPlayersList.Contains(item.Key))
                    {
                        localPlayers.Remove(item.Key);
                    }
                }

                lastCheck = DateTime.Now;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "error");
            }
        }

        private string GetPlayerName()
        {
            return clientState.LocalPlayer!.Name.ToString();
        }

        private void Watcher_PlayerChanged(Dalamud.Game.ClientState.Objects.Types.Character actor)
        {
            if (actor.Name.ToString() == clientState.LocalPlayer!.Name.ToString())
            {
                PlayerChanged(actor.Name.ToString());
            }
            else
            {
                PluginLog.Debug("PlayerChanged: " + actor.Name.ToString());
            }
        }

        private unsafe void PlayerChanged(string name)
        {
            //if (sender == null) return;
            PluginLog.Debug("Player changed: " + name);
            if (playerChangedTask is { IsCompleted: false }) return;

            playerChangedTask = Task.Run(() =>
            {
                var obj = (GameObject*)clientState.LocalPlayer!.Address;

                PluginLog.Debug("Waiting for charater to be drawn");
                while ((obj->RenderFlags & 0b100000000000) == 0b100000000000) // 0b100000000000 is "still rendering" or something
                {
                    //PluginLog.Debug("Waiting for character to finish drawing");
                    Thread.Sleep(10);
                }
                PluginLog.Debug("Character finished drawing");

                // wait half a second just in case
                Thread.Sleep(500);

                var cache = CreateFullCharacterCache();
                while (!cache.IsCompleted)
                {
                    Thread.Sleep(50);
                }

                _ = apiController.SendCharacterData(cache.Result.ToCharacterCacheDto(), GetLocalPlayers().Select(d => d.Key).ToList());
            });
        }

        private Dictionary<string, PlayerCharacter> GetLocalPlayers()
        {
            Dictionary<string, PlayerCharacter> allLocalPlayers = new();
            foreach (var obj in objectTable)
            {
                if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;
                string playerName = obj.Name.ToString();
                if (playerName == GetPlayerName()) continue;
                var playerObject = (PlayerCharacter)obj;
                allLocalPlayers.Add(Crypto.GetHash256(playerObject.Name.ToString() + playerObject.HomeWorld.Id.ToString()), playerObject);
            }

            return allLocalPlayers;
        }

        public unsafe CharacterCache BuildCharacterCache()
        {
            var cache = new CharacterCache();

            while (clientState.LocalPlayer == null)
            {
                PluginLog.Debug("Character is null but it shouldn't be, waiting");
                Thread.Sleep(50);
            }
            var model = (CharacterBase*)((Character*)clientState.LocalPlayer!.Address)->GameObject.GetDrawObject();
            for (var idx = 0; idx < model->SlotCount; ++idx)
            {
                var mdl = (RenderModel*)model->ModelArray[idx];
                if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
                {
                    continue;
                }

                var mdlPath = new Utf8String(mdl->ResourceHandle->FileName()).ToString();

                FileReplacement cachedMdlResource = factory.Create();
                cachedMdlResource.GamePaths = ipcManager.PenumbraReverseResolvePath(mdlPath, GetPlayerName());
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
                    var cachedMtrlResource = factory.Create();
                    cachedMtrlResource.GamePaths = ipcManager.PenumbraReverseResolvePath(mtrlPath, GetPlayerName());
                    cachedMtrlResource.SetResolvedPath(mtrlPath);
                    cache.AddAssociatedResource(cachedMtrlResource, cachedMdlResource, null!);

                    var mtrlResource = (MtrlResource*)mtrl->ResourceHandle;
                    for (int resIdx = 0; resIdx < mtrlResource->NumTex; resIdx++)
                    {
                        var texPath = new Utf8String(mtrlResource->TexString(resIdx)).ToString();

                        if (string.IsNullOrEmpty(texPath.ToString())) continue;
                        PluginLog.Verbose("Resolving for texture " + texPath);

                        var cachedTexResource = factory.Create();
                        cachedTexResource.GamePaths = new[] { texPath };
                        cachedTexResource.SetResolvedPath(ipcManager.PenumbraResolvePath(texPath, GetPlayerName())!);
                        cache.AddAssociatedResource(cachedTexResource, cachedMdlResource, cachedMtrlResource);
                    }
                }
            }

            return cache;
        }

        private void ClientState_TerritoryChanged(object? sender, ushort e)
        {
            localPlayers.Clear();
            _ = Task.Run(async () =>
            {
                while (clientState.LocalPlayer == null)
                {
                    await Task.Delay(250);
                }

                await AssignLocalPlayersData();
            });
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

            PluginLog.Debug("Updating local players from service");
            await apiController.GetCharacterData(currentLocalPlayers
                .ToDictionary(
                    k => k.Key,
                    k => (int)k.Value.ClassJob.Id));
        }

        public void Dispose()
        {
            ipcManager.PenumbraRedrawEvent -= IpcManager_PenumbraRedrawEvent;
            _framework.Update -= Framework_Update;
            clientState.TerritoryChanged -= ClientState_TerritoryChanged;
            apiController.Connected -= ApiController_Connected;
            apiController.Disconnected -= ApiController_Disconnected;
            apiController.CharacterReceived -= ApiControllerOnCharacterReceived;
            apiController.RemovedFromWhitelist -= ApiControllerOnRemovedFromWhitelist;
            apiController.AddedToWhitelist -= ApiControllerOnAddedToWhitelist;
            watcher.Disable();
            watcher.PlayerChanged -= Watcher_PlayerChanged;
            watcher?.Dispose();
        }

        internal void StartWatchingPlayer()
        {
            watcher.AddPlayerToWatch(GetPlayerName());
            watcher.PlayerChanged += Watcher_PlayerChanged;
            watcher.Enable();
            apiController.Connected += ApiController_Connected;
            apiController.Disconnected += ApiController_Disconnected;
            apiController.CharacterReceived += ApiControllerOnCharacterReceived;
            apiController.RemovedFromWhitelist += ApiControllerOnRemovedFromWhitelist;
            apiController.AddedToWhitelist += ApiControllerOnAddedToWhitelist;

            PluginLog.Debug("Watching Player, ApiController is Connected: " + apiController.IsConnected);
            if (apiController.IsConnected)
            {
                ApiController_Connected(null, EventArgs.Empty);
            }
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
                PluginLog.Debug("You got removed from whitelist, restoring glamourer state for " + playerName);
                ipcManager.GlamourerRevertCharacterCustomization(playerName);
            }
        }

        private void ApiControllerOnAddedToWhitelist(object? sender, EventArgs e)
        {
            var characterHash = (string?)sender;
            if (string.IsNullOrEmpty(characterHash)) return;
            var players = GetLocalPlayers();
            if (players.ContainsKey(characterHash))
            {
                PluginLog.Debug("You got added to a whitelist, restoring data for " + characterHash);
                _ = apiController.GetCharacterData(new Dictionary<string, int> { { characterHash, (int)players[characterHash].ClassJob.Id } });
            }
        }

        private void ApiControllerOnCharacterReceived(object? sender, CharacterReceivedEventArgs e)
        {
            PlayerCharacter? playerObject = null;
            PluginLog.Debug("Received hash for " + e.CharacterNameHash);
            foreach (var obj in objectTable)
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

            _characterCache[(e.CharacterNameHash, e.CharacterData.JobId)] = e.CharacterData;

            foreach (var file in e.CharacterData.FileReplacements)
            {
                var hash = file.Hash;
                bool hasLocalFile;
                using (var db = new FileCacheContext())
                {
                    hasLocalFile = db.FileCaches.Any(f => f.Hash == hash);
                }

                if (hasLocalFile) continue;
                PluginLog.Debug("Downloading file for " + hash);
                var task = apiController.DownloadData(hash);
                while (!task.IsCompleted)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(0.5));
                }
                PluginLog.Debug("Download finished: " + hash);
                var extractedFile = LZ4.LZ4Codec.Unwrap(task.Result);
                var ext = file.GamePaths.First().Split(".", StringSplitOptions.None).Last();
                var filePath = Path.Combine(_pluginConfiguration.CacheFolder, file.Hash + "." + ext);
                File.WriteAllBytes(filePath, extractedFile);
                PluginLog.Debug("File written to : " + filePath);
                using (var db = new FileCacheContext())
                {
                    db.Add(new FileCache
                    {
                        Filepath = filePath.ToLower(),
                        Hash = file.Hash,
                        LastModifiedDate = DateTime.Now.Ticks.ToString(),
                    });
                    db.SaveChanges();
                }
                PluginLog.Debug("Added hash to db: " + hash);
            }

            PluginLog.Debug("Assigned hash to visible player: " + playerObject.Name.ToString());
            ipcManager.GlamourerApplyCharacterCustomization(e.CharacterData.GlamourerData, playerObject.Name.ToString());
        }

        private void ApiController_Disconnected(object? sender, EventArgs args)
        {
            PluginLog.Debug(nameof(ApiController_Disconnected));
            _framework.Update -= Framework_Update;
            ipcManager.PenumbraRedrawEvent -= IpcManager_PenumbraRedrawEvent;
            clientState.TerritoryChanged -= ClientState_TerritoryChanged;
        }

        private void ApiController_Connected(object? sender, EventArgs args)
        {
            PluginLog.Debug(nameof(ApiController_Connected));
            PluginLog.Debug("MyHashedName:" + Crypto.GetHash256(GetPlayerName() + clientState.LocalPlayer!.HomeWorld.Id));
            var apiTask = apiController.SendCharacterName(Crypto.GetHash256(GetPlayerName() + clientState.LocalPlayer!.HomeWorld.Id));
            var assignTask = AssignLocalPlayersData();

            Task.WaitAll(apiTask, assignTask);

            _framework.Update += Framework_Update;
            ipcManager.PenumbraRedrawEvent += IpcManager_PenumbraRedrawEvent;
            clientState.TerritoryChanged += ClientState_TerritoryChanged;
        }

        public void StopWatchPlayer(string name)
        {
            watcher.RemovePlayerFromWatch(name);
        }

        public void WatchPlayer(string name)
        {
            watcher.AddPlayerToWatch(name);
        }

        private async Task<CharacterCache> CreateFullCharacterCache()
        {
            var cache = BuildCharacterCache();
            cache.SetGlamourerData(ipcManager.GlamourerGetCharacterCustomization()!);
            cache.JobId = clientState.LocalPlayer!.ClassJob.Id;
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

        public async Task DebugJson()
        {
            var cache = CreateFullCharacterCache();
            while (!cache.IsCompleted)
            {
                await Task.Delay(50);
            }

            PluginLog.Debug(JsonConvert.SerializeObject(cache.Result, Formatting.Indented));
        }
    }
}
