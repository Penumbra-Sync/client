using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Logging;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Utils;
using Microsoft.EntityFrameworkCore;

namespace MareSynchronos.Managers
{
    public class FileCacheManager : IDisposable
    {
        private readonly IpcManager _ipcManager;
        private readonly ConcurrentBag<string> _modifiedFiles = new();
        private readonly Configuration _pluginConfiguration;
        private FileSystemWatcher? _cacheDirWatcher;
        private FileSystemWatcher? _penumbraDirWatcher;
        private Task? _rescanTask;
        private readonly CancellationTokenSource _rescanTaskCancellationTokenSource = new();
        private CancellationTokenSource _rescanTaskRunCancellationTokenSource = new();
        private CancellationTokenSource? _scanCancellationTokenSource;
        public FileCacheManager(IpcManager ipcManager, Configuration pluginConfiguration)
        {
            Logger.Verbose("Creating " + nameof(FileCacheManager));

            _ipcManager = ipcManager;
            _pluginConfiguration = pluginConfiguration;

            StartWatchersAndScan();

            _ipcManager.PenumbraInitialized += StartWatchersAndScan;
            _ipcManager.PenumbraDisposed += StopWatchersAndScan;
        }

        public long CurrentFileProgress { get; private set; }

        public long FileCacheSize { get; set; }

        public bool IsScanRunning => CurrentFileProgress > 0 || TotalFiles > 0;

        public long TotalFiles { get; private set; }

        public string WatchedCacheDirectory => (_cacheDirWatcher?.EnableRaisingEvents ?? false) ? _cacheDirWatcher!.Path : "Not watched";

        public string WatchedPenumbraDirectory => (_penumbraDirWatcher?.EnableRaisingEvents ?? false) ? _penumbraDirWatcher!.Path : "Not watched";

        public FileCache? Create(string file, CancellationToken token)
        {
            FileInfo fileInfo = new(file);
            int attempt = 0;
            while (IsFileLocked(fileInfo) && attempt++ <= 10)
            {
                Thread.Sleep(1000);
                Logger.Debug("Waiting for file release " + fileInfo.FullName + " attempt " + attempt);
                token.ThrowIfCancellationRequested();
            }

            if (attempt >= 10) return null;

            var sha1Hash = Crypto.GetFileHash(fileInfo.FullName);
            return new FileCache()
            {
                Filepath = fileInfo.FullName.ToLower(),
                Hash = sha1Hash,
                LastModifiedDate = fileInfo.LastWriteTimeUtc.Ticks.ToString(),
            };
        }

        public void Dispose()
        {
            Logger.Verbose("Disposing " + nameof(FileCacheManager));

            _ipcManager.PenumbraInitialized -= StartWatchersAndScan;
            _ipcManager.PenumbraDisposed -= StopWatchersAndScan;
            _rescanTaskCancellationTokenSource?.Cancel();
            _rescanTaskRunCancellationTokenSource?.Cancel();
            _scanCancellationTokenSource?.Cancel();

            StopWatchersAndScan();
        }

        public void StartInitialScan()
        {
            _scanCancellationTokenSource = new CancellationTokenSource();
            Task.Run(() => StartFileScan(_scanCancellationTokenSource.Token));
        }

