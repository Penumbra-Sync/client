using MareSynchronos.Interop;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.FileCache;

public sealed class PeriodicFileScanner : DisposableMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly FileCacheManager _fileDbManager;
    private readonly IpcManager _ipcManager;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly DalamudUtilService _dalamudUtil;
    private long _currentFileProgress = 0;
    private bool _fileScanWasRunning = false;
    private CancellationTokenSource _scanCancellationTokenSource = new();
    private TimeSpan _timeUntilNextScan = TimeSpan.Zero;

    public PeriodicFileScanner(ILogger<PeriodicFileScanner> logger, IpcManager ipcManager, MareConfigService configService,
        FileCacheManager fileDbManager, MareMediator mediator, PerformanceCollectorService performanceCollector, DalamudUtilService dalamudUtil) : base(logger, mediator)
    {
        _ipcManager = ipcManager;
        _configService = configService;
        _fileDbManager = fileDbManager;
        _performanceCollector = performanceCollector;
        _dalamudUtil = dalamudUtil;
        Mediator.Subscribe<PenumbraInitializedMessage>(this, (_) => StartScan());
        Mediator.Subscribe<HaltScanMessage>(this, (msg) => HaltScan(msg.Source));
        Mediator.Subscribe<ResumeScanMessage>(this, (msg) => ResumeScan(msg.Source));
        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => StartScan());
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) => StartScan());
    }

    public long CurrentFileProgress => _currentFileProgress;
    public long FileCacheSize { get; set; }
    public ConcurrentDictionary<string, int> HaltScanLocks { get; set; } = new(StringComparer.Ordinal);
    public bool IsScanRunning => CurrentFileProgress > 0 || TotalFiles > 0;

    public string TimeUntilNextScan => _timeUntilNextScan.ToString(@"mm\:ss");

    public long TotalFiles { get; private set; }

    private int TimeBetweenScans => _configService.Current.TimeSpanBetweenScansInSeconds;

    public void HaltScan(string source)
    {
        if (!HaltScanLocks.ContainsKey(source)) HaltScanLocks[source] = 0;
        HaltScanLocks[source]++;

        if (IsScanRunning && HaltScanLocks.Any(f => f.Value > 0))
        {
            _scanCancellationTokenSource?.Cancel();
            _fileScanWasRunning = true;
        }
    }

    public void InvokeScan(bool forced = false)
    {
        bool isForced = forced;
        bool isForcedFromExternal = forced;
        TotalFiles = 0;
        _currentFileProgress = 0;
        _scanCancellationTokenSource?.Cancel();
        _scanCancellationTokenSource = new CancellationTokenSource();
        var token = _scanCancellationTokenSource.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                while (HaltScanLocks.Any(f => f.Value > 0) || !_ipcManager.CheckPenumbraApi() || _dalamudUtil.IsOnFrameworkThread)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                }

                isForced |= RecalculateFileCacheSize();
                if (!_configService.Current.FileScanPaused || isForced)
                {
                    isForced = false;
                    TotalFiles = 0;
                    _currentFileProgress = 0;
                    while (_dalamudUtil.IsOnFrameworkThread)
                    {
                        Logger.LogWarning("Scanner is on framework, waiting for leaving thread before continuing");
                        await Task.Delay(250, token).ConfigureAwait(false);
                    }
                    await _performanceCollector.LogPerformance(this, "PeriodicFileScan",
                        async () => await PeriodicFileScan(isForcedFromExternal, token).ConfigureAwait(false)).ConfigureAwait(false);
                    if (isForcedFromExternal) isForcedFromExternal = false;
                    TotalFiles = 0;
                    _currentFileProgress = 0;
                }
                _timeUntilNextScan = TimeSpan.FromSeconds(TimeBetweenScans);
                while (_timeUntilNextScan.TotalSeconds >= 0 || _dalamudUtil.IsOnFrameworkThread)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                    _timeUntilNextScan -= TimeSpan.FromSeconds(1);
                }
            }
        }, token);
    }

    public bool RecalculateFileCacheSize()
    {
        FileCacheSize = Directory.EnumerateFiles(_configService.Current.CacheFolder).Sum(f =>
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

        var maxCacheInBytes = (long)(_configService.Current.MaxLocalCacheInGiB * 1024d * 1024d * 1024d);

        if (FileCacheSize < maxCacheInBytes) return false;

        var allFiles = Directory.EnumerateFiles(_configService.Current.CacheFolder)
            .Select(f => new FileInfo(f)).OrderBy(f => f.LastAccessTime).ToList();
        var maxCacheBuffer = maxCacheInBytes * 0.05d;
        while (FileCacheSize > maxCacheInBytes - (long)maxCacheBuffer)
        {
            var oldestFile = allFiles[0];
            FileCacheSize -= oldestFile.Length;
            File.Delete(oldestFile.FullName);
            allFiles.Remove(oldestFile);
        }

        return true;
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

        if (_fileScanWasRunning && HaltScanLocks.All(f => f.Value == 0))
        {
            _fileScanWasRunning = false;
            InvokeScan(forced: true);
        }
    }

    public void StartScan()
    {
        if (!_ipcManager.Initialized || !_configService.Current.HasValidSetup()) return;
        Logger.LogTrace("Penumbra is active, configuration is valid, scan");
        InvokeScan(forced: true);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _scanCancellationTokenSource?.Cancel();
    }

    private async Task PeriodicFileScan(bool noWaiting, CancellationToken ct)
    {
        TotalFiles = 1;
        var penumbraDir = _ipcManager.PenumbraModDirectory;
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
        string[] ext = { ".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".scd", ".skp", ".shpk" };

        var scannedFilesTask = Task.Run(() => Directory.EnumerateFiles(penumbraDir!, "*.*", SearchOption.AllDirectories)
                            .Select(s => s.ToLowerInvariant())
                            .Where(f => ext.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                                && !f.Contains(@"\bg\", StringComparison.OrdinalIgnoreCase)
                                && !f.Contains(@"\bgcommon\", StringComparison.OrdinalIgnoreCase)
                                && !f.Contains(@"\ui\", StringComparison.OrdinalIgnoreCase)));

        var scannedCacheTask = Task.Run(() => Directory.EnumerateFiles(_configService.Current.CacheFolder, "*.*", SearchOption.TopDirectoryOnly)
                                .Where(f =>
                                {
                                    var val = f.Split('\\').Last();
                                    return val.Length == 40 || (val.Split('.').FirstOrDefault()?.Length ?? 0) == 40;
                                })
                                .Select(s => s.ToLowerInvariant()));


        var scannedFiles = (await scannedFilesTask.ConfigureAwait(true))
            .Concat(await scannedCacheTask.ConfigureAwait(true))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t, t => false, StringComparer.OrdinalIgnoreCase);
        TotalFiles = scannedFiles.Count;
        Thread.CurrentThread.Priority = previousThreadPriority;

        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);

        // scan files from database
        var cpuCount = Math.Clamp((int)(Environment.ProcessorCount / 2.0f), 2, 8);

        List<FileCacheEntity> entitiesToRemove = new();
        List<FileCacheEntity> entitiesToUpdate = new();
        object sync = new();
        try
        {
            Parallel.ForEach(_fileDbManager.GetAllFileCaches(), new ParallelOptions()
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = cpuCount,
            }, (cache) =>
            {
                try
                {
                    if (ct.IsCancellationRequested) return;

                    if (!_ipcManager.CheckPenumbraApi())
                    {
                        Logger.LogWarning("Penumbra not available");
                        return;
                    }

                    var validatedCacheResult = _fileDbManager.ValidateFileCacheEntity(cache);
                    if (validatedCacheResult.State != FileState.RequireDeletion)
                        scannedFiles[validatedCacheResult.FileCache.ResolvedFilepath] = true;
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
                    Logger.LogWarning(ex, "Failed validating {path}", cache.ResolvedFilepath);
                }

                Interlocked.Increment(ref _currentFileProgress);
                if (!noWaiting) Thread.Sleep(5);
            });

        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during enumerating FileCaches");
        }

        if (ct.IsCancellationRequested) return;


        if (!_ipcManager.CheckPenumbraApi())
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
                _fileDbManager.RemoveHashedFile(entity);
            }

            _fileDbManager.WriteOutFullCsv();
        }

        Logger.LogTrace("Scanner validated existing db files");

        if (!_ipcManager.CheckPenumbraApi())
        {
            Logger.LogWarning("Penumbra not available");
            return;
        }

        if (ct.IsCancellationRequested) return;

        // scan new files
        if (scannedFiles.Any(c => !c.Value))
        {
            Parallel.ForEach(scannedFiles.Where(c => !c.Value).Select(c => c.Key),
                new ParallelOptions()
                {
                    MaxDegreeOfParallelism = cpuCount,
                    CancellationToken = ct
                }, (cachePath) =>
                {
                    if (ct.IsCancellationRequested) return;

                    if (!_ipcManager.CheckPenumbraApi())
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
                    if (!noWaiting) Thread.Sleep(5);
                });

            Logger.LogTrace("Scanner added {notScanned} new files to db", scannedFiles.Count(c => !c.Value));
        }

        Logger.LogDebug("Scan complete");
        TotalFiles = 0;
        _currentFileProgress = 0;
        entitiesToRemove.Clear();
        scannedFiles.Clear();

        if (!_configService.Current.InitialScanComplete)
        {
            _configService.Current.InitialScanComplete = true;
            _configService.Save();
        }
    }
}