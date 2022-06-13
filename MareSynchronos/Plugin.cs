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

namespace SamplePlugin
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Mare Synchronos";

        private const string commandName = "/pscan";

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
            Framework framework, ObjectTable objectTable, ClientState clientState, DataManager dataManager)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;

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
            drawHooks = new DrawHooks(pluginInterface, clientState, objectTable, new MareSynchronos.Models.FileReplacementFactory(pluginInterface, clientState));
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

            if (args == "copy")
            {
                var resources = drawHooks.PrintRequestedResources();
                Task.Run(() =>
                {
                    PluginLog.Debug("Copying files");
                    foreach (var file in Directory.GetFiles(@"G:\Penumbra\TestMod\files"))
                    {
                        File.Delete(file);
                    }
                    File.Delete(@"G:\Penumbra\testmod\filelist.txt");
                    using FileCacheContext db = new FileCacheContext();
                    foreach (var resource in resources)
                    {
                        CopyRecursive(resource, db);
                    }
                });
            }
        }

        private void CopyRecursive(FileReplacement replacement, FileCacheContext db)
        {
            if (replacement.HasFileReplacement)
            {
                PluginLog.Debug("Copying file \"" + replacement.ReplacedPath + "\"");

                var fileCache = db.FileCaches.Single(f => f.Filepath.Contains(replacement.ReplacedPath.Replace('/', '\\')));
                try
                {
                    var ext = new FileInfo(fileCache.Filepath).Extension;
                    File.Copy(fileCache.Filepath, Path.Combine(@"G:\Penumbra\TestMod\files", fileCache.Hash.ToLower() + ext));
                    File.AppendAllLines(Path.Combine(@"G:\Penumbra\TestMod", "filelist.txt"), new[] { $"\"{replacement.GamePath}\": \"files\\\\{fileCache.Hash.ToLower() + ext}\"," });
                }
                catch { }
            }

            foreach (var associated in replacement.Associated)
            {
                CopyRecursive(associated, db);
            }
        }

        private void PlayerWatch_PlayerChanged(Dalamud.Game.ClientState.Objects.Types.Character actor)
        {
            var equipment = playerWatch.UpdatePlayerWithoutEvent(actor);
            var customization = new CharacterCustomization(actor);
            DebugCustomization(customization);
            //PluginLog.Debug(customization.Gender.ToString());
            if (equipment != null)
            {
                PluginLog.Debug(equipment.ToString());
            }
        }

        private void DebugCustomization(CharacterCustomization customization)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Gender: " + customization[CustomizationId.Gender].ToString() + ":" + customization.Gender.ToName());
            sb.AppendLine("Race: " + customization[CustomizationId.Race].ToString() + ":" + GetBodyRaceCode(customization));
            sb.AppendLine("Face: " + customization.Face.ToString());

            PluginLog.Debug(sb.ToString());
        }

        private string GetBodyRaceMdlPath(CharacterCustomization customization)
        {
            return "";
        }

        private string GetBodyRaceCode(CharacterCustomization customization)
        {
            return Names.CombinedRace(customization.Gender, GetBodyRace(FromSubRace(customization.Clan))).ToRaceCode();
        }

        private ModelRace GetBodyRace(ModelRace modelRace)
        {
            return modelRace switch
            {
                ModelRace.AuRa => ModelRace.AuRa,
                ModelRace.Miqote => ModelRace.Midlander,
                ModelRace.Highlander => ModelRace.Highlander,
                ModelRace.Lalafell => ModelRace.Lalafell,
                ModelRace.Midlander => ModelRace.Midlander,
                ModelRace.Elezen => ModelRace.Midlander,
                ModelRace.Hrothgar => ModelRace.Hrothgar,
            };
        }

        private ModelRace FromSubRace(SubRace race)
        {
            return race switch
            {
                SubRace.Xaela => ModelRace.AuRa,
                SubRace.Raen => ModelRace.AuRa,
                SubRace.Highlander => ModelRace.Highlander,
                SubRace.Midlander => ModelRace.Midlander,
                SubRace.Plainsfolk => ModelRace.Lalafell,
                SubRace.Dunesfolk => ModelRace.Lalafell,
                SubRace.SeekerOfTheSun => ModelRace.Miqote,
                SubRace.KeeperOfTheMoon => ModelRace.Miqote,
                SubRace.Seawolf => ModelRace.Roegadyn,
                SubRace.Hellsguard => ModelRace.Roegadyn,
                SubRace.Rava => ModelRace.Viera,
                SubRace.Veena => ModelRace.Viera,
                SubRace.Wildwood => ModelRace.Elezen,
                SubRace.Duskwight => ModelRace.Elezen,
                SubRace.Helion => ModelRace.Hrothgar,
                SubRace.Lost => ModelRace.Hrothgar,
                _ => ModelRace.Unknown
            };
        }

        private void StartScan()
        {
            Stopwatch st = Stopwatch.StartNew();

            string penumbraDir = Configuration.PenumbraFolder;
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
