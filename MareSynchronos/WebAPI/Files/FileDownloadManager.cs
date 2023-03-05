using Dalamud.Utility;
using LZ4;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Files;
using MareSynchronos.API.Routes;
using MareSynchronos.FileCache;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;

namespace MareSynchronos.WebAPI.Files;

public partial class FileDownloadManager : MediatorSubscriberBase
{
    private readonly FileTransferOrchestrator _orchestrator;
    private readonly FileCacheManager _fileDbManager;
    private readonly ConcurrentDictionary<Guid, bool> _downloadReady = new();
    private Dictionary<string, FileDownloadStatus> _downloadStatus;

    public List<DownloadFileTransfer> CurrentDownloads { get; private set; } = new();
    public List<FileTransfer> ForbiddenTransfers => _orchestrator.ForbiddenTransfers;
    public bool IsDownloading => !CurrentDownloads.Any();

    public FileDownloadManager(ILogger<FileDownloadManager> logger, MareMediator mediator,
        FileTransferOrchestrator orchestrator,
        FileCacheManager fileCacheManager,
        string downloadId) : base(logger, mediator)
    {
        _downloadStatus = new Dictionary<string, FileDownloadStatus>(StringComparer.Ordinal);
        _orchestrator = orchestrator;
        _fileDbManager = fileCacheManager;

        Mediator.Subscribe<DownloadReadyMessage>(this, (msg) =>
        {
            if (_downloadReady.ContainsKey(((DownloadReadyMessage)msg).RequestId))
            {
                _downloadReady[((DownloadReadyMessage)msg).RequestId] = true;
            }
        });
    }

    public async Task DownloadFiles(string aliasOrUid, List<FileReplacementData> fileReplacementDto, CancellationToken ct)
    {
        Mediator.Publish(new HaltScanMessage("Download"));
        try
        {
            await DownloadFilesInternal(aliasOrUid, fileReplacementDto, ct).ConfigureAwait(false);
        }
        catch
        {
            CancelDownload();
        }
        finally
        {
            Mediator.Publish(new DownloadFinishedMessage(aliasOrUid));
            Mediator.Publish(new ResumeScanMessage("Download"));
        }
    }

    public void CancelDownload()
    {
        CurrentDownloads.Clear();
        _downloadStatus.Clear();
    }

