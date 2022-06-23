using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Logging;
using MareSynchronos.Factories;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Utils;

namespace MareSynchronos.Managers
{
    internal class FileCacheManager : IDisposable
    {
        private const int MinutesForScan = 10;
        private readonly FileCacheFactory _fileCacheFactory;
        private readonly IpcManager _ipcManager;
        private readonly Configuration _pluginConfiguration;
        private CancellationTokenSource? _scanCancellationTokenSource;
        private System.Timers.Timer? _scanScheduler;
        private Task? _scanTask;
        private Stopwatch? _timerStopWatch;
        public FileCacheManager(FileCacheFactory fileCacheFactory, IpcManager ipcManager, Configuration pluginConfiguration)
        {
            Logger.Debug("Creating " + nameof(FileCacheManager));

            _fileCacheFactory = fileCacheFactory;
            _ipcManager = ipcManager;
            _pluginConfiguration = pluginConfiguration;

            if (_ipcManager.CheckPenumbraApi()
               && _pluginConfiguration.AcceptedAgreement
               && !string.IsNullOrEmpty(_pluginConfiguration.CacheFolder)
               && _pluginConfiguration.ClientSecret.ContainsKey(_pluginConfiguration.ApiUri)
               && !string.IsNullOrEmpty(_ipcManager.PenumbraModDirectory()))
            {
                StartInitialScan();
            }
        }

        public long CurrentFileProgress { get; private set; }
        public bool IsScanRunning => !_scanTask?.IsCompleted ?? false;

        public TimeSpan TimeToNextScan => TimeSpan.FromMinutes(MinutesForScan).Subtract(_timerStopWatch?.Elapsed ?? TimeSpan.FromMinutes(MinutesForScan));
        public long TotalFiles { get; private set; }

        public void Dispose()
        {
            Logger.Debug("Disposing " + nameof(FileCacheManager));

            _scanScheduler?.Stop();
            _scanCancellationTokenSource?.Cancel();
        }

        public void StartInitialScan()
        {
            _scanCancellationTokenSource = new CancellationTokenSource();
            _scanTask = Task.Run(() => StartFileScan(_scanCancellationTokenSource.Token));
        }

        private async Task StartFileScan(CancellationToken ct)
        {
            _scanCancellationTokenSource = new CancellationTokenSource();
            var penumbraDir = _ipcManager.PenumbraModDirectory()!;
            Logger.Debug("Getting files from " + penumbraDir);
            var scannedFiles = new ConcurrentDictionary<string, bool>(
                Directory.EnumerateFiles(penumbraDir, "*.*", SearchOption.AllDirectories)
                                .Select(s => s.ToLowerInvariant())
                                .Where(f => f.Contains(@"\chara\") && (f.EndsWith(".tex") || f.EndsWith(".mdl") || f.EndsWith(".mtrl")))
                                .Select(p => new KeyValuePair<string, bool>(p, false)));
            List<FileCache> fileCaches;
            await using (FileCacheContext db = new())
            {
                fileCaches = db.FileCaches.ToList();
            }

            TotalFiles = scannedFiles.Count;

            var fileCachesToUpdate = new ConcurrentBag<FileCache>();
            var fileCachesToDelete = new ConcurrentBag<FileCache>();
            var fileCachesToAdd = new ConcurrentBag<FileCache>();

            Logger.Debug("Getting file list from Database");
            // scan files from database
            Parallel.ForEach(fileCaches, new ParallelOptions()
            {
                MaxDegreeOfParallelism = _pluginConfiguration.MaxParallelScan,
                CancellationToken = ct,
            },
            cache =>
            {
                if (ct.IsCancellationRequested) return;
                if (!File.Exists(cache.Filepath))
                {
                    fileCachesToDelete.Add(cache);
                }
                else
                {
                    if (scannedFiles.ContainsKey(cache.Filepath))
                    {
                        scannedFiles[cache.Filepath] = true;
                    }
                    FileInfo fileInfo = new(cache.Filepath);
                    if (fileInfo.LastWriteTimeUtc.Ticks == long.Parse(cache.LastModifiedDate)) return;
                    _fileCacheFactory.UpdateFileCache(cache);
                    fileCachesToUpdate.Add(cache);
                }

                var files = CurrentFileProgress;
                Interlocked.Increment(ref files);
                CurrentFileProgress = files;
            });

            if (ct.IsCancellationRequested) return;

            // scan new files
            Parallel.ForEach(scannedFiles.Where(c => c.Value == false), new ParallelOptions()
            {
                MaxDegreeOfParallelism = _pluginConfiguration.MaxParallelScan,
                CancellationToken = ct
            },
            file =>
            {
                fileCachesToAdd.Add(_fileCacheFactory.Create(file.Key));

                var files = CurrentFileProgress;
                Interlocked.Increment(ref files);
                CurrentFileProgress = files;
            });

            await using (FileCacheContext db = new())
            {
                if (fileCachesToAdd.Any() || fileCachesToUpdate.Any() || fileCachesToDelete.Any())
                {
                    db.FileCaches.AddRange(fileCachesToAdd);
                    db.FileCaches.UpdateRange(fileCachesToUpdate);
                    db.FileCaches.RemoveRange(fileCachesToDelete);

                    await db.SaveChangesAsync(ct);
                }
            }

            Logger.Debug("Scan complete");
            TotalFiles = 0;
            CurrentFileProgress = 0;

            if (!_pluginConfiguration.InitialScanComplete)
            {
                _pluginConfiguration.InitialScanComplete = true;
                _pluginConfiguration.Save();
                _timerStopWatch = Stopwatch.StartNew();
                StartScheduler();
            }
            else if (_timerStopWatch == null)
            {
                StartScheduler();
                _timerStopWatch = Stopwatch.StartNew();
            }
        }

        private void StartScheduler()
        {
            Logger.Debug("Scheduling next scan for in " + MinutesForScan + " minutes");
            _scanScheduler = new System.Timers.Timer(TimeSpan.FromMinutes(MinutesForScan).TotalMilliseconds)
            {
                AutoReset = false,
                Enabled = false,
            };
            _scanScheduler.AutoReset = true;
            _scanScheduler.Elapsed += (_, _) =>
            {
                _timerStopWatch?.Stop();
                if (_scanTask?.IsCompleted ?? false)
                {
                    PluginLog.Warning("Scanning task is still running, not re-initiating.");
                    return;
                }

                Logger.Debug("Initiating periodic scan for mod changes");
                Task.Run(() => _scanTask = StartFileScan(_scanCancellationTokenSource!.Token));
                _timerStopWatch = Stopwatch.StartNew();
            };

            _scanScheduler.Start();
        }
    }
}
