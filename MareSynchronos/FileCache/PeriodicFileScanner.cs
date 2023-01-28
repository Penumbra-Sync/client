using System.Collections.Concurrent;
using MareSynchronos.Managers;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.FileCache;

public class PeriodicFileScanner : IDisposable
{
    private readonly IpcManager _ipcManager;
    private readonly ConfigurationService _configService;
    private readonly FileCacheManager _fileDbManager;
    private readonly ApiController _apiController;
    private readonly DalamudUtil _dalamudUtil;
    private CancellationTokenSource? _scanCancellationTokenSource;
    private Task? _fileScannerTask = null;
    public ConcurrentDictionary<string, int> haltScanLocks = new(StringComparer.Ordinal);
    public PeriodicFileScanner(IpcManager ipcManager, ConfigurationService configService, FileCacheManager fileDbManager, ApiController apiController, DalamudUtil dalamudUtil)
    {
        Logger.Verbose("Creating " + nameof(PeriodicFileScanner));

        _ipcManager = ipcManager;
        _configService = configService;
        _fileDbManager = fileDbManager;
        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _ipcManager.PenumbraInitialized += StartScan;
        _apiController.DownloadStarted += ApiHaltScan;
        _apiController.DownloadFinished += ApiResumeScan;
        _dalamudUtil.ZoneSwitchStart += ZoneSwitchHaltScan;
        _dalamudUtil.ZoneSwitchEnd += ZoneSwitchResumeScan;
    }

    private void ApiHaltScan()
    {
        HaltScan("Download");
    }

    private void ApiResumeScan()
    {
        ResumeScan("Download");
    }

    private void ZoneSwitchHaltScan()
    {
        HaltScan("Zoning/Gpose");
    }

    private void ZoneSwitchResumeScan()
    {
        ResumeScan("Zoning/Gpose");
    }

    public void ResetLocks()
    {
        haltScanLocks.Clear();
    }

    public void ResumeScan(string source)
    {
        if (!haltScanLocks.ContainsKey(source)) haltScanLocks[source] = 0;

        haltScanLocks[source]--;
        if (haltScanLocks[source] < 0) haltScanLocks[source] = 0;

        if (fileScanWasRunning && haltScanLocks.All(f => f.Value == 0))
        {
            fileScanWasRunning = false;
            InvokeScan(true);
        }
    }

    public void HaltScan(string source)
    {
        if (!haltScanLocks.ContainsKey(source)) haltScanLocks[source] = 0;
        haltScanLocks[source]++;

        if (IsScanRunning && haltScanLocks.Any(f => f.Value > 0))
        {
            _scanCancellationTokenSource?.Cancel();
            fileScanWasRunning = true;
        }
    }

    private bool fileScanWasRunning = false;
    private long currentFileProgress = 0;
    public long CurrentFileProgress => currentFileProgress;

    public long FileCacheSize { get; set; }

    public bool IsScanRunning => CurrentFileProgress > 0 || TotalFiles > 0;

    public long TotalFiles { get; private set; }

    public string TimeUntilNextScan => _timeUntilNextScan.ToString(@"mm\:ss");
    private TimeSpan _timeUntilNextScan = TimeSpan.Zero;
    private int timeBetweenScans => _configService.Current.TimeSpanBetweenScansInSeconds;

    public void Dispose()
    {
        Logger.Verbose("Disposing " + nameof(PeriodicFileScanner));

        _ipcManager.PenumbraInitialized -= StartScan;
        _apiController.DownloadStarted -= ApiHaltScan;
        _apiController.DownloadFinished -= ApiResumeScan;
        _dalamudUtil.ZoneSwitchStart -= ZoneSwitchHaltScan;
        _dalamudUtil.ZoneSwitchEnd -= ZoneSwitchResumeScan;
        _scanCancellationTokenSource?.Cancel();
    }

    public void InvokeScan(bool forced = false)
    {
        bool isForced = forced;
        TotalFiles = 0;
        currentFileProgress = 0;
        _scanCancellationTokenSource?.Cancel();
        _scanCancellationTokenSource = new CancellationTokenSource();
        var token = _scanCancellationTokenSource.Token;
        _fileScannerTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                while (haltScanLocks.Any(f => f.Value > 0) || !_ipcManager.CheckPenumbraApi())
                {
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                }

                isForced |= RecalculateFileCacheSize();
                if (!_configService.Current.FileScanPaused || isForced)
                {
                    isForced = false;
                    TotalFiles = 0;
                    currentFileProgress = 0;
                    PeriodicFileScan(token);
                    TotalFiles = 0;
                    currentFileProgress = 0;
                }
                _timeUntilNextScan = TimeSpan.FromSeconds(timeBetweenScans);
                while (_timeUntilNextScan.TotalSeconds >= 0)
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

        if (FileCacheSize < (long)_configService.Current.MaxLocalCacheInGiB * 1024 * 1024 * 1024) return false;

        var allFiles = Directory.EnumerateFiles(_configService.Current.CacheFolder)
            .Select(f => new FileInfo(f)).OrderBy(f => f.LastAccessTime).ToList();
        while (FileCacheSize > (long)_configService.Current.MaxLocalCacheInGiB * 1024 * 1024 * 1024)
        {
            var oldestFile = allFiles.First();
            FileCacheSize -= oldestFile.Length;
            File.Delete(oldestFile.FullName);
            allFiles.Remove(oldestFile);
        }

        return true;
    }

