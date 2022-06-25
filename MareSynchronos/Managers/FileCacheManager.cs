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

namespace MareSynchronos.Managers
{
    public class FileCacheManager : IDisposable
    {
        private readonly IpcManager _ipcManager;
        private readonly Configuration _pluginConfiguration;
        private FileSystemWatcher? _cacheDirWatcher;
        private FileSystemWatcher? _penumbraDirWatcher;
        private CancellationTokenSource? _scanCancellationTokenSource;
        private Task? _scanTask;
        public FileCacheManager(IpcManager ipcManager, Configuration pluginConfiguration)
        {
            Logger.Debug("Creating " + nameof(FileCacheManager));

            _ipcManager = ipcManager;
            _pluginConfiguration = pluginConfiguration;

            if (_ipcManager.CheckPenumbraApi()
               && _pluginConfiguration.AcceptedAgreement
               && !string.IsNullOrEmpty(_pluginConfiguration.CacheFolder)
               && _pluginConfiguration.ClientSecret.ContainsKey(_pluginConfiguration.ApiUri)
               && !string.IsNullOrEmpty(_ipcManager.PenumbraModDirectory()))
            {
                StartWatchers();
                StartInitialScan();
            }
        }

        public long CurrentFileProgress { get; private set; }
        public long FileCacheSize { get; set; }
        public bool IsScanRunning => !_scanTask?.IsCompleted ?? false;

        public long TotalFiles { get; private set; }

        public string WatchedCacheDirectory => (_cacheDirWatcher?.EnableRaisingEvents ?? false) ? _cacheDirWatcher!.Path : "Not watched";

        public string WatchedPenumbraDirectory => (_penumbraDirWatcher?.EnableRaisingEvents ?? false) ? _penumbraDirWatcher!.Path : "Not watched";

        public FileCache Create(string file)
        {
            FileInfo fileInfo = new(file);
            while (IsFileLocked(fileInfo))
            {
                Thread.Sleep(100);
                Logger.Debug("Waiting for file release " + fileInfo.FullName);
            }
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

        public void StartWatchers()
        {
            Logger.Debug("Starting File System Watchers");
            _penumbraDirWatcher?.Dispose();
            _cacheDirWatcher?.Dispose();

            _penumbraDirWatcher = new FileSystemWatcher(_ipcManager.PenumbraModDirectory()!)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 65536
            };
            _penumbraDirWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size;
            //_penumbraDirWatcher.Created += OnCreated;
            _penumbraDirWatcher.Deleted += OnDeleted;
            _penumbraDirWatcher.Changed += OnModified;
            _penumbraDirWatcher.Filters.Add("*.mtrl");
            _penumbraDirWatcher.Filters.Add("*.mdl");
            _penumbraDirWatcher.Filters.Add("*.tex");
            _penumbraDirWatcher.Error += (sender, args) => PluginLog.Error(args.GetException(), "Error in Penumbra Dir Watcher");
            _penumbraDirWatcher.EnableRaisingEvents = true;

            _cacheDirWatcher = new FileSystemWatcher(_pluginConfiguration.CacheFolder)
            {
                EnableRaisingEvents = true,
                IncludeSubdirectories = true,
                InternalBufferSize = 65536
            };
            _cacheDirWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size;
            //_cacheDirWatcher.Created += OnCreated;
            _cacheDirWatcher.Deleted += OnDeleted;
            _cacheDirWatcher.Changed += OnModified;
            _cacheDirWatcher.Filters.Add("*.mtrl");
            _cacheDirWatcher.Filters.Add("*.mdl");
            _cacheDirWatcher.Filters.Add("*.tex");
            _cacheDirWatcher.Error +=
                (sender, args) => PluginLog.Error(args.GetException(), "Error in Cache Dir Watcher");
            _cacheDirWatcher.EnableRaisingEvents = true;

            Task.Run(RecalculateFileCacheSize);
        }

