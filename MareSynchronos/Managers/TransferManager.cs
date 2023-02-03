using Dalamud.Utility;
using LZ4;
using MareSynchronos.API.Data;
using MareSynchronos.API.Routes;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Utils;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MareSynchronos.Managers;

public class TransferManager : MediatorSubscriberBase, IDisposable
{
    public record DownloadFileStatus(long DownloadedBytes, long TotalBytes, int DownloadedFiles, int TotalFiles);

    private readonly ServerConfigurationManager _serverManager;
    private readonly ApiController _apiController;
    private readonly ConfigurationService _configService;
    private readonly FileCacheManager _fileDbManager;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _downloadCancellationTokenSources = new(StringComparer.Ordinal);
    private HttpClient _httpClient = new();
    private readonly ConcurrentDictionary<Guid, bool> _downloadReady = new();

    public TransferManager(MareMediator mediator, ServerConfigurationManager serverManager, ApiController apiController, ConfigurationService configService, FileCacheManager fileDbManager) : base(mediator)
    {
        _serverManager = serverManager;
        _apiController = apiController;
        _configService = configService;
        _fileDbManager = fileDbManager;
        Mediator.Subscribe<ConnectedMessage>(this, (_) =>
        {
            _httpClient.Dispose();
            _httpClient = new();
        });
        Mediator.Subscribe<DownloadReadyMessage>(this, (msg) =>
        {
            DownloadReadyMessage actualMsg = (DownloadReadyMessage)msg;
            _downloadReady[actualMsg.RequestId] = true;
        });
    }

    public async Task<List<DownloadFileTransfer>> DownloadForPlayer(string playerName, string uid, List<FileReplacementData> filesToDownload)
    {
        RemoveDownloadForUid(uid);

        _downloadCancellationTokenSources[uid] = new CancellationTokenSource();

        Mediator.Publish(new DownloadStartedMessage(uid, playerName, filesToDownload.Count));

        try
        {
            return await DownloadPlayerInternal(uid, filesToDownload, _downloadCancellationTokenSources[uid].Token)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AggregateException ex)
        {
            foreach (var exc in ex.InnerExceptions)
            {
                Logger.Error("Error during download for " + uid, exc);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Error during download of " + uid, ex);
        }
        finally
        {
            RemoveDownloadForUid(uid);
            Mediator.Publish(new DownloadFinishedMessage(uid));
        }

        return new List<DownloadFileTransfer>();
    }

    private async Task<List<DownloadFileTransfer>> DownloadPlayerInternal(string uid, List<FileReplacementData> filesToDownload, CancellationToken token)
    {
        // get file transfer objects
        var fileTransfers = (await _apiController.FilesGetSizes(filesToDownload
            .Where(f => !f.Hash.IsNullOrEmpty())
            .Select(c => c.Hash)
            .Distinct(StringComparer.Ordinal).ToList()).ConfigureAwait(false))
            .Select(t => new DownloadFileTransfer(t)).ToList();
        var groupedTransfers = fileTransfers.Where(t => !t.IsForbidden)
            .GroupBy(f => f.DownloadUri.Host + f.DownloadUri.Port, StringComparer.Ordinal).ToList();

        Mediator.Publish(new DownloadUpdateMessage(uid, new(0,
            groupedTransfers.Sum(f => f.Sum(c => c.Total)),
            0,
            groupedTransfers.Sum(t => t.Count()))));

        // gather files that are forbidden and send them to the api controller
        var forbiddenFiles = fileTransfers.Where(f => f.IsForbidden).ToList();

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                Mediator.Publish(new DownloadUpdateMessage(uid, new(groupedTransfers.Sum(f => f.Sum(p => p.Transferred)),
                    groupedTransfers.Sum(f => f.Sum(c => c.Total)),
                    groupedTransfers.Sum(f => f.Count(p => p.CanBeTransferred)),
                    groupedTransfers.Sum(t => t.Count()))));
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }, token);

        var exceptions = new ConcurrentQueue<Exception>();
        await Parallel.ForEachAsync(groupedTransfers, new ParallelOptions()
        {
            MaxDegreeOfParallelism = groupedTransfers.Count,
            CancellationToken = token
        }, async (fileGroup, ct) =>
        {
            // send enqueue request
            await SendRequestAsync(HttpMethod.Post, MareFiles.RequestEnqueueFullPath(fileGroup.First().DownloadUri),
                fileGroup.Select(c => c.Hash), ct).ConfigureAwait(false);

            // for each file download file and save it locally
            foreach (var file in fileGroup.OrderByDescending(f => f.Total))
            {
                Progress<long> progress = new((bytesDownloaded) =>
                {
                    file.Transferred += bytesDownloaded;

                });

                var tempPath = Path.Combine(_configService.Current.CacheFolder, file.Hash + ".tmp");
                try
                {
                    await DownloadFileHttpClient(file, tempPath, progress, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    File.Delete(tempPath);
                    exceptions.Enqueue(ex);
                    return;
                }

                var tempFileData = await File.ReadAllBytesAsync(tempPath, token).ConfigureAwait(false);
                var unwrapFile = LZ4Codec.Unwrap(tempFileData);
                File.Delete(tempPath);
                var filePath = Path.Combine(_configService.Current.CacheFolder, file.Hash);
                await File.WriteAllBytesAsync(filePath, unwrapFile, token).ConfigureAwait(false);
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
                _ = _fileDbManager.CreateCacheEntry(filePath);
            }
        }).ConfigureAwait(false);

        if (!exceptions.IsEmpty && !token.IsCancellationRequested) throw new AggregateException(exceptions);

        return forbiddenFiles;
    }

