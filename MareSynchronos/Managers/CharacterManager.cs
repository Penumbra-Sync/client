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
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MareSynchronos.Managers
{
    public class CharacterManager : IDisposable
    {
        private readonly ClientState clientState;
        private readonly Framework framework;
        private readonly ApiController apiController;
        private readonly ObjectTable objectTable;
        private readonly IpcManager ipcManager;
        private readonly FileReplacementFactory factory;
        private readonly IPlayerWatcher watcher;
        private Task? playerChangedTask = null;

        public CharacterManager(ClientState clientState, Framework framework, ApiController apiController, ObjectTable objectTable, IpcManager ipcManager, FileReplacementFactory factory)
        {
            this.clientState = clientState;
            this.framework = framework;
            this.apiController = apiController;
            this.objectTable = objectTable;
            this.ipcManager = ipcManager;
            this.factory = factory;
            watcher = PlayerWatchFactory.Create(framework, clientState, objectTable);
            clientState.TerritoryChanged += ClientState_TerritoryChanged;
            framework.Update += Framework_Update;
            ipcManager.PenumbraRedrawEvent += IpcManager_PenumbraRedrawEvent;
        }

        private void IpcManager_PenumbraRedrawEvent(object? sender, EventArgs e)
        {
            var actorName = ((string)sender!);
            PluginLog.Debug("Penumbra redraw " + actorName);
            if (actorName == GetPlayerName())
            {
                PlayerChanged(actorName);
            }
        }

        Dictionary<string, string> localPlayers = new();
        private DateTime lastCheck = DateTime.Now;

        private unsafe void Framework_Update(Framework framework)
        {
            try
            {
                if (clientState.LocalPlayer == null) return;

                if (DateTime.Now < lastCheck.AddSeconds(5)) return;

                List<string> localPlayersList = new();
                List<string> newPlayers = new();
                foreach (var obj in objectTable)
                {
                    if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;
                    string playerName = obj.Name.ToString();
                    if (playerName == clientState.LocalPlayer.Name.ToString()) continue;
                    var hashedName = Crypto.GetHash(playerName);
                }

                foreach (var item in localPlayers.ToList())
                {
                    if (!localPlayersList.Contains(item.Key))
                    {
                        localPlayers.Remove(item.Key);
                    }
                }

                if (newPlayers.Any())
                    PluginLog.Debug("New players: " + string.Join(",", newPlayers.Select(p => p + ":" + localPlayers[p])));
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
                    PluginLog.Debug("Waiting for character to finish drawing");
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

                _ = apiController.SendCharacterData(cache.Result);
            });
        }

        public unsafe CharacterCache BuildCharacterCache()
        {
            var cache = new CharacterCache();

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
        }

        public void Dispose()
        {
            framework.Update -= Framework_Update;
            clientState.TerritoryChanged -= ClientState_TerritoryChanged;
            watcher.PlayerChanged -= Watcher_PlayerChanged;
            watcher?.Dispose();
        }

        internal void StartWatchingPlayer()
        {
            watcher.AddPlayerToWatch(clientState.LocalPlayer!.Name.ToString());
            watcher.PlayerChanged += Watcher_PlayerChanged;
            watcher.Enable();
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