    private async Task DownloadFilesInternal(string aliasOrUid, List<FileReplacementData> fileReplacement, CancellationToken ct)
    {
        _logger.LogDebug("Downloading files for {id}", aliasOrUid);

        List<DownloadFileDto> downloadFileInfoFromService = new();
        downloadFileInfoFromService.AddRange(await FilesGetSizes(fileReplacement.Select(f => f.Hash).ToList(), ct).ConfigureAwait(false));

        _logger.LogDebug("Files with size 0 or less: {files}", string.Join(", ", downloadFileInfoFromService.Where(f => f.Size <= 0).Select(f => f.Hash)));

        CurrentDownloads = downloadFileInfoFromService.Distinct().Select(d => new DownloadFileTransfer(d))
            .Where(d => d.CanBeTransferred).ToList();

        foreach (var dto in downloadFileInfoFromService.Where(c => c.IsForbidden))
        {
            if (!_orchestrator.ForbiddenTransfers.Any(f => string.Equals(f.Hash, dto.Hash, StringComparison.Ordinal)))
            {
                _orchestrator.ForbiddenTransfers.Add(new DownloadFileTransfer(dto));
            }
        }

        var downloadGroups = CurrentDownloads.Where(f => f.CanBeTransferred).GroupBy(f => f.DownloadUri.Host + f.DownloadUri.Port, StringComparer.Ordinal);

        foreach (var downloadGroup in downloadGroups)
        {
            _downloadStatus[downloadGroup.Key] = new FileDownloadStatus()
            {
                DownloadStatus = DownloadStatus.Initializing,
                TotalBytes = downloadGroup.Sum(c => c.Total),
                TotalFiles = downloadGroup.Count(),
                TransferredBytes = 0,
                TransferredFiles = 0
            };
        }

        Mediator.Publish(new DownloadStartedMessage(aliasOrUid, _downloadStatus));

        await Parallel.ForEachAsync(downloadGroups, new ParallelOptions()
        {
            MaxDegreeOfParallelism = downloadGroups.Count(),
            CancellationToken = ct,
        },
        async (fileGroup, token) =>
        {
            // let server predownload files
            await _orchestrator.SendRequestAsync(HttpMethod.Post, MareFiles.RequestEnqueueFullPath(fileGroup.First().DownloadUri),
                fileGroup.Select(c => c.Hash), token).ConfigureAwait(false);

            foreach (var file in fileGroup)
            {
                var tempPath = _fileDbManager.GetCacheFilePath(file.Hash, true);
                var hash = file.Hash;
                Progress<long> progress = new((bytesDownloaded) =>
                {
                    _downloadStatus[fileGroup.Key].TransferredBytes += bytesDownloaded;
                    file.Transferred += bytesDownloaded;
                });

                try
                {
                    _downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.WaitingForSlot;
                    await _orchestrator.WaitForDownloadSlotAsync(token).ConfigureAwait(false);
                    _downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.WaitingForQueue;
                    await DownloadFileHttpClient(fileGroup.Key, file, tempPath, progress, token).ConfigureAwait(false);
                    _downloadStatus[fileGroup.Key].TransferredFiles += 1;
                }
                catch (OperationCanceledException)
                {
                    File.Delete(tempPath);
                    _logger.LogDebug("Detected cancellation, removing {id}", aliasOrUid);
                    CancelDownload();
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during download of {hash}", file.Hash);
                    continue;
                }
                finally
                {
                    _orchestrator.ReleaseDownloadSlot();
                    _downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.Decompressing;
                }

                var tempFileData = await File.ReadAllBytesAsync(tempPath, token).ConfigureAwait(false);
                var extractedFile = LZ4Codec.Unwrap(tempFileData);
                File.Delete(tempPath);
                var filePath = _fileDbManager.GetCacheFilePath(file.Hash, false);
                await File.WriteAllBytesAsync(filePath, extractedFile, token).ConfigureAwait(false);
                var fi = new FileInfo(filePath);
                Func<DateTime> RandomDayInThePast()
                {
                    DateTime start = new(1995, 1, 1);
                    Random gen = new();
                    int range = (DateTime.Today - start).Days;
                    return () => start.AddDays(gen.Next(range));
                }

                fi.CreationTime = RandomDayInThePast().Invoke();
                fi.LastAccessTime = DateTime.Today;
                fi.LastWriteTime = RandomDayInThePast().Invoke();
                try
                {
                    _ = _fileDbManager.CreateCacheEntry(filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Issue creating cache entry");
                }
            }
        }).ConfigureAwait(false);

        _logger.LogDebug("Download for {id} complete", aliasOrUid);
        CancelDownload();
    }

    private async Task<List<DownloadFileDto>> FilesGetSizes(List<string> hashes, CancellationToken ct)
    {
        if (!_orchestrator.IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");
        var response = await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.ServerFilesGetSizesFullPath(_orchestrator._filesCdnUri!), hashes, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<DownloadFileDto>>(cancellationToken: ct).ConfigureAwait(false) ?? new List<DownloadFileDto>();
    }

    private async Task<Guid> GetQueueRequest(DownloadFileTransfer downloadFileTransfer, CancellationToken ct)
    {
        var response = await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestRequestFileFullPath(downloadFileTransfer.DownloadUri, downloadFileTransfer.Hash), ct).ConfigureAwait(false);
        var responseString = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var requestId = Guid.Parse(responseString.Trim('"'));
        if (!_downloadReady.ContainsKey(requestId))
        {
            _downloadReady[requestId] = false;
        }
        return requestId;
    }

    private async Task WaitForDownloadReady(DownloadFileTransfer downloadFileTransfer, Guid requestId, CancellationToken downloadCt)
    {
        bool alreadyCancelled = false;
        try
        {
            CancellationTokenSource localTimeoutCts = new();
            localTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            CancellationTokenSource composite = CancellationTokenSource.CreateLinkedTokenSource(downloadCt, localTimeoutCts.Token);

            while (_downloadReady.TryGetValue(requestId, out bool isReady) && !isReady)
            {
                try
                {
                    await Task.Delay(250, composite.Token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    if (downloadCt.IsCancellationRequested) throw;

                    var req = await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCheckQueueFullPath(downloadFileTransfer.DownloadUri, requestId, downloadFileTransfer.Hash), downloadCt).ConfigureAwait(false);
                    try
                    {
                        req.EnsureSuccessStatusCode();
                        localTimeoutCts.Dispose();
                        composite.Dispose();
                        localTimeoutCts = new();
                        localTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                        composite = CancellationTokenSource.CreateLinkedTokenSource(downloadCt, localTimeoutCts.Token);
                    }
                    catch (HttpRequestException)
                    {
                        throw;
                    }
                }
            }

            localTimeoutCts.Dispose();
            composite.Dispose();

            _logger.LogDebug("Download {requestId} ready", requestId);
        }
        catch (TaskCanceledException)
        {
            try
            {
                await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer.DownloadUri, requestId)).ConfigureAwait(false);
                alreadyCancelled = true;
            }
            catch { }

            throw;
        }
        finally
        {
            if (downloadCt.IsCancellationRequested && !alreadyCancelled)
            {
                try
                {
                    await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer.DownloadUri, requestId)).ConfigureAwait(false);
                }
                catch { }
            }
            _downloadReady.Remove(requestId, out _);
        }
    }

    private async Task DownloadFileHttpClient(string downloadGroup, DownloadFileTransfer fileTransfer, string tempPath, IProgress<long> progress, CancellationToken ct)
    {
        var requestId = await GetQueueRequest(fileTransfer, ct).ConfigureAwait(false);

        _logger.LogDebug("GUID {requestId} for file {hash} on server {uri}", requestId, fileTransfer.Hash, fileTransfer.DownloadUri);

        await WaitForDownloadReady(fileTransfer, requestId, ct).ConfigureAwait(false);

        _downloadStatus[downloadGroup].DownloadStatus = DownloadStatus.Downloading;

        HttpResponseMessage response = null!;
        var requestUrl = MareFiles.CacheGetFullPath(fileTransfer.DownloadUri, requestId);

        _logger.LogDebug("Downloading {requestUrl} for file {hash}", requestUrl, fileTransfer.Hash);
        try
        {
            response = await _orchestrator.SendRequestAsync(HttpMethod.Get, requestUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Error during download of {requestUrl}, HttpStatusCode: {code}", requestUrl, ex.StatusCode);
            if (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            {
                throw new Exception($"Http error {ex.StatusCode} (cancelled: {ct.IsCancellationRequested}): {requestUrl}", ex);
            }
        }

        try
        {
            var fileStream = File.Create(tempPath);
            await using (fileStream.ConfigureAwait(false))
            {
                var bufferSize = response.Content.Headers.ContentLength > 1024 * 1024 ? 4096 : 1024;
                var buffer = new byte[bufferSize];

                var bytesRead = 0;
                while ((bytesRead = await (await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false)).ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    ct.ThrowIfCancellationRequested();

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);

                    progress.Report(bytesRead);
                }

                _logger.LogDebug("{requestUrl} downloaded to {tempPath}", requestUrl, tempPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during file download of {requestUrl}", requestUrl);
            try
            {
                if (!tempPath.IsNullOrEmpty())
                    File.Delete(tempPath);
            }
            catch { }
            throw;
        }
    }
}
