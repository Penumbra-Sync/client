using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    public class FileCacheManager : IDisposable
    {
        private readonly FileCacheFactory _fileCacheFactory;
        private readonly IpcManager _ipcManager;
        private readonly Configuration _pluginConfiguration;
        private CancellationTokenSource? _scanCancellationTokenSource;
        private Task? _scanTask;
        private FileSystemWatcher? _penumbraDirWatcher;
        private FileSystemWatcher? _cacheDirWatcher;

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

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            var fi = new FileInfo(e.FullPath);
            using var db = new FileCacheContext();
            if (fi.Extension.ToLower() is not ".mdl" or ".tex" or ".mtrl")
            {
                // this is most likely a folder
                Logger.Debug("Folder added: " + e.FullPath);
                var newFiles = Directory.EnumerateFiles(e.FullPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".tex", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".mtrl", StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var file in newFiles)
                {
                    Logger.Debug("Adding " + file);
                    db.Add(_fileCacheFactory.Create(file));
                }
            }
            else
            {
                Logger.Debug("File created: " + e.FullPath);
                var createdFileCache = _fileCacheFactory.Create(fi.FullName);
                db.Add(createdFileCache);
            }

            db.SaveChanges();
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            var fi = new FileInfo(e.FullPath);
            using var db = new FileCacheContext();
            if (fi.Extension.ToLower() is not ".mdl" or ".tex" or ".mtrl")
            {
                // this is most likely a folder
                var filesToRemove = db.FileCaches.Where(f => f.Filepath.StartsWith(e.FullPath.ToLower())).ToList();
                Logger.Debug($"Folder deleted: {e.FullPath}, removing {filesToRemove.Count} files");
                db.RemoveRange(filesToRemove);
            }
            else
            {
                Logger.Debug("File deleted: " + e.FullPath);
                var fileInDb = db.FileCaches.SingleOrDefault(f => f.Filepath == fi.FullName.ToLower());
                if (fileInDb == null) return;
                db.Remove(fileInDb);
            }
            db.SaveChanges();
        }

        private void OnModified(object sender, FileSystemEventArgs e)
        {
            Logger.Debug("OnModified: " + e.FullPath);
            var fi = new FileInfo(e.FullPath);
            if (fi.Extension.ToLower() is not ".mdl" or ".tex" or ".mtrl") return;
            Logger.Debug("File changed: " + e.FullPath);
            using var db = new FileCacheContext();
            var modifiedFile = _fileCacheFactory.Create(fi.FullName);
            var fileInDb = db.FileCaches.SingleOrDefault(f => f.Filepath == fi.FullName.ToLower() || modifiedFile.Hash == f.Hash);
            if (fileInDb == null) return;
            db.Remove(fileInDb);
            db.Add(modifiedFile);
            db.SaveChanges();
        }

        public long CurrentFileProgress { get; private set; }
        public bool IsScanRunning => !_scanTask?.IsCompleted ?? false;
        public long TotalFiles { get; private set; }
        public string WatchedPenumbraDirectory => (!_penumbraDirWatcher?.EnableRaisingEvents ?? false) ? "Not watched" : _penumbraDirWatcher!.Path;
        public string WatchedCacheDirectory => (!_cacheDirWatcher?.EnableRaisingEvents ?? false) ? "Not watched" : _cacheDirWatcher!.Path;

        public void Dispose()
        {
            Logger.Debug("Disposing " + nameof(FileCacheManager));

            _cacheDirWatcher?.Dispose();
            _penumbraDirWatcher?.Dispose();
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
            Logger.Debug("Getting files from " + penumbraDir + " and " + _pluginConfiguration.CacheFolder);
            var scannedFiles = new ConcurrentDictionary<string, bool>(
                Directory.EnumerateFiles(penumbraDir, "*.*", SearchOption.AllDirectories)
                                .Select(s => s.ToLowerInvariant())
                                .Where(f => f.Contains(@"\chara\"))
                                .Concat(Directory.EnumerateFiles(_pluginConfiguration.CacheFolder, "*.*", SearchOption.AllDirectories)
                                    .Select(s => s.ToLowerInvariant()))
                                .Where(f => (f.EndsWith(".tex", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mtrl", StringComparison.OrdinalIgnoreCase)))
                                .Select(p => new KeyValuePair<string, bool>(p, false)));
            await using FileCacheContext db = new();
            List<FileCache> fileCaches = db.FileCaches.ToList();

            TotalFiles = scannedFiles.Count;

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
                    fileCachesToAdd.Add(_fileCacheFactory.Create(cache.Filepath));
                    fileCachesToDelete.Add(cache);
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

            if (fileCachesToAdd.Any() || fileCachesToDelete.Any())
            {
                Logger.Debug("Found " + fileCachesToAdd.Count + " additions and " + fileCachesToDelete.Count + " deletions");
                try
                {
                    db.FileCaches.RemoveRange(fileCachesToDelete);
                    db.FileCaches.AddRange(fileCachesToAdd);
                    db.SaveChanges();
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

            StartWatchers();
        }

        public void StartWatchers()
        {
            Logger.Debug("Starting File System Watchers");
            _penumbraDirWatcher?.Dispose();
            _cacheDirWatcher?.Dispose();

            _penumbraDirWatcher = new FileSystemWatcher(_ipcManager.PenumbraModDirectory()!)
            {
                EnableRaisingEvents = true,
                Filter = "*.*",
                IncludeSubdirectories = true,
            };
            _penumbraDirWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size;
            _penumbraDirWatcher.Created += OnCreated;
            _penumbraDirWatcher.Deleted += OnDeleted;
            _penumbraDirWatcher.Changed += OnModified;
            _penumbraDirWatcher.Error += (sender, args) => PluginLog.Error(args.GetException(), "Error in Penumbra Dir Watcher");

            _cacheDirWatcher = new FileSystemWatcher(_pluginConfiguration.CacheFolder)
            {
                EnableRaisingEvents = true,
                Filter = "*.*",
                IncludeSubdirectories = true,
            };
            _cacheDirWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size;
            _cacheDirWatcher.Created += OnCreated;
            _cacheDirWatcher.Deleted += OnDeleted;
            _cacheDirWatcher.Changed += OnModified;
            _cacheDirWatcher.Error +=
                (sender, args) => PluginLog.Error(args.GetException(), "Error in Cache Dir Watcher");
        }
    }
}