    private void PeriodicFileScan(CancellationToken ct)
    {
        TotalFiles = 1;
        var penumbraDir = _ipcManager.PenumbraModDirectory();
        bool penDirExists = true;
        bool cacheDirExists = true;
        if (string.IsNullOrEmpty(penumbraDir) || !Directory.Exists(penumbraDir))
        {
            penDirExists = false;
            Logger.Warn("Penumbra directory is not set or does not exist.");
        }
        if (string.IsNullOrEmpty(_configService.Current.CacheFolder) || !Directory.Exists(_configService.Current.CacheFolder))
        {
            cacheDirExists = false;
            Logger.Warn("Mare Cache directory is not set or does not exist.");
        }
        if (!penDirExists || !cacheDirExists)
        {
            return;
        }

        Logger.Debug("Getting files from " + penumbraDir + " and " + _configService.Current.CacheFolder);
        string[] ext = { ".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".scd", ".skp", ".shpk" };

        var scannedFiles = new ConcurrentDictionary<string, bool>(Directory.EnumerateFiles(penumbraDir, "*.*", SearchOption.AllDirectories)
                            .Select(s => s.ToLowerInvariant())
                            .Where(f => ext.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)) 
                                && !f.Contains(@"\bg\", StringComparison.OrdinalIgnoreCase) 
                                && !f.Contains(@"\bgcommon\", StringComparison.OrdinalIgnoreCase) 
                                && !f.Contains(@"\ui\", StringComparison.OrdinalIgnoreCase))
                            .Concat(Directory.EnumerateFiles(_configService.Current.CacheFolder, "*.*", SearchOption.TopDirectoryOnly)
                                .Where(f => new FileInfo(f).Name.Length == 40)
                                .Select(s => s.ToLowerInvariant()).ToList())
                            .Select(c => new KeyValuePair<string, bool>(c, false)), StringComparer.OrdinalIgnoreCase);

        TotalFiles = scannedFiles.Count;

        // scan files from database
        var cpuCount = (int)(Environment.ProcessorCount / 2.0f);
        Task[] dbTasks = Enumerable.Range(0, cpuCount).Select(c => Task.CompletedTask).ToArray();

        ConcurrentBag<FileCacheEntity> entitiesToRemove = new();
        ConcurrentBag<FileCacheEntity> entitiesToUpdate = new();
        try
        {
            foreach (var cache in _fileDbManager.GetAllFileCaches())
            {
                var idx = Task.WaitAny(dbTasks, ct);
                dbTasks[idx] = Task.Run(() =>
                {
                    try
                    {
                        var validatedCacheResult = _fileDbManager.ValidateFileCacheEntity(cache);
                        if (validatedCacheResult.Item1 != FileState.RequireDeletion)
                            scannedFiles[validatedCacheResult.Item2.ResolvedFilepath] = true;
                        if (validatedCacheResult.Item1 == FileState.RequireUpdate)
                        {
                            Logger.Verbose("To update: " + validatedCacheResult.Item2.ResolvedFilepath);
                            entitiesToUpdate.Add(validatedCacheResult.Item2);
                        }
                        else if (validatedCacheResult.Item1 == FileState.RequireDeletion)
                        {
                            Logger.Verbose("To delete: " + validatedCacheResult.Item2.ResolvedFilepath);
                            entitiesToRemove.Add(validatedCacheResult.Item2);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Failed validating " + cache.ResolvedFilepath);
                        Logger.Warn(ex.Message);
                        Logger.Warn(ex.StackTrace);
                    }

                    Interlocked.Increment(ref currentFileProgress);
                    Thread.Sleep(1);
                }, ct);

                if (ct.IsCancellationRequested) return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.Warn("Error during enumerating FileCaches: " + ex.Message);
        }

        Task.WaitAll(dbTasks);

        if (entitiesToUpdate.Any() || entitiesToRemove.Any())
        {
            foreach (var entity in entitiesToUpdate)
            {
                _fileDbManager.UpdateHash(entity);
            }

            foreach (var entity in entitiesToRemove)
            {
                _fileDbManager.RemoveHash(entity);
            }

            _fileDbManager.WriteOutFullCsv();
        }

        Logger.Verbose("Scanner validated existing db files");

        if (ct.IsCancellationRequested) return;

        // scan new files
        foreach (var c in scannedFiles.Where(c => c.Value == false))
        {
            var idx = Task.WaitAny(dbTasks, ct);
            dbTasks[idx] = Task.Run(() =>
            {
                try
                {
                    var entry = _fileDbManager.CreateFileEntry(c.Key);
                    if (entry == null) _ = _fileDbManager.CreateCacheEntry(c.Key);
                }
                catch (Exception ex)
                {
                    Logger.Warn("Failed adding " + c.Key);
                    Logger.Warn(ex.Message);
                    Logger.Warn(ex.StackTrace);
                }

                Interlocked.Increment(ref currentFileProgress);
                Thread.Sleep(1);
            }, ct);

            if (ct.IsCancellationRequested) return;
        }

        Task.WaitAll(dbTasks);

        Logger.Verbose("Scanner added new files to db");

        Logger.Debug("Scan complete");
        TotalFiles = 0;
        currentFileProgress = 0;
        entitiesToRemove.Clear();
        scannedFiles.Clear();
        dbTasks = Array.Empty<Task>();

        if (!_configService.Current.InitialScanComplete)
        {
            _configService.Current.InitialScanComplete = true;
            _configService.Save();
        }
    }

    public void StartScan()
    {
        if (!_ipcManager.Initialized || !_configService.Current.HasValidSetup()) return;
        Logger.Verbose("Penumbra is active, configuration is valid, scan");
        InvokeScan(true);
    }
}
