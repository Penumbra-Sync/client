using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Factories;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MareSynchronos.Hooks;
using Penumbra.PlayerWatch;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState;
using Dalamud.Data;
using Lumina.Excel.GeneratedSheets;
using System.Text;
using Penumbra.GameData.Enums;
using System;
using MareSynchronos.Models;
using Dalamud.Game.Gui;
using MareSynchronos.PenumbraMod;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;
using MareSynchronos.Managers;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using MareSynchronos.Utils;

namespace MareSynchronos
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Mare Synchronos";

        private const string commandName = "/mare";
        private readonly Framework framework;
        private readonly ObjectTable objectTable;
        private readonly ClientState clientState;
        private readonly GameGui gameGui;

        private DalamudPluginInterface pluginInterface { get; init; }
        private CommandManager commandManager { get; init; }
        private Configuration configuration { get; init; }
        private PluginUI PluginUi { get; init; }
        private DrawHooks drawHooks;
        private IpcManager ipcManager;

        public Plugin(DalamudPluginInterface pluginInterface, CommandManager commandManager,
            Framework framework, ObjectTable objectTable, ClientState clientState, GameGui gameGui)
        {
            this.pluginInterface = pluginInterface;
            this.commandManager = commandManager;
            this.framework = framework;
            this.objectTable = objectTable;
            this.clientState = clientState;
            this.gameGui = gameGui;
            configuration = this.pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            configuration.Initialize(this.pluginInterface);

            // you might normally want to embed resources and load them from the manifest stream
            this.PluginUi = new PluginUI(this.configuration);

            this.commandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "pass 'scan' to initialize or rescan files into the database"
            });

            FileCacheContext db = new FileCacheContext();
            db.Dispose();

            clientState.Login += ClientState_Login;
            clientState.Logout += ClientState_Logout;

            if (clientState.IsLoggedIn)
            {
                ClientState_Login(null, null!);
            }

            this.pluginInterface.UiBuilder.Draw += DrawUI;
            this.pluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
        }

        private void IpcManager_IpcManagerInitialized(object? sender, EventArgs e)
        {
            PluginLog.Debug("IPC Manager initialized event");
            ipcManager.IpcManagerInitialized -= IpcManager_IpcManagerInitialized;
            Task.Run(async () =>
            {
                while (clientState.LocalPlayer == null)
                {
                    await Task.Delay(500);
                }
                drawHooks.StartHooks();
                ipcManager.PenumbraRedraw(clientState.LocalPlayer!.Name.ToString());
            });
        }

        private void ClientState_Logout(object? sender, EventArgs e)
        {
            PluginLog.Debug("Client logout");
            drawHooks.PlayerLoadEvent -= DrawHooks_PlayerLoadEvent;
            ipcManager.Dispose();
            drawHooks.Dispose();
            ipcManager = null!;
            drawHooks = null!;
        }

        private void ClientState_Login(object? sender, EventArgs e)
        {
            PluginLog.Debug("Client login");
            ipcManager = new IpcManager(pluginInterface);
            drawHooks = new DrawHooks(pluginInterface, clientState, objectTable, new FileReplacementFactory(ipcManager, clientState), gameGui);
            ipcManager.IpcManagerInitialized += IpcManager_IpcManagerInitialized;
            ipcManager.Initialize();
            drawHooks.PlayerLoadEvent += DrawHooks_PlayerLoadEvent;
        }

        private Task drawHookTask;

        private unsafe void DrawHooks_PlayerLoadEvent(object? sender, EventArgs e)
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

                // we should recalculate cache here
                // probably needs a different method
                // at that point we will also have to send data to the api
                _ = drawHooks.BuildCharacterCache();
            });
        }

        public void Dispose()
        {
            this.PluginUi?.Dispose();
            this.commandManager.RemoveHandler(commandName);
            drawHooks.PlayerLoadEvent -= DrawHooks_PlayerLoadEvent;
            clientState.Login -= ClientState_Login;
            clientState.Logout -= ClientState_Logout;
            ipcManager?.Dispose();
            drawHooks?.Dispose();
        }

        private void OnCommand(string command, string args)
        {
            if (args == "print")
            {
                var resources = drawHooks.PrintRequestedResources();
            }

            if (args == "printjson")
            {
                var cache = drawHooks.BuildCharacterCache();
                cache.SetGlamourerData(ipcManager.GlamourerGetCharacterCustomization()!);
                cache.JobId = clientState.LocalPlayer!.ClassJob.Id;
                Task.Run(async () =>
                {
                    while (!cache.IsReady)
                    {
                        await Task.Delay(50);
                    }
                    var json = JsonConvert.SerializeObject(cache, Formatting.Indented);

                    cache.CacheHash = Crypto.GetHash(json);
                    
                    json = JsonConvert.SerializeObject(cache, Formatting.Indented);
                    PluginLog.Debug(json);
                });
            }

            if (args == "createtestmod")
            {
                Task.Run(() =>
                {
                    var playerName = clientState.LocalPlayer!.Name.ToString();
                    var modName = $"Mare Synchronos Test Mod {playerName}";
                    var modDirectory = ipcManager.PenumbraModDirectory()!;
                    string modDirectoryPath = Path.Combine(modDirectory, modName);
                    if (Directory.Exists(modDirectoryPath))
                    {
                        Directory.Delete(modDirectoryPath, true);
                    }

                    Directory.CreateDirectory(modDirectoryPath);
                    Directory.CreateDirectory(Path.Combine(modDirectoryPath, "files"));
                    Meta meta = new Meta()
                    {
                        Name = modName,
                        Author = playerName,
                        Description = "Mare Synchronous Test Mod Export",
                    };

                    var resources = drawHooks.PrintRequestedResources();
                    var metaJson = JsonConvert.SerializeObject(meta);
                    File.WriteAllText(Path.Combine(modDirectoryPath, "meta.json"), metaJson);

                    DefaultMod defaultMod = new DefaultMod();

                    using var db = new FileCacheContext();
                    foreach (var resource in resources)
                    {
                        CopyRecursive(resource, modDirectoryPath, db, defaultMod.Files);
                    }

                    var defaultModJson = JsonConvert.SerializeObject(defaultMod);
                    File.WriteAllText(Path.Combine(modDirectoryPath, "default_mod.json"), defaultModJson);

                    PluginLog.Debug("Mod created to " + modDirectoryPath);
                });
            }
        }

        private void CopyRecursive(FileReplacement replacement, string targetDirectory, FileCacheContext db, Dictionary<string, string>? resourceDict = null)
        {
            if (replacement.HasFileReplacement)
            {
                PluginLog.Debug("Copying file \"" + replacement.ResolvedPath + "\"");

                var fileCache = db.FileCaches.Single(f => f.Filepath.Contains(replacement.ResolvedPath.Replace('/', '\\')));
                try
                {
                    var ext = new FileInfo(fileCache.Filepath).Extension;
                    File.Copy(fileCache.Filepath, Path.Combine(targetDirectory, "files", fileCache.Hash.ToLower() + ext));
                    if (resourceDict != null)
                    {
                        resourceDict[replacement.GamePath] = $"files\\{fileCache.Hash.ToLower() + ext}";
                    }
                    else
                    {
                        File.AppendAllLines(Path.Combine(targetDirectory, "filelist.txt"), new[] { $"\"{replacement.GamePath}\": \"files\\\\{fileCache.Hash.ToLower() + ext}\"," });
                    }
                }
                catch { }
            }

            foreach (var associated in replacement.Associated)
            {
                CopyRecursive(associated, targetDirectory, db, resourceDict);
            }
        }

        private void PlayerWatch_PlayerChanged(Dalamud.Game.ClientState.Objects.Types.Character actor)
        {
            //var equipment = playerWatch.UpdatePlayerWithoutEvent(actor);
            //var customization = new CharacterCustomization(actor);
            //DebugCustomization(customization);
            //PluginLog.Debug(customization.Gender.ToString());
            //if (equipment != null)
            //{
            //   PluginLog.Debug(equipment.ToString());
            //}
        }

        private void DrawUI()
        {
            this.PluginUi.Draw();
        }

        private void DrawConfigUI()
        {
            this.PluginUi.SettingsVisible = true;
        }
    }
}
