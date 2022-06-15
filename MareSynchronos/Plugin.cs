using Dalamud.Game.Command;
using Dalamud.Logging;
using Dalamud.Plugin;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Factories;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MareSynchronos.Hooks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState;
using System;
using MareSynchronos.Models;
using Dalamud.Game.Gui;
using MareSynchronos.PenumbraMod;
using Newtonsoft.Json;
using MareSynchronos.Managers;
using LZ4;
using MareSynchronos.WebAPI;

namespace MareSynchronos
{
    public sealed class Plugin : IDalamudPlugin
    {
        private const string commandName = "/mare";
        private readonly ClientState clientState;
        private readonly Framework framework;
        private readonly GameGui gameGui;
        private readonly ObjectTable objectTable;
        private readonly ApiController apiController;
        private CharacterManager? characterManager;
        private IpcManager? ipcManager;
        public Plugin(DalamudPluginInterface pluginInterface, CommandManager commandManager,
            Framework framework, ObjectTable objectTable, ClientState clientState, GameGui gameGui)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;
            this.framework = framework;
            this.objectTable = objectTable;
            this.clientState = clientState;
            this.gameGui = gameGui;
            Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(this.PluginInterface);

            apiController = new ApiController(Configuration);

            // you might normally want to embed resources and load them from the manifest stream
            this.PluginUi = new PluginUI(this.Configuration);

            new FileCacheContext().Dispose(); // make sure db is initialized I guess

            clientState.Login += ClientState_Login;
            clientState.Logout += ClientState_Logout;

            if (clientState.IsLoggedIn)
            {
                ClientState_Login(null, null!);
            }

            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
        }

        public string Name => "Mare Synchronos";
        private CommandManager CommandManager { get; init; }
        private Configuration Configuration { get; init; }
        private DalamudPluginInterface PluginInterface { get; init; }
        private PluginUI PluginUi { get; init; }
        public void Dispose()
        {
            this.PluginUi?.Dispose();
            this.CommandManager.RemoveHandler(commandName);
            clientState.Login -= ClientState_Login;
            clientState.Logout -= ClientState_Logout;
            ipcManager?.Dispose();
            characterManager?.Dispose();
        }

        private void ClientState_Login(object? sender, EventArgs e)
        {
            PluginLog.Debug("Client login");
            ipcManager = new IpcManager(PluginInterface);
            ipcManager.IpcManagerInitialized += IpcManager_IpcManagerInitialized;
            ipcManager.Initialize();

            CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "pass 'scan' to initialize or rescan files into the database"
            });
        }

        private void ClientState_Logout(object? sender, EventArgs e)
        {
            PluginLog.Debug("Client logout");
            characterManager?.Dispose();
            ipcManager?.Dispose();
            ipcManager = null!;
            CommandManager.RemoveHandler(commandName);
        }

        private void CopyFile(FileReplacement replacement, string targetDirectory, Dictionary<string, string>? resourceDict = null)
        {
            if (replacement.HasFileReplacement)
            {
                PluginLog.Debug("Copying file \"" + replacement.ResolvedPath + "\"");
                var db1 = new FileCacheContext();
                var fileCache = db1.FileCaches.Single(f => f.Filepath.Contains(replacement.ResolvedPath.Replace('/', '\\')));
                db1.Dispose();
                try
                {
                    var ext = new FileInfo(fileCache.Filepath).Extension;
                    var newFilePath = Path.Combine(targetDirectory, "files", fileCache.Hash.ToLower() + ext);
                    string lc4hcPath = Path.Combine(targetDirectory, "files", "lz4hc." + fileCache.Hash.ToLower() + ext);
                    if (!File.Exists(lc4hcPath))
                    {

                        Stopwatch st = Stopwatch.StartNew();
                        File.WriteAllBytes(lc4hcPath, LZ4Codec.Encode(File.ReadAllBytes(fileCache.Filepath), 0, (int)new FileInfo(fileCache.Filepath).Length));
                        st.Stop();
                        PluginLog.Debug("Compressed " + new FileInfo(fileCache.Filepath).Length + " bytes to " + new FileInfo(lc4hcPath).Length + " bytes in " + st.Elapsed);
                        File.Copy(fileCache.Filepath, newFilePath);
                        if (resourceDict != null)
                        {
                            resourceDict[replacement.GamePath] = $"files\\{fileCache.Hash.ToLower() + ext}";
                        }
                        else
                        {
                            File.AppendAllLines(Path.Combine(targetDirectory, "filelist.txt"), new[] { $"\"{replacement.GamePath}\": \"files\\\\{fileCache.Hash.ToLower() + ext}\"," });
                        }
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "error during copy");
                }
            }
        }

        private void DrawConfigUI()
        {
            this.PluginUi.SettingsVisible = true;
        }

        private void DrawUI()
        {
            this.PluginUi.Draw();
        }

        private void IpcManager_IpcManagerInitialized(object? sender, EventArgs e)
        {
            PluginLog.Debug("IPC Manager initialized event");
            ipcManager!.IpcManagerInitialized -= IpcManager_IpcManagerInitialized;
            Task.Run(async () =>
            {
                while (clientState.LocalPlayer == null)
                {
                    await Task.Delay(500);
                }

                characterManager = new CharacterManager(
                    new DrawHooks(PluginInterface, clientState, objectTable, new FileReplacementFactory(ipcManager, clientState), gameGui),
                    clientState, framework, apiController, objectTable, ipcManager);
                ipcManager.PenumbraRedraw(clientState.LocalPlayer!.Name.ToString());
            });
        }

        private void OnCommand(string command, string args)
        {
            if (args == "print")
            {
                characterManager?.PrintRequestedResources();
            }

            if (args == "printjson")
            {
                characterManager?.DebugJson();
            }

            if (args == "createtestmod")
            {
                Task.Run(() =>
                {
                    var playerName = clientState.LocalPlayer!.Name.ToString();
                    var modName = $"Mare Synchronos Test Mod {playerName}";
                    var modDirectory = ipcManager!.PenumbraModDirectory()!;
                    string modDirectoryPath = Path.Combine(modDirectory, modName);
                    if (Directory.Exists(modDirectoryPath))
                    {
                        Directory.Delete(modDirectoryPath, true);
                    }

                    Directory.CreateDirectory(modDirectoryPath);
                    Directory.CreateDirectory(Path.Combine(modDirectoryPath, "files"));
                    Meta meta = new()
                    {
                        Name = modName,
                        Author = playerName,
                        Description = "Mare Synchronous Test Mod Export",
                    };

                    var resources = characterManager!.GetCharacterCache();
                    var metaJson = JsonConvert.SerializeObject(meta);
                    File.WriteAllText(Path.Combine(modDirectoryPath, "meta.json"), metaJson);

                    DefaultMod defaultMod = new();

                    //using var db = new FileCacheContext();
                    Stopwatch st = Stopwatch.StartNew();
                    Parallel.ForEach(resources.AllReplacements, resource =>
                    {
                        CopyFile(resource, modDirectoryPath, defaultMod.Files);
                    });
                    PluginLog.Debug("Compression took " + st.Elapsed);

                    var defaultModJson = JsonConvert.SerializeObject(defaultMod);
                    File.WriteAllText(Path.Combine(modDirectoryPath, "default_mod.json"), defaultModJson);

                    PluginLog.Debug("Mod created to " + modDirectoryPath);
                });
            }
        }
    }
}