        public void StartWatchers()
        {
            if (!_ipcManager.Initialized || string.IsNullOrEmpty(_pluginConfiguration.CacheFolder)) return;
            Logger.Verbose("Starting File System Watchers");
            _penumbraDirWatcher?.Dispose();
            _cacheDirWatcher?.Dispose();

            _penumbraDirWatcher = new FileSystemWatcher(_ipcManager.PenumbraModDirectory()!)
            {
                IncludeSubdirectories = true,
            };
            _penumbraDirWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size;
            _penumbraDirWatcher.Deleted += OnModified;
            _penumbraDirWatcher.Changed += OnModified;
            _penumbraDirWatcher.Renamed += OnModified;
            _penumbraDirWatcher.Filters.Add("*.mtrl");
            _penumbraDirWatcher.Filters.Add("*.mdl");
            _penumbraDirWatcher.Filters.Add("*.tex");
            _penumbraDirWatcher.Error += (sender, args) => PluginLog.Error(args.GetException(), "Error in Penumbra Dir Watcher");
            _penumbraDirWatcher.EnableRaisingEvents = true;

            _cacheDirWatcher = new FileSystemWatcher(_pluginConfiguration.CacheFolder)
            {
                IncludeSubdirectories = false,
            };
            _cacheDirWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size;
            _cacheDirWatcher.Deleted += OnModified;
            _cacheDirWatcher.Changed += OnModified;
            _cacheDirWatcher.Renamed += OnModified;
            _cacheDirWatcher.Filters.Add("*");
            _cacheDirWatcher.Error +=
                (sender, args) => PluginLog.Error(args.GetException(), "Error in Cache Dir Watcher");
            _cacheDirWatcher.EnableRaisingEvents = true;

            Task.Run(RecalculateFileCacheSize);
        }