    private async Task DownloadFileHttpClient(DownloadFileTransfer fileTransfer, string tempPath, IProgress<long> progress, CancellationToken ct)
    {
        var requestId = await GetQueueRequest(fileTransfer, ct).ConfigureAwait(false);

        Logger.Debug($"GUID {requestId} for file {fileTransfer.Hash} on server {fileTransfer.DownloadUri}");

        await WaitForDownloadReady(fileTransfer, requestId, ct).ConfigureAwait(false);

        HttpResponseMessage response = null!;
        var requestUrl = MareFiles.CacheGetFullPath(fileTransfer.DownloadUri, requestId);

        Logger.Debug($"Downloading {requestUrl} for file {fileTransfer.Hash}");
        try
        {
            response = await SendRequestAsync(HttpMethod.Get, requestUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Logger.Warn($"Error during download of {requestUrl}, HttpStatusCode: {ex.StatusCode}");
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

                Logger.Debug($"{requestUrl} downloaded to {tempPath}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error during file download of {requestUrl}", ex);
            try
            {
                if (!tempPath.IsNullOrEmpty())
                    File.Delete(tempPath);
            }
            catch { }
            throw;
        }
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

                    var req = await SendRequestAsync(HttpMethod.Get, MareFiles.RequestCheckQueueFullPath(downloadFileTransfer.DownloadUri, requestId, downloadFileTransfer.Hash), downloadCt).ConfigureAwait(false);
                    req.EnsureSuccessStatusCode();
                    localTimeoutCts.Dispose();
                    composite.Dispose();
                    localTimeoutCts = new();
                    localTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                    composite = CancellationTokenSource.CreateLinkedTokenSource(downloadCt, localTimeoutCts.Token);
                }
            }

            localTimeoutCts.Dispose();
            composite.Dispose();

            Logger.Debug($"Download {requestId} ready");
        }
        catch (TaskCanceledException)
        {
            alreadyCancelled = await CancelDownload(downloadFileTransfer, requestId).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _downloadReady.Remove(requestId, out _);
            if (downloadCt.IsCancellationRequested && !alreadyCancelled)
            {
                await CancelDownload(downloadFileTransfer, requestId).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> CancelDownload(DownloadFileTransfer transfer, Guid requestId)
    {
        bool successfullyCancelled = false;
        int attempts = 0;
        while (!successfullyCancelled && attempts < 10)
        {
            attempts++;
            try
            {
                await SendRequestAsync(HttpMethod.Get,
                        MareFiles.RequestCancelFullPath(transfer.DownloadUri, requestId))
                    .ConfigureAwait(false);
                successfullyCancelled = true;
            }
            catch (Exception ex)
            {
                Logger.Warn("Error during Download Cancellation for " + requestId, ex);
            }
        }

        return successfullyCancelled;
    }

    private async Task<Guid> GetQueueRequest(DownloadFileTransfer downloadFileTransfer, CancellationToken ct)
    {
        var response = await SendRequestAsync(HttpMethod.Get, MareFiles.RequestRequestFileFullPath(downloadFileTransfer.DownloadUri, downloadFileTransfer.Hash), ct).ConfigureAwait(false);
        var responseString = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var requestId = Guid.Parse(responseString.Trim('"'));
        if (!_downloadReady.ContainsKey(requestId))
        {
            _downloadReady[requestId] = false;
        }
        return requestId;
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, Uri uri, CancellationToken? ct = null)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        return await SendRequestInternalAsync(requestMessage, ct).ConfigureAwait(false);
    }
    private async Task<HttpResponseMessage> SendRequestAsync<T>(HttpMethod method, Uri uri, T content, CancellationToken ct) where T : class
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        requestMessage.Content = JsonContent.Create(content);
        return await SendRequestInternalAsync(requestMessage, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendRequestInternalAsync(HttpRequestMessage requestMessage, CancellationToken? ct = null)
    {
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serverManager.GetToken());

        if (requestMessage.Content != null)
        {
            Logger.Debug("Sending " + requestMessage.Method + " to " + requestMessage.RequestUri + " (Content: " + await (((JsonContent)requestMessage.Content).ReadAsStringAsync()) + ")");
        }
        else
        {
            Logger.Debug("Sending " + requestMessage.Method + " to " + requestMessage.RequestUri);
        }

        try
        {
            if (ct != null)
                return await _httpClient.SendAsync(requestMessage, ct.Value).ConfigureAwait(false);
            return await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("Error during SendRequestInternal for " + requestMessage.RequestUri, ex);
            throw;
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _httpClient.Dispose();
    }

    public void RemoveDownloadForUid(string uid)
    {
        if (_downloadCancellationTokenSources.Remove(uid, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }
    }
}