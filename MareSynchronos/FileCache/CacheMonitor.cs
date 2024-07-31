using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace MareSynchronos.FileCache;

public sealed class CacheMonitor : DisposableMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileCompactor _fileCompactor;
    private readonly FileCacheManager _fileDbManager;
    private readonly IpcManager _ipcManager;
    private readonly PerformanceCollectorService _performanceCollector;
    private long _currentFileProgress = 0;
    private CancellationTokenSource _scanCancellationTokenSource = new();
    private readonly CancellationTokenSource _periodicCalculationTokenSource = new();
    public static readonly IImmutableList<string> AllowedFileExtensions = [".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".pbd", ".scd", ".skp", ".shpk"];

    public CacheMonitor(ILogger<CacheMonitor> logger, IpcManager ipcManager, MareConfigService configService,
        FileCacheManager fileDbManager, MareMediator mediator, PerformanceCollectorService performanceCollector, DalamudUtilService dalamudUtil,
        FileCompactor fileCompactor) : base(logger, mediator)
    {
        _ipcManager = ipcManager;
        _configService = configService;
        _fileDbManager = fileDbManager;
        _performanceCollector = performanceCollector;
        _dalamudUtil = dalamudUtil;
        _fileCompactor = fileCompactor;
        Mediator.Subscribe<PenumbraInitializedMessage>(this, (_) =>
        {
            StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
            StartMareWatcher(configService.Current.CacheFolder);
            InvokeScan();
        });
        Mediator.Subscribe<HaltScanMessage>(this, (msg) => HaltScan(msg.Source));
        Mediator.Subscribe<ResumeScanMessage>(this, (msg) => ResumeScan(msg.Source));
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) =>
        {
            StartMareWatcher(configService.Current.CacheFolder);
            StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
            InvokeScan();
        });
        Mediator.Subscribe<PenumbraDirectoryChangedMessage>(this, (msg) =>
        {
            StartPenumbraWatcher(msg.ModDirectory);
            InvokeScan();
        });
        if (_ipcManager.Penumbra.APIAvailable && !string.IsNullOrEmpty(_ipcManager.Penumbra.ModDirectory))
        {
            StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
        }
        if (configService.Current.HasValidSetup())
        {
            StartMareWatcher(configService.Current.CacheFolder);
            InvokeScan();
        }

        var token = _periodicCalculationTokenSource.Token;
        _ = Task.Run(async () =>
        {
            Logger.LogInformation("Starting Periodic Storage Directory Calculation Task");
            var token = _periodicCalculationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    while (_dalamudUtil.IsOnFrameworkThread && !token.IsCancellationRequested)
                    {
                        await Task.Delay(1).ConfigureAwait(false);
                    }

                    RecalculateFileCacheSize(token);
                }
                catch
                {
                    // ignore
                }
                await Task.Delay(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
            }
        }, token);
    }

    public long CurrentFileProgress => _currentFileProgress;
    public long FileCacheSize { get; set; }
    public long FileCacheDriveFree { get; set; }
    public ConcurrentDictionary<string, int> HaltScanLocks { get; set; } = new(StringComparer.Ordinal);
    public bool IsScanRunning => CurrentFileProgress > 0 || TotalFiles > 0;
    public long TotalFiles { get; private set; }
    public long TotalFilesStorage { get; private set; }

    public void HaltScan(string source)
    {
        if (!HaltScanLocks.ContainsKey(source)) HaltScanLocks[source] = 0;
        HaltScanLocks[source]++;
    }

    record WatcherChange(WatcherChangeTypes ChangeType, string? OldPath = null);
    private readonly Dictionary<string, WatcherChange> _watcherChanges = new Dictionary<string, WatcherChange>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WatcherChange> _mareChanges = new Dictionary<string, WatcherChange>(StringComparer.OrdinalIgnoreCase);

    public void StopMonitoring()
    {
        Logger.LogInformation("Stopping monitoring of Penumbra and Mare storage folders");
        MareWatcher?.Dispose();
        PenumbraWatcher?.Dispose();
        MareWatcher = null;
        PenumbraWatcher = null;
    }

    public bool StorageisNTFS { get; private set; } = false;

    public void StartMareWatcher(string? marePath)
    {
        MareWatcher?.Dispose();
        if (string.IsNullOrEmpty(marePath) || !Directory.Exists(marePath))
        {
            MareWatcher = null;
            Logger.LogWarning("Mare file path is not set, cannot start the FSW for Mare.");
            return;
        }

        DriveInfo di = new(new DirectoryInfo(_configService.Current.CacheFolder).Root.FullName);
        StorageisNTFS = string.Equals("NTFS", di.DriveFormat, StringComparison.OrdinalIgnoreCase);
        Logger.LogInformation("Mare Storage is on NTFS drive: {isNtfs}", StorageisNTFS);

        Logger.LogDebug("Initializing Mare FSW on {path}", marePath);
        MareWatcher = new()
        {
            Path = marePath,
            InternalBufferSize = 8388608,
            NotifyFilter = NotifyFilters.CreationTime
                | NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.Size,
            Filter = "*.*",
            IncludeSubdirectories = false,
        };

        MareWatcher.Deleted += MareWatcher_FileChanged;
        MareWatcher.Created += MareWatcher_FileChanged;
        MareWatcher.EnableRaisingEvents = true;
    }

    private void MareWatcher_FileChanged(object sender, FileSystemEventArgs e)
    {
        Logger.LogTrace("Mare FSW: FileChanged: {change} => {path}", e.ChangeType, e.FullPath);

        if (!AllowedFileExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return;

        lock (_watcherChanges)
        {
            _mareChanges[e.FullPath] = new(e.ChangeType);
        }

        _ = MareWatcherExecution();
    }

    public void StartPenumbraWatcher(string? penumbraPath)
    {
        PenumbraWatcher?.Dispose();
        if (string.IsNullOrEmpty(penumbraPath))
        {
            PenumbraWatcher = null;
            Logger.LogWarning("Penumbra is not connected or the path is not set, cannot start FSW for Penumbra.");
            return;
        }

        Logger.LogDebug("Initializing Penumbra FSW on {path}", penumbraPath);
        PenumbraWatcher = new()
        {
            Path = penumbraPath,
            InternalBufferSize = 8388608,
            NotifyFilter = NotifyFilters.CreationTime
                | NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.Size,
            Filter = "*.*",
            IncludeSubdirectories = true
        };

        PenumbraWatcher.Deleted += Fs_Changed;
        PenumbraWatcher.Created += Fs_Changed;
        PenumbraWatcher.Changed += Fs_Changed;
        PenumbraWatcher.Renamed += Fs_Renamed;
        PenumbraWatcher.EnableRaisingEvents = true;
    }

    private void Fs_Changed(object sender, FileSystemEventArgs e)
    {
        if (Directory.Exists(e.FullPath)) return;
        if (!AllowedFileExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return;

        if (e.ChangeType is not (WatcherChangeTypes.Changed or WatcherChangeTypes.Deleted or WatcherChangeTypes.Created))
            return;

        lock (_watcherChanges)
        {
            _watcherChanges[e.FullPath] = new(e.ChangeType);
        }

        Logger.LogTrace("FSW {event}: {path}", e.ChangeType, e.FullPath);

        _ = PenumbraWatcherExecution();
    }

    private void Fs_Renamed(object sender, RenamedEventArgs e)
    {
        if (Directory.Exists(e.FullPath))
        {
            var directoryFiles = Directory.GetFiles(e.FullPath, "*.*", SearchOption.AllDirectories);
            lock (_watcherChanges)
            {
                foreach (var file in directoryFiles)
                {
                    if (!AllowedFileExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) continue;
                    var oldPath = file.Replace(e.FullPath, e.OldFullPath, StringComparison.OrdinalIgnoreCase);

                    _watcherChanges.Remove(oldPath);
                    _watcherChanges[file] = new(WatcherChangeTypes.Renamed, oldPath);
                    Logger.LogTrace("FSW Renamed: {path} -> {new}", oldPath, file);

                }
            }
        }
        else
        {
            if (!AllowedFileExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return;

            lock (_watcherChanges)
            {
                _watcherChanges.Remove(e.OldFullPath);
                _watcherChanges[e.FullPath] = new(WatcherChangeTypes.Renamed, e.OldFullPath);
            }

            Logger.LogTrace("FSW Renamed: {path} -> {new}", e.OldFullPath, e.FullPath);
        }

        _ = PenumbraWatcherExecution();
    }

    private CancellationTokenSource _penumbraFswCts = new();
    private CancellationTokenSource _mareFswCts = new();
    public FileSystemWatcher? PenumbraWatcher { get; private set; }
    public FileSystemWatcher? MareWatcher { get; private set; }

    private async Task MareWatcherExecution()
    {
        _mareFswCts = _mareFswCts.CancelRecreate();
        var token = _mareFswCts.Token;
        var delay = TimeSpan.FromSeconds(5);
        Dictionary<string, WatcherChange> changes;
        lock (_mareChanges)
            changes = _mareChanges.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        try
        {
            do
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            } while (HaltScanLocks.Any(f => f.Value > 0));
        }
        catch (TaskCanceledException)
        {
            return;
        }

        lock (_mareChanges)
        {
            foreach (var key in changes.Keys)
            {
                _mareChanges.Remove(key);
            }
        }

        HandleChanges(changes);
    }

    private void HandleChanges(Dictionary<string, WatcherChange> changes)
    {
        lock (_fileDbManager)
        {
            var deletedEntries = changes.Where(c => c.Value.ChangeType == WatcherChangeTypes.Deleted).Select(c => c.Key);
            var renamedEntries = changes.Where(c => c.Value.ChangeType == WatcherChangeTypes.Renamed);
            var remainingEntries = changes.Where(c => c.Value.ChangeType != WatcherChangeTypes.Deleted).Select(c => c.Key);

            foreach (var entry in deletedEntries)
            {
                Logger.LogDebug("FSW Change: Deletion - {val}", entry);
            }

            foreach (var entry in renamedEntries)
            {
                Logger.LogDebug("FSW Change: Renamed - {oldVal} => {val}", entry.Value.OldPath, entry.Key);
            }

            foreach (var entry in remainingEntries)
            {
                Logger.LogDebug("FSW Change: Creation or Change - {val}", entry);
            }

            var allChanges = deletedEntries
                .Concat(renamedEntries.Select(c => c.Value.OldPath!))
                .Concat(renamedEntries.Select(c => c.Key))
                .Concat(remainingEntries)
                .ToArray();

            _ = _fileDbManager.GetFileCachesByPaths(allChanges);

            _fileDbManager.WriteOutFullCsv();
        }
    }

    private async Task PenumbraWatcherExecution()
    {
        _penumbraFswCts = _penumbraFswCts.CancelRecreate();
        var token = _penumbraFswCts.Token;
        Dictionary<string, WatcherChange> changes;
        lock (_watcherChanges)
            changes = _watcherChanges.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        var delay = TimeSpan.FromSeconds(10);
        try
        {
            do
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            } while (HaltScanLocks.Any(f => f.Value > 0));
        }
        catch (TaskCanceledException)
        {
            return;
        }

        lock (_watcherChanges)
        {
            foreach (var key in changes.Keys)
            {
                _watcherChanges.Remove(key);
            }
        }

        HandleChanges(changes);
    }

    public void InvokeScan()
    {
        TotalFiles = 0;
        _currentFileProgress = 0;
        _scanCancellationTokenSource = _scanCancellationTokenSource?.CancelRecreate() ?? new CancellationTokenSource();
        var token = _scanCancellationTokenSource.Token;
        _ = Task.Run(async () =>
        {
            Logger.LogDebug("Starting Full File Scan");
            TotalFiles = 0;
            _currentFileProgress = 0;
            while (_dalamudUtil.IsOnFrameworkThread)
            {
                Logger.LogWarning("Scanner is on framework, waiting for leaving thread before continuing");
                await Task.Delay(250, token).ConfigureAwait(false);
            }

            Thread scanThread = new(() =>
            {
                try
                {
                    _performanceCollector.LogPerformance(this, $"FullFileScan", () => FullFileScan(token));
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error during Full File Scan");
                }
            })
            {
                Priority = ThreadPriority.Lowest,
                IsBackground = true
            };
            scanThread.Start();
            while (scanThread.IsAlive)
            {
                await Task.Delay(250).ConfigureAwait(false);
            }
            TotalFiles = 0;
            _currentFileProgress = 0;
        }, token);
    }

    public void RecalculateFileCacheSize(CancellationToken token)
    {
        if (string.IsNullOrEmpty(_configService.Current.CacheFolder) || !Directory.Exists(_configService.Current.CacheFolder))
        {
            FileCacheSize = 0;
            return;
        }

        FileCacheSize = -1;
        DriveInfo di = new(new DirectoryInfo(_configService.Current.CacheFolder).Root.FullName);
        try
        {
            FileCacheDriveFree = di.AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not determine drive size for Storage Folder {folder}", _configService.Current.CacheFolder);
        }

        var files = Directory.EnumerateFiles(_configService.Current.CacheFolder).Select(f => new FileInfo(f))
            .OrderBy(f => f.LastAccessTime).ToList();
        FileCacheSize = files
            .Sum(f =>
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    return _fileCompactor.GetFileSizeOnDisk(f, StorageisNTFS);
                }
                catch
                {
                    return 0;
                }
            });

        var maxCacheInBytes = (long)(_configService.Current.MaxLocalCacheInGiB * 1024d * 1024d * 1024d);

        if (FileCacheSize < maxCacheInBytes) return;

        var maxCacheBuffer = maxCacheInBytes * 0.05d;
        while (FileCacheSize > maxCacheInBytes - (long)maxCacheBuffer)
        {
            var oldestFile = files[0];
            FileCacheSize -= _fileCompactor.GetFileSizeOnDisk(oldestFile);
            File.Delete(oldestFile.FullName);
            files.Remove(oldestFile);
        }
    }

    public void ResetLocks()
    {
        HaltScanLocks.Clear();
    }

    public void ResumeScan(string source)
    {
        if (!HaltScanLocks.ContainsKey(source)) HaltScanLocks[source] = 0;

        HaltScanLocks[source]--;
        if (HaltScanLocks[source] < 0) HaltScanLocks[source] = 0;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _scanCancellationTokenSource?.Cancel();
        PenumbraWatcher?.Dispose();
        MareWatcher?.Dispose();
        _penumbraFswCts?.CancelDispose();
        _mareFswCts?.CancelDispose();
        _periodicCalculationTokenSource?.CancelDispose();
    }

    private void FullFileScan(CancellationToken ct)
    {
        TotalFiles = 1;
        var penumbraDir = _ipcManager.Penumbra.ModDirectory;
        bool penDirExists = true;
        bool cacheDirExists = true;
        if (string.IsNullOrEmpty(penumbraDir) || !Directory.Exists(penumbraDir))
        {
            penDirExists = false;
            Logger.LogWarning("Penumbra directory is not set or does not exist.");
        }
        if (string.IsNullOrEmpty(_configService.Current.CacheFolder) || !Directory.Exists(_configService.Current.CacheFolder))
        {
            cacheDirExists = false;
            Logger.LogWarning("Mare Cache directory is not set or does not exist.");
        }
        if (!penDirExists || !cacheDirExists)
        {
            return;
        }

        var previousThreadPriority = Thread.CurrentThread.Priority;
        Thread.CurrentThread.Priority = ThreadPriority.Lowest;
        Logger.LogDebug("Getting files from {penumbra} and {storage}", penumbraDir, _configService.Current.CacheFolder);

        Dictionary<string, string[]> penumbraFiles = new(StringComparer.Ordinal);
        foreach (var folder in Directory.EnumerateDirectories(penumbraDir!))
        {
            try
            {
                penumbraFiles[folder] =
                [
                    .. Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                                            .AsParallel()
                                            .Where(f => AllowedFileExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                                                && !f.Contains(@"\bg\", StringComparison.OrdinalIgnoreCase)
                                                && !f.Contains(@"\bgcommon\", StringComparison.OrdinalIgnoreCase)
                                                && !f.Contains(@"\ui\", StringComparison.OrdinalIgnoreCase)),
                ];
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Could not enumerate path {path}", folder);
            }
            Thread.Sleep(50);
            if (ct.IsCancellationRequested) return;
        }

        var allCacheFiles = Directory.GetFiles(_configService.Current.CacheFolder, "*.*", SearchOption.TopDirectoryOnly)
                                .AsParallel()
                                .Where(f =>
                                {
                                    var val = f.Split('\\')[^1];
                                    return val.Length == 40 || (val.Split('.').FirstOrDefault()?.Length ?? 0) == 40;
                                });

        if (ct.IsCancellationRequested) return;

        var allScannedFiles = (penumbraFiles.SelectMany(k => k.Value))
            .Concat(allCacheFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t.ToLowerInvariant(), t => false, StringComparer.OrdinalIgnoreCase);

        TotalFiles = allScannedFiles.Count;
        Thread.CurrentThread.Priority = previousThreadPriority;

        Thread.Sleep(TimeSpan.FromSeconds(2));

        if (ct.IsCancellationRequested) return;

        // scan files from database
        var threadCount = Math.Clamp((int)(Environment.ProcessorCount / 2.0f), 2, 8);

        List<FileCacheEntity> entitiesToRemove = [];
        List<FileCacheEntity> entitiesToUpdate = [];
        object sync = new();
        Thread[] workerThreads = new Thread[threadCount];

        ConcurrentQueue<FileCacheEntity> fileCaches = new(_fileDbManager.GetAllFileCaches());

        TotalFilesStorage = fileCaches.Count;

        for (int i = 0; i < threadCount; i++)
        {
            Logger.LogTrace("Creating Thread {i}", i);
            workerThreads[i] = new((tcounter) =>
            {
                var threadNr = (int)tcounter!;
                Logger.LogTrace("Spawning Worker Thread {i}", threadNr);
                while (!ct.IsCancellationRequested && fileCaches.TryDequeue(out var workload))
                {
                    try
                    {
                        if (ct.IsCancellationRequested) return;

                        if (!_ipcManager.Penumbra.APIAvailable)
                        {
                            Logger.LogWarning("Penumbra not available");
                            return;
                        }

                        var validatedCacheResult = _fileDbManager.ValidateFileCacheEntity(workload);
                        if (validatedCacheResult.State != FileState.RequireDeletion)
                        {
                            lock (sync) { allScannedFiles[validatedCacheResult.FileCache.ResolvedFilepath] = true; }
                        }
                        if (validatedCacheResult.State == FileState.RequireUpdate)
                        {
                            Logger.LogTrace("To update: {path}", validatedCacheResult.FileCache.ResolvedFilepath);
                            lock (sync) { entitiesToUpdate.Add(validatedCacheResult.FileCache); }
                        }
                        else if (validatedCacheResult.State == FileState.RequireDeletion)
                        {
                            Logger.LogTrace("To delete: {path}", validatedCacheResult.FileCache.ResolvedFilepath);
                            lock (sync) { entitiesToRemove.Add(validatedCacheResult.FileCache); }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed validating {path}", workload.ResolvedFilepath);
                    }
                    Interlocked.Increment(ref _currentFileProgress);
                }

                Logger.LogTrace("Ending Worker Thread {i}", threadNr);
            })
            {
                Priority = ThreadPriority.Lowest,
                IsBackground = true
            };
            workerThreads[i].Start(i);
        }

        while (!ct.IsCancellationRequested && workerThreads.Any(u => u.IsAlive))
        {
            Thread.Sleep(1000);
        }

        if (ct.IsCancellationRequested) return;

        Logger.LogTrace("Threads exited");

        if (!_ipcManager.Penumbra.APIAvailable)
        {
            Logger.LogWarning("Penumbra not available");
            return;
        }

        if (entitiesToUpdate.Any() || entitiesToRemove.Any())
        {
            foreach (var entity in entitiesToUpdate)
            {
                _fileDbManager.UpdateHashedFile(entity);
            }

            foreach (var entity in entitiesToRemove)
            {
                _fileDbManager.RemoveHashedFile(entity.Hash, entity.PrefixedFilePath);
            }

            _fileDbManager.WriteOutFullCsv();
        }

        Logger.LogTrace("Scanner validated existing db files");

        if (!_ipcManager.Penumbra.APIAvailable)
        {
            Logger.LogWarning("Penumbra not available");
            return;
        }

        if (ct.IsCancellationRequested) return;

        // scan new files
        if (allScannedFiles.Any(c => !c.Value))
        {
            Parallel.ForEach(allScannedFiles.Where(c => !c.Value).Select(c => c.Key),
                new ParallelOptions()
                {
                    MaxDegreeOfParallelism = threadCount,
                    CancellationToken = ct
                }, (cachePath) =>
                {
                    if (ct.IsCancellationRequested) return;

                    if (!_ipcManager.Penumbra.APIAvailable)
                    {
                        Logger.LogWarning("Penumbra not available");
                        return;
                    }

                    try
                    {
                        var entry = _fileDbManager.CreateFileEntry(cachePath);
                        if (entry == null) _ = _fileDbManager.CreateCacheEntry(cachePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed adding {file}", cachePath);
                    }

                    Interlocked.Increment(ref _currentFileProgress);
                });

            Logger.LogTrace("Scanner added {notScanned} new files to db", allScannedFiles.Count(c => !c.Value));
        }

        Logger.LogDebug("Scan complete");
        TotalFiles = 0;
        _currentFileProgress = 0;
        entitiesToRemove.Clear();
        allScannedFiles.Clear();

        if (!_configService.Current.InitialScanComplete)
        {
            _configService.Current.InitialScanComplete = true;
            _configService.Save();
            StartMareWatcher(_configService.Current.CacheFolder);
            StartPenumbraWatcher(penumbraDir);
        }
    }
}