        private bool IsFileLocked(FileInfo file)
        {
            try
            {
                using var fs = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch
            {
                return true;
            }

            return false;
        }

        private void OnModified(object sender, FileSystemEventArgs e)
        {
            _modifiedFiles.Add(e.FullPath);
            _ = StartRescan();
        }

        private void RecalculateFileCacheSize()
        {
            FileCacheSize = Directory.EnumerateFiles(_pluginConfiguration.CacheFolder).Sum(f =>
            {
                try
                {
                    return new FileInfo(f).Length;
                }
                catch
                {
                    return 0;
                }
            });

            if (FileCacheSize < (long)_pluginConfiguration.MaxLocalCacheInGiB * 1024 * 1024 * 1024) return;

            var allFiles = Directory.EnumerateFiles(_pluginConfiguration.CacheFolder)
                .Select(f => new FileInfo(f)).OrderBy(f => f.LastAccessTime).ToList();
            while (FileCacheSize > (long)_pluginConfiguration.MaxLocalCacheInGiB * 1024 * 1024 * 1024)
            {
                var oldestFile = allFiles.First();
                FileCacheSize -= oldestFile.Length;
                File.Delete(oldestFile.FullName);
                allFiles.Remove(oldestFile);
            }
        }

        public async Task StartRescan(bool force = false)
        {
            _rescanTaskRunCancellationTokenSource.Cancel();
            _rescanTaskRunCancellationTokenSource = new CancellationTokenSource();
            var token = _rescanTaskRunCancellationTokenSource.Token;
            if (!force)
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            while ((!_rescanTask?.IsCompleted ?? false) && !token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }

            if (token.IsCancellationRequested) return;

            Logger.Debug("File changes detected");

            if (!_modifiedFiles.Any()) return;

            _rescanTask = Task.Run(async () =>
            {
                var listCopy = _modifiedFiles.ToList();
                _modifiedFiles.Clear();
                await using var db = new FileCacheContext();
                foreach (var item in listCopy.Distinct())
                {
                    var fi = new FileInfo(item);
                    if (!fi.Exists)
                    {
                        PluginLog.Verbose("Removed: " + item);

                        db.RemoveRange(db.FileCaches.Where(f => f.Filepath.ToLower() == item.ToLower()));
                    }
                    else
                    {
                        PluginLog.Verbose("Changed :" + item);
                        var fileCache = Create(item, _rescanTaskCancellationTokenSource.Token);
                        if (fileCache != null)
                        {
                            db.RemoveRange(db.FileCaches.Where(f => f.Filepath.ToLower() == fileCache.Filepath.ToLower()));
                            await db.AddAsync(fileCache, _rescanTaskCancellationTokenSource.Token);
                        }
                    }
                }

                await db.SaveChangesAsync(_rescanTaskCancellationTokenSource.Token);

                RecalculateFileCacheSize();
            }, _rescanTaskCancellationTokenSource.Token);
        }

        private async Task StartFileScan(CancellationToken ct)
        {
            TotalFiles = 1;
            _scanCancellationTokenSource = new CancellationTokenSource();
            var penumbraDir = _ipcManager.PenumbraModDirectory()!;
            Logger.Debug("Getting files from " + penumbraDir + " and " + _pluginConfiguration.CacheFolder);
            var scannedFiles = new ConcurrentDictionary<string, bool>(
                Directory.EnumerateFiles(penumbraDir, "*.*", SearchOption.AllDirectories)
                                .Select(s => s.ToLowerInvariant())
                                .Where(f => f.Contains(@"\chara\"))
                                .Where(f =>
                                    (f.EndsWith(".tex", StringComparison.OrdinalIgnoreCase)
                                     || f.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)
                                     || f.EndsWith(".mtrl", StringComparison.OrdinalIgnoreCase)))
                                .Concat(Directory.EnumerateFiles(_pluginConfiguration.CacheFolder, "*.*", SearchOption.AllDirectories)
                                    .Where(f => new FileInfo(f).Name.Length == 40)
                                    .Select(s => s.ToLowerInvariant()))
                                .Select(p => new KeyValuePair<string, bool>(p, false)).ToList());
            List<FileCache> fileCaches;
            await using (var db = new FileCacheContext())
                fileCaches = db.FileCaches.ToList();

            TotalFiles = scannedFiles.Count;

            var fileCachesToDelete = new ConcurrentBag<FileCache>();
            var fileCachesToAdd = new ConcurrentBag<FileCache>();

            Logger.Debug("Database contains " + fileCaches.Count + " files, local system contains " + TotalFiles);
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
                    var newCache = Create(cache.Filepath, ct);
                    if (newCache != null)
                    {
                        fileCachesToAdd.Add(newCache);
                        fileCachesToDelete.Add(cache);
                    }
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
                    var newCache = Create(file.Key, ct);
                    if (newCache != null)
                    {
                        fileCachesToAdd.Add(newCache);
                    }

                    var files = CurrentFileProgress;
                    Interlocked.Increment(ref files);
                    CurrentFileProgress = files;
                });

            if (fileCachesToAdd.Any() || fileCachesToDelete.Any())
            {
                await using FileCacheContext db = new();

                Logger.Debug("Found " + fileCachesToAdd.Count + " additions and " + fileCachesToDelete.Count + " deletions");
                try
                {
                    foreach (var deletion in fileCachesToDelete)
                    {
                        var entries = db.FileCaches.Where(f =>
                            f.Hash == deletion.Hash && f.Filepath.ToLower() == deletion.Filepath.ToLower());
                        if (await entries.AnyAsync(ct))
                        {
                            Logger.Verbose("Removing file from DB: " + deletion.Filepath);
                            db.FileCaches.RemoveRange(entries);
                        }
                    }
                    await db.SaveChangesAsync(ct);
                    foreach (var entry in fileCachesToAdd)
                    {
                        try
                        {
                            db.FileCaches.Add(entry);
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, ex.Message);
                }
            }

            Logger.Debug("Scan complete");
            TotalFiles = 0;
            CurrentFileProgress = 0;

            if (!_pluginConfiguration.InitialScanComplete)
            {
                _pluginConfiguration.InitialScanComplete = true;
                _pluginConfiguration.Save();
            }
        }

        private void StartWatchersAndScan()
        {
            if (!_ipcManager.Initialized || !_pluginConfiguration.HasValidSetup()) return;
            Logger.Verbose("Penumbra is active, configuration is valid, starting watchers and scan");
            StartWatchers();
            StartInitialScan();
        }

        private void StopWatchersAndScan()
        {
            _cacheDirWatcher?.Dispose();
        }
    }
}