        private bool IsFileLocked(FileInfo file)
        {
            try
            {
                using var fs = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch
            {
                return true;
            }

            return false;
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            var fi = new FileInfo(e.FullPath);
            using var db = new FileCacheContext();
            var ext = fi.Extension.ToLower();
            if (ext is ".mdl" or ".tex" or ".mtrl")
            {
                Logger.Debug("File created: " + e.FullPath);
                try
                {
                    var createdFileCache = Create(fi.FullName.ToLower());
                    db.Add(createdFileCache);
                }
                catch (FileLoadException)
                {
                    Logger.Debug("File was still being written to.");
                }
            }
            else
            {
                if (Directory.Exists(e.FullPath))
                {
                    Logger.Debug("Folder added: " + e.FullPath);
                    var newFiles = Directory.EnumerateFiles(e.FullPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".tex", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".mtrl", StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var file in newFiles)
                    {
                        Logger.Debug("Adding " + file);
                        db.Add(Create(file));
                    }
                }
            }

            db.SaveChanges();

            if (e.FullPath.Contains(_pluginConfiguration.CacheFolder, StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(RecalculateFileCacheSize);
            }
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            var fi = new FileInfo(e.FullPath);
            using var db = new FileCacheContext();
            var ext = fi.Extension.ToLower();
            if (ext is ".mdl" or ".tex" or ".mtrl")
            {
                Logger.Debug("File deleted: " + e.FullPath);
                var fileInDb = db.FileCaches.SingleOrDefault(f => f.Filepath == fi.FullName.ToLower());
                if (fileInDb == null) return;
                db.Remove(fileInDb);

            }
            else
            {
                if (fi.Extension == string.Empty)
                {
                    // this is most likely a folder
                    var filesToRemove = db.FileCaches.Where(f => f.Filepath.StartsWith(e.FullPath.ToLower())).ToList();
                    Logger.Debug($"Folder deleted: {e.FullPath}, removing {filesToRemove.Count} files");
                    db.RemoveRange(filesToRemove);
                }
            }

            db.SaveChanges();

            if (e.FullPath.Contains(_pluginConfiguration.CacheFolder, StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(RecalculateFileCacheSize);
            }
        }

        private void OnModified(object sender, FileSystemEventArgs e)
        {
            var fi = new FileInfo(e.FullPath);
            Logger.Debug("File changed: " + e.FullPath);
            using var db = new FileCacheContext();
            var modifiedFile = Create(fi.FullName);
            var fileInDb = db.FileCaches.SingleOrDefault(f => f.Filepath == fi.FullName.ToLower());
            if (fileInDb != null)
                db.Remove(fileInDb);
            else
            {
                var files = db.FileCaches.Where(f => f.Hash == modifiedFile.Hash);
                foreach (var file in files)
                {
                    if (!File.Exists(file.Filepath)) db.Remove(file.Filepath);
                }
            }
            db.Add(modifiedFile);
            db.SaveChanges();

            if (e.FullPath.Contains(_pluginConfiguration.CacheFolder, StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(RecalculateFileCacheSize);
            }
        }
        private void RecalculateFileCacheSize()
        {
            FileCacheSize = 0;
            foreach (var file in Directory.EnumerateFiles(_pluginConfiguration.CacheFolder))
            {
                FileCacheSize += new FileInfo(file).Length;
            }
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
                    fileCachesToAdd.Add(Create(cache.Filepath));
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
                fileCachesToAdd.Add(Create(file.Key));

                var files = CurrentFileProgress;
                Interlocked.Increment(ref files);
                CurrentFileProgress = files;
            });

            if (fileCachesToAdd.Any() || fileCachesToDelete.Any())
            {
                Logger.Debug("Found " + fileCachesToAdd.Count + " additions and " + fileCachesToDelete.Count + " deletions");
                try
                {
                    foreach (var deletion in fileCachesToDelete)
                    {
                        var entry = db.FileCaches.SingleOrDefault(f =>
                            f.Hash == deletion.Hash && f.Filepath == deletion.Filepath);
                        if (entry != null)
                            db.FileCaches.Remove(entry);
                    }
                    db.FileCaches.AddRange(fileCachesToAdd);
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
    }
}
