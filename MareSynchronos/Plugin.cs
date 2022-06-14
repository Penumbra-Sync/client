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
using Glamourer.Customization;
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

namespace SamplePlugin
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Mare Synchronos";

        private const string commandName = "/mare";
        private readonly ClientState clientState;

        private DalamudPluginInterface PluginInterface { get; init; }
        private CommandManager CommandManager { get; init; }
        private Configuration Configuration { get; init; }
        private PluginUI PluginUi { get; init; }
        private FileCacheFactory FileCacheFactory { get; init; }
        private DrawHooks drawHooks;

        private CancellationTokenSource cts;
        private IPlayerWatcher playerWatch;

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] CommandManager commandManager,
            Framework framework, ObjectTable objectTable, ClientState clientState, DataManager dataManager, GameGui gameGui)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;
            this.clientState = clientState;
            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);

            // you might normally want to embed resources and load them from the manifest stream
            this.PluginUi = new PluginUI(this.Configuration);

            this.CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "pass 'scan' to initialize or rescan files into the database"
            });

            FileCacheFactory = new FileCacheFactory();

            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

            playerWatch = PlayerWatchFactory.Create(framework, clientState, objectTable);
            drawHooks = new DrawHooks(pluginInterface, clientState, objectTable, new FileReplacementFactory(pluginInterface, clientState), gameGui);
        }

        public void Dispose()
        {
            this.PluginUi.Dispose();
            this.CommandManager.RemoveHandler(commandName);
            playerWatch.PlayerChanged -= PlayerWatch_PlayerChanged;
            playerWatch.RemovePlayerFromWatch("Ilya Zhelmo");
            drawHooks.Dispose();
        }

        private void OnCommand(string command, string args)
        {
            if (args == "stop")
            {
                cts?.Cancel();
                return;
            }

            if(args == "playerdata")
            {
                PluginLog.Debug(PluginInterface.GetIpcSubscriber<string>("Glamourer.GetCharacterCustomization").InvokeFunc());
            }

            if(args == "applyglam")
            {
                PluginInterface.GetIpcSubscriber<string, string, object>("Glamourer.ApplyCharacterCustomization")
                    .InvokeAction("Ah3/DwQBAR4IBHOABIOceTIIApkDAgADQmQBZJqepQZlAAEAAAAAAAAAAACcEwEAyxcBbrAXAUnKFwJIuBcBBkYAAQBIAAEANQABADUAAQACAAQAAQAAAIA/Eg==", "Ilya Zhelmo");
            }

            if (args == "scan")
            {
                cts = new CancellationTokenSource();

                Task.Run(() => StartScan(), cts.Token);
            }

            if (args == "watch")
            {
                playerWatch.AddPlayerToWatch("Ilya Zhelmo");
                playerWatch.PlayerChanged += PlayerWatch_PlayerChanged;
            }

            if (args == "stopwatch")
            {
                playerWatch.PlayerChanged -= PlayerWatch_PlayerChanged;
                playerWatch.RemovePlayerFromWatch("Ilya Zhelmo");
            }

            if (args == "hook")
            {
                drawHooks.StartHooks();
            }

            if (args == "print")
            {
                var resources = drawHooks.PrintRequestedResources();
            }

            if (args == "printjson")
            {
                var cache = drawHooks.BuildCharacterCache();
                var json = JsonConvert.SerializeObject(cache, Formatting.Indented);
                PluginLog.Debug(json);
            }

            if (args == "createtestmod")
            {
                Task.Run(() =>
                {
                    var playerName = clientState.LocalPlayer!.Name.ToString();
                    var modName = $"Mare Synchronos Test Mod {playerName}";
                    var modDirectory = PluginInterface.GetIpcSubscriber<string>("Penumbra.GetModDirectory").InvokeFunc();
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
            var equipment = playerWatch.UpdatePlayerWithoutEvent(actor);
            var customization = new CharacterCustomization(actor);
            //DebugCustomization(customization);
            //PluginLog.Debug(customization.Gender.ToString());
            if (equipment != null)
            {
                PluginLog.Debug(equipment.ToString());
            }
        }

        private void StartScan()
        {
            Stopwatch st = Stopwatch.StartNew();

            string penumbraDir = PluginInterface.GetIpcSubscriber<string>("Penumbra.GetModDirectory").InvokeFunc();
            PluginLog.Debug("Getting files from " + penumbraDir);
            ConcurrentDictionary<string, bool> charaFiles = new ConcurrentDictionary<string, bool>(
                Directory.GetFiles(penumbraDir, "*.*", SearchOption.AllDirectories)
                                .Select(s => s.ToLowerInvariant())
                                .Where(f => !f.EndsWith(".json"))
                                .Where(f => f.Contains(@"\chara\"))
                                .Select(p => new KeyValuePair<string, bool>(p, false)));
            int count = 0;
            using FileCacheContext db = new();
            var fileCaches = db.FileCaches.ToList();

            var fileCachesToUpdate = new ConcurrentBag<FileCache>();
            var fileCachesToDelete = new ConcurrentBag<FileCache>();
            var fileCachesToAdd = new ConcurrentBag<FileCache>();

            // scan files from database
            Parallel.ForEach(fileCaches, new ParallelOptions()
            {
                CancellationToken = cts.Token,
                MaxDegreeOfParallelism = 10
            },
            cache =>
            {
                count = Interlocked.Increment(ref count);
                PluginLog.Debug($"[{count}/{fileCaches.Count}] Checking: {cache.Filepath}");

                if (!File.Exists(cache.Filepath))
                {
                    PluginLog.Debug("File was not found anymore: " + cache.Filepath);
                    fileCachesToDelete.Add(cache);
                }
                else
                {
                    charaFiles[cache.Filepath] = true;

                    FileInfo fileInfo = new(cache.Filepath);
                    if (fileInfo.LastWriteTimeUtc.Ticks != long.Parse(cache.LastModifiedDate))
                    {
                        PluginLog.Debug("File was modified since last time: " + cache.Filepath + "; " + cache.LastModifiedDate + " / " + fileInfo.LastWriteTimeUtc.Ticks);
                        FileCacheFactory.UpdateFileCache(cache);
                        fileCachesToUpdate.Add(cache);
                    }
                }
            });

            // scan new files
            count = 0;
            Parallel.ForEach(charaFiles.Where(c => c.Value == false), new ParallelOptions()
            {
                CancellationToken = cts.Token,
                MaxDegreeOfParallelism = 10
            },
            file =>
            {
                count = Interlocked.Increment(ref count);
                PluginLog.Debug($"[{count}/{charaFiles.Count()}] Hashing: {file.Key}");

                fileCachesToAdd.Add(FileCacheFactory.Create(file.Key));
            });

            st.Stop();

            if (cts.Token.IsCancellationRequested) return;

            PluginLog.Debug("Scanning complete, total elapsed time: " + st.Elapsed.ToString());

            if (fileCachesToAdd.Any() || fileCachesToUpdate.Any() || fileCachesToDelete.Any())
            {
                PluginLog.Debug("Writing files to database…");

                db.FileCaches.AddRange(fileCachesToAdd);
                db.FileCaches.UpdateRange(fileCachesToUpdate);
                db.FileCaches.RemoveRange(fileCachesToDelete);

                db.SaveChanges();
                PluginLog.Debug("Database has been written.");
            }

            cts = new CancellationTokenSource();
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
