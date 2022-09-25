using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MareSynchronos.Managers;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.EntityFrameworkCore;

namespace MareSynchronos.FileCacheDB;

public class PeriodicFileScanner : IDisposable
{
    private readonly IpcManager _ipcManager;
    private readonly Configuration _pluginConfiguration;
    private readonly FileDbManager _fileDbManager;
    private readonly ApiController _apiController;
    private CancellationTokenSource? _scanCancellationTokenSource;
    private Task? _fileScannerTask = null;
    public PeriodicFileScanner(IpcManager ipcManager, Configuration pluginConfiguration, FileDbManager fileDbManager, ApiController apiController)
    {
        Logger.Verbose("Creating " + nameof(PeriodicFileScanner));

        _ipcManager = ipcManager;
        _pluginConfiguration = pluginConfiguration;
        _fileDbManager = fileDbManager;
        _apiController = apiController;
        _ipcManager.PenumbraInitialized += StartScan;
        if (!string.IsNullOrEmpty(_ipcManager.PenumbraModDirectory()))
        {
            StartScan();
        }
        _apiController.DownloadStarted += _apiController_DownloadStarted;
        _apiController.DownloadFinished += _apiController_DownloadFinished;
    }

    private void _apiController_DownloadFinished()
    {
        InvokeScan();
    }

    private void _apiController_DownloadStarted()
    {
        _scanCancellationTokenSource?.Cancel();
    }

    private long currentFileProgress = 0;
    public long CurrentFileProgress => currentFileProgress;

    public long FileCacheSize { get; set; }

    public bool IsScanRunning => CurrentFileProgress > 0 || TotalFiles > 0;

    public long TotalFiles { get; private set; }

    public string TimeUntilNextScan => _timeUntilNextScan.ToString(@"mm\:ss");
    private TimeSpan _timeUntilNextScan = TimeSpan.Zero;
    private int timeBetweenScans => _pluginConfiguration.TimeSpanBetweenScansInSeconds;

    public void Dispose()
    {
        Logger.Verbose("Disposing " + nameof(PeriodicFileScanner));

        _ipcManager.PenumbraInitialized -= StartScan;
        _apiController.DownloadStarted -= _apiController_DownloadStarted;
        _apiController.DownloadFinished -= _apiController_DownloadFinished;
        _scanCancellationTokenSource?.Cancel();
    }

    public void InvokeScan(bool forced = false)
    {
        bool isForced = forced;
        _scanCancellationTokenSource?.Cancel();
        _scanCancellationTokenSource = new CancellationTokenSource();
        var token = _scanCancellationTokenSource.Token;
        _fileScannerTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                isForced |= RecalculateFileCacheSize();
                if (!_pluginConfiguration.FileScanPaused || isForced)
                {
                    isForced = false;
                    await PeriodicFileScan(token);
                }
                _timeUntilNextScan = TimeSpan.FromSeconds(timeBetweenScans);
                while (_timeUntilNextScan.TotalSeconds >= 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                    _timeUntilNextScan -= TimeSpan.FromSeconds(1);
                }
            }
        });
    }

    internal void StartWatchers()
    {
        InvokeScan();
    }

    public bool RecalculateFileCacheSize()
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

        if (FileCacheSize < (long)_pluginConfiguration.MaxLocalCacheInGiB * 1024 * 1024 * 1024) return false;

        var allFiles = Directory.EnumerateFiles(_pluginConfiguration.CacheFolder)
            .Select(f => new FileInfo(f)).OrderBy(f => f.LastAccessTime).ToList();
        while (FileCacheSize > (long)_pluginConfiguration.MaxLocalCacheInGiB * 1024 * 1024 * 1024)
        {
            var oldestFile = allFiles.First();
            FileCacheSize -= oldestFile.Length;
            File.Delete(oldestFile.FullName);
            allFiles.Remove(oldestFile);
        }

        return true;
    }

    private async Task PeriodicFileScan(CancellationToken ct)
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
        if (string.IsNullOrEmpty(_pluginConfiguration.CacheFolder) || !Directory.Exists(_pluginConfiguration.CacheFolder))
        {
            cacheDirExists = false;
            Logger.Warn("Mare Cache directory is not set or does not exist.");
        }
        if (!penDirExists || !cacheDirExists)
        {
            return;
        }

        Logger.Debug("Getting files from " + penumbraDir + " and " + _pluginConfiguration.CacheFolder);
        string[] ext = { ".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".scd", ".skp" };
        var scannedFiles = new ConcurrentDictionary<string, bool>(
            Directory.EnumerateFiles(penumbraDir, "*.*", SearchOption.AllDirectories)
                            .Select(s => new FileInfo(s))
                            .Where(f => ext.Contains(f.Extension) && !f.FullName.Contains(@"\bg\") && !f.FullName.Contains(@"\bgcommon\") && !f.FullName.Contains(@"\ui\"))
                            .Select(f => f.FullName.ToLowerInvariant())
                            .Concat(Directory.EnumerateFiles(_pluginConfiguration.CacheFolder, "*.*", SearchOption.AllDirectories)
                                .Where(f => new FileInfo(f).Name.Length == 40)
                                .Select(s => s.ToLowerInvariant()))
                            .Select(p => new KeyValuePair<string, bool>(p, false)).ToList());
        List<FileCacheEntity> fileDbEntries;
        using (var db = new FileCacheContext())
        {
            fileDbEntries = await db.FileCaches.ToListAsync(cancellationToken: ct);
        }

        TotalFiles = scannedFiles.Count;

        Logger.Debug("Database contains " + fileDbEntries.Count + " files, local system contains " + TotalFiles);
        // scan files from database
        Parallel.ForEach(fileDbEntries.ToList(), new ParallelOptions()
        {
            MaxDegreeOfParallelism = _pluginConfiguration.MaxParallelScan,
            CancellationToken = ct,
        },
        dbEntry =>
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var file = _fileDbManager.ValidateFileCache(dbEntry);
                if (file != null && scannedFiles.ContainsKey(file.Filepath))
                {
                    scannedFiles[file.Filepath] = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex.Message);
                Logger.Warn(ex.StackTrace);
            }

            Interlocked.Increment(ref currentFileProgress);
        });

        Logger.Debug("Scanner validated existing db files");

        if (ct.IsCancellationRequested) return;

        // scan new files
        Parallel.ForEach(scannedFiles.Where(c => c.Value == false), new ParallelOptions()
        {
            MaxDegreeOfParallelism = _pluginConfiguration.MaxParallelScan,
            CancellationToken = ct
        },
        file =>
        {
            if (ct.IsCancellationRequested) return;

            var entry = _fileDbManager.CreateFileEntry(file.Key);
            if (entry == null) _ = _fileDbManager.CreateCacheEntry(file.Key);
            Interlocked.Increment(ref currentFileProgress);
        });

        Logger.Debug("Scanner added new files to db");

        Logger.Debug("Scan complete");
        TotalFiles = 0;
        currentFileProgress = 0;

        if (!_pluginConfiguration.InitialScanComplete)
        {
            _pluginConfiguration.InitialScanComplete = true;
            _pluginConfiguration.Save();
        }
    }

    private void StartScan()
    {
        if (!_ipcManager.Initialized || !_pluginConfiguration.HasValidSetup()) return;
        Logger.Verbose("Penumbra is active, configuration is valid, starting watchers and scan");
        InvokeScan(true);
    }
}
