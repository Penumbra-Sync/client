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

        private CancellationTokenSource cts;

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] CommandManager commandManager)
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
        }

        public void Dispose()
        {
            this.PluginUi.Dispose();
            this.CommandManager.RemoveHandler(commandName);
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
