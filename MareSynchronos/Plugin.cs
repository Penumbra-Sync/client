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
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState;
using System;
using MareSynchronos.Models;
using MareSynchronos.PenumbraMod;
using Newtonsoft.Json;
using MareSynchronos.Managers;
using LZ4;
using MareSynchronos.WebAPI;
using Dalamud.Interface.Windowing;
using MareSynchronos.UI;

namespace MareSynchronos
{
    public sealed class Plugin : IDalamudPlugin
    {
        private const string CommandName = "/mare";
        private readonly ApiController _apiController;
        private readonly ClientState _clientState;
        private readonly CommandManager _commandManager;
        private readonly Configuration _configuration;
        private readonly FileCacheManager _fileCacheManager;
        private readonly Framework _framework;
        private readonly IntroUI _introUi;
        private readonly IpcManager _ipcManager;
        private readonly ObjectTable _objectTable;
        private readonly DalamudPluginInterface _pluginInterface;
        private readonly PluginUi _pluginUi;
        private readonly WindowSystem _windowSystem;
        private CharacterManager? _characterManager;
        public Plugin(DalamudPluginInterface pluginInterface, CommandManager commandManager,
            Framework framework, ObjectTable objectTable, ClientState clientState)
        {
            _pluginInterface = pluginInterface;
            _commandManager = commandManager;
            _framework = framework;
            _objectTable = objectTable;
            _clientState = clientState;
            _configuration = _pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            _configuration.Initialize(_pluginInterface);

            _windowSystem = new WindowSystem("MareSynchronos");

            _apiController = new ApiController(_configuration);
            _ipcManager = new IpcManager(_pluginInterface);
            _fileCacheManager = new FileCacheManager(new FileCacheFactory(), _ipcManager, _configuration);

            var uiSharedComponent =
                new UIShared(_ipcManager, _apiController, _fileCacheManager, _configuration);

            // you might normally want to embed resources and load them from the manifest stream
            _pluginUi = new PluginUi(_windowSystem, uiSharedComponent, _configuration, _apiController);
            _introUi = new IntroUI(_windowSystem, uiSharedComponent, _configuration, _fileCacheManager);
            _introUi.FinishedRegistration += (_, _) =>
            {
                _pluginUi.IsOpen = true;
                _introUi?.Dispose();
                ClientState_Login(null, EventArgs.Empty);
            };

            new FileCacheContext().Dispose(); // make sure db is initialized I guess

            clientState.Login += ClientState_Login;
            clientState.Logout += ClientState_Logout;

            if (clientState.IsLoggedIn)
            {
                ClientState_Login(null, null!);
            }
        }

        public string Name => "Mare Synchronos";
        public void Dispose()
        {
            _commandManager.RemoveHandler(CommandName);
            _clientState.Login -= ClientState_Login;
            _clientState.Logout -= ClientState_Logout;

            _pluginUi?.Dispose();
            _introUi?.Dispose();

            _fileCacheManager?.Dispose();
            _ipcManager?.Dispose();
            _characterManager?.Dispose();
            _apiController?.Dispose();
        }

        private void ClientState_Login(object? sender, EventArgs e)
        {
            PluginLog.Debug("Client login");

            _pluginInterface.UiBuilder.Draw += Draw;

            if (!_configuration.HasValidSetup)
            {
                _introUi.IsOpen = true;
                return;
            }
            else
            {
                _introUi.IsOpen = false;
            }

            Task.Run(async () =>
            {
                while (_clientState.LocalPlayer == null)
                {
                    await Task.Delay(50);
                }

                _characterManager = new CharacterManager(
                    _clientState, _framework, _apiController, _objectTable, _ipcManager, new FileReplacementFactory(_ipcManager), _configuration);
                _characterManager.StartWatchingPlayer();
                _ipcManager.PenumbraRedraw(_clientState.LocalPlayer!.Name.ToString());
            });

            _pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;

            _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the Mare Synchronos UI"
            });
        }

        private void ClientState_Logout(object? sender, EventArgs e)
        {
            PluginLog.Debug("Client logout");
            _characterManager?.Dispose();
            _pluginInterface.UiBuilder.Draw -= Draw;
            _pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
            _commandManager.RemoveHandler(CommandName);
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
                    string lc4HcPath = Path.Combine(targetDirectory, "files", "lz4hc." + fileCache.Hash.ToLower() + ext);
                    if (!File.Exists(lc4HcPath))
                    {

                        Stopwatch st = Stopwatch.StartNew();
                        File.WriteAllBytes(lc4HcPath, LZ4Codec.WrapHC(File.ReadAllBytes(fileCache.Filepath), 0, (int)new FileInfo(fileCache.Filepath).Length));
                        st.Stop();
                        PluginLog.Debug("Compressed " + new FileInfo(fileCache.Filepath).Length + " bytes to " + new FileInfo(lc4HcPath).Length + " bytes in " + st.Elapsed);
                        File.Copy(fileCache.Filepath, newFilePath);
                        if (resourceDict != null)
                        {
                            foreach (var path in replacement.GamePaths)
                            {
                                resourceDict[path] = $"files\\{fileCache.Hash.ToLower() + ext}";
                            }
                        }
                        else
                        {
                            //File.AppendAllLines(Path.Combine(targetDirectory, "filelist.txt"), new[] { $"\"{replacement.GamePath}\": \"files\\\\{fileCache.Hash.ToLower() + ext}\"," });
                        }
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "error during copy");
                }
            }
        }

        private void Draw()
        {
            _windowSystem.Draw();
        }

        private void OnCommand(string command, string args)
        {
            if (args == "printjson")
            {
                _ = _characterManager?.DebugJson();
            }

            if (args.StartsWith("watch"))
            {
                var playerName = args.Replace("watch", "").Trim();
                _characterManager!.WatchPlayer(playerName);
            }

            if (args.StartsWith("stop"))
            {
                var playerName = args.Replace("watch", "").Trim();
                _characterManager!.StopWatchPlayer(playerName);
            }

            if (args == "createtestmod")
            {
                Task.Run(() =>
                {
                    var playerName = _clientState.LocalPlayer!.Name.ToString();
                    var modName = $"Mare Synchronos Test Mod {playerName}";
                    var modDirectory = _ipcManager!.PenumbraModDirectory()!;
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

                    var resources = _characterManager!.BuildCharacterCache();
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

            if (string.IsNullOrEmpty(args))
            {
                _pluginUi.Toggle();
            }
        }

        private void OpenConfigUi()
        {
            _pluginUi.Toggle();
        }
    }
}
