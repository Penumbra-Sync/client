using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using MareSynchronos.Hooks;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MareSynchronos.Managers
{
    public class CharacterManager : IDisposable
    {
        private readonly DrawHooks drawHooks;
        private readonly ClientState clientState;
        private readonly Framework framework;
        private readonly ApiController apiController;
        private readonly ObjectTable objectTable;
        private readonly IpcManager ipcManager;
        private Task? drawHookTask = null;

        public CharacterManager(DrawHooks drawhooks, ClientState clientState, Framework framework, ApiController apiController, ObjectTable objectTable, IpcManager ipcManager)
        {
            this.drawHooks = drawhooks;
            this.clientState = clientState;
            this.framework = framework;
            this.apiController = apiController;
            this.objectTable = objectTable;
            this.ipcManager = ipcManager;
            drawHooks.StartHooks();
            clientState.TerritoryChanged += ClientState_TerritoryChanged;
            framework.Update += Framework_Update;
            drawhooks.PlayerLoadEvent += Drawhooks_PlayerLoadEvent;
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

        private void ClientState_TerritoryChanged(object? sender, ushort e)
        {
            localPlayers.Clear();
        }

        public void Dispose()
        {
            framework.Update -= Framework_Update;
            drawHooks.PlayerLoadEvent -= Drawhooks_PlayerLoadEvent;
            clientState.TerritoryChanged -= ClientState_TerritoryChanged;
            drawHooks?.Dispose();
        }

        private unsafe void Drawhooks_PlayerLoadEvent(object? sender, EventArgs e)
        {
            if (sender == null) return;
            if (drawHookTask != null && !drawHookTask.IsCompleted) return;

            var obj = (GameObject*)(IntPtr)sender;
            drawHookTask = Task.Run(() =>
            {
                PluginLog.Debug("Waiting for charater to be drawn");
                while ((obj->RenderFlags & 0b100000000000) == 0b100000000000) // 0b100000000000 is "still rendering" or something
                {
                    Thread.Sleep(10);
                }
                PluginLog.Debug("Character finished drawing");

                // wait one more second just in case
                Thread.Sleep(1000);

                var cache = CreateFullCharacterCache();
                while (!cache.IsCompleted)
                {
                    Task.Delay(50);
                }

                _ = apiController.SendCharacterData(cache.Result);
            });
        }

        public CharacterCache GetCharacterCache() => drawHooks.BuildCharacterCache();

        public void PrintRequestedResources() => drawHooks.PrintRequestedResources();

        private async Task<CharacterCache> CreateFullCharacterCache()
        {
            var cache = drawHooks.BuildCharacterCache();
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

        public void DebugJson()
        {
            var cache = CreateFullCharacterCache();
            while (!cache.IsCompleted)
            {
                Task.Delay(50);
            }

            PluginLog.Debug(JsonConvert.SerializeObject(cache.Result, Formatting.Indented));
        }
    }
}
