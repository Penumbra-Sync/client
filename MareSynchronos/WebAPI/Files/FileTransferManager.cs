using Dalamud.Utility;
using LZ4;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Files;
using MareSynchronos.API.Routes;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MareSynchronos.WebAPI.Files;

public class FileTransferManager : MediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly ConcurrentDictionary<Guid, bool> _downloadReady = new();
    private readonly FileCacheManager _fileDbManager;
    private readonly HttpClient _httpClient;
    private readonly ServerConfigurationManager _serverManager;
    private readonly Dictionary<string, DateTime> _verifiedUploadedHashes = new(StringComparer.Ordinal);
    private int _downloadId = 0;
    private Uri? _filesCdnUri;
    private CancellationTokenSource? _uploadCancellationTokenSource = new();
    public FileTransferManager(ILogger<FileTransferManager> logger, MareMediator mediator,
        MareConfigService configService, FileCacheManager fileDbManager, ServerConfigurationManager serverManager) : base(logger, mediator)
    {
        _httpClient = new HttpClient();
        _configService = configService;
        _fileDbManager = fileDbManager;
        _serverManager = serverManager;

        Mediator.Subscribe<DownloadReadyMessage>(this, (msg) =>
        {
            _downloadReady[((DownloadReadyMessage)msg).RequestId] = true;
        });

        Mediator.Subscribe<ConnectedMessage>(this, (msg) =>
        {
            _filesCdnUri = ((ConnectedMessage)msg).Connection.ServerInfo.FileServerAddress;
        });

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            Reset();
            _filesCdnUri = null;
        });
    }

    public ConcurrentDictionary<string, List<DownloadFileTransfer>> CurrentDownloads { get; } = new();
    public List<FileTransfer> CurrentUploads { get; } = new();
    public List<FileTransfer> ForbiddenTransfers { get; } = new();
    public bool IsDownloading => !CurrentDownloads.IsEmpty;
    public bool IsUploading => CurrentUploads.Count > 0;
    private bool IsInitialized => _filesCdnUri != null;
    public void CancelDownload(string downloadId)
    {
        while (CurrentDownloads.ContainsKey(downloadId))
        {
            CurrentDownloads.TryRemove(downloadId, out _);
        }
    }

    public bool CancelUpload()
    {
        if (CurrentUploads.Any())
        {
            _logger.LogDebug("Cancelling current upload");
            _uploadCancellationTokenSource?.Cancel();
            _uploadCancellationTokenSource?.Dispose();
            _uploadCancellationTokenSource = null;
            CurrentUploads.Clear();
            return true;
        }

        return false;
    }

    public async Task DeleteAllFiles()
    {
        if (_filesCdnUri is null) throw new InvalidOperationException("FileTransferManager is not initialized");

        await SendRequestAsync(HttpMethod.Post, MareFiles.ServerFilesDeleteAllFullPath(_filesCdnUri)).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        base.Dispose();
        Reset();
    }
    public async Task DownloadFiles(string currentDownloadId, List<FileReplacementData> fileReplacementDto, CancellationToken ct)
    {
        Mediator.Publish(new HaltScanMessage("Download"));
        try
        {
            await DownloadFilesInternal(currentDownloadId, fileReplacementDto, ct).ConfigureAwait(false);
        }
        catch
        {
            CancelDownload(currentDownloadId);
        }
        finally
        {
            Mediator.Publish(new ResumeScanMessage("Download"));
        }
    }

    public int GetDownloadId() => _downloadId++;

    public async Task<CharacterData> UploadFiles(CharacterData data)
    {
        CancelUpload();

        _uploadCancellationTokenSource = new CancellationTokenSource();
        var uploadToken = _uploadCancellationTokenSource.Token;
        _logger.LogDebug("Sending Character data {hash} to service {url}", data.DataHash.Value, _serverManager.CurrentApiUrl);

        HashSet<string> unverifiedUploads = VerifyFiles(data);
        if (unverifiedUploads.Any())
        {
            await UploadMissingFiles(unverifiedUploads, uploadToken).ConfigureAwait(false);
            _logger.LogInformation("Upload complete for {hash}", data.DataHash.Value);
        }

        foreach (var kvp in data.FileReplacements)
        {
            data.FileReplacements[kvp.Key].RemoveAll(i => ForbiddenTransfers.Any(f => string.Equals(f.Hash, i.Hash, StringComparison.OrdinalIgnoreCase)));
        }

        return data;
    }

    private async Task DownloadFileHttpClient(DownloadFileTransfer fileTransfer, string tempPath, IProgress<long> progress, CancellationToken ct)
    {
        var requestId = await GetQueueRequest(fileTransfer, ct).ConfigureAwait(false);

        _logger.LogDebug("GUID {requestId} for file {hash} on server {uri}", requestId, fileTransfer.Hash, fileTransfer.DownloadUri);

        await WaitForDownloadReady(fileTransfer, requestId, ct).ConfigureAwait(false);

        HttpResponseMessage response = null!;
        var requestUrl = MareFiles.CacheGetFullPath(fileTransfer.DownloadUri, requestId);

        _logger.LogDebug("Downloading {requestUrl} for file {hash}", requestUrl, fileTransfer.Hash);
        try
        {
            response = await SendRequestAsync(HttpMethod.Get, requestUrl, ct).ConfigureAwait(false);
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

    private async Task DownloadFilesInternal(string currentDownloadId, List<FileReplacementData> fileReplacement, CancellationToken ct)
    {
        _logger.LogDebug("Downloading files (Download ID {id})", currentDownloadId);

        List<DownloadFileDto> downloadFileInfoFromService = new();
        downloadFileInfoFromService.AddRange(await FilesGetSizes(fileReplacement.Select(f => f.Hash).ToList(), ct).ConfigureAwait(false));

        _logger.LogDebug("Files with size 0 or less: {files}", string.Join(", ", downloadFileInfoFromService.Where(f => f.Size <= 0).Select(f => f.Hash)));

        CurrentDownloads[currentDownloadId] = downloadFileInfoFromService.Distinct().Select(d => new DownloadFileTransfer(d))
            .Where(d => d.CanBeTransferred).ToList();

        foreach (var dto in downloadFileInfoFromService.Where(c => c.IsForbidden))
        {
            if (!ForbiddenTransfers.Any(f => string.Equals(f.Hash, dto.Hash, StringComparison.Ordinal)))
            {
                ForbiddenTransfers.Add(new DownloadFileTransfer(dto));
            }
        }

        var downloadGroups = CurrentDownloads[currentDownloadId].Where(f => f.CanBeTransferred).GroupBy(f => f.DownloadUri.Host + f.DownloadUri.Port, StringComparer.Ordinal);

        await Parallel.ForEachAsync(downloadGroups, new ParallelOptions()
        {
            MaxDegreeOfParallelism = downloadGroups.Count(),
            CancellationToken = ct,
        },
        async (fileGroup, token) =>
        {
            // let server predownload files
            await SendRequestAsync(HttpMethod.Post, MareFiles.RequestEnqueueFullPath(fileGroup.First().DownloadUri),
                fileGroup.Select(c => c.Hash), token).ConfigureAwait(false);

            foreach (var file in fileGroup)
            {
                var hash = file.Hash;
                Progress<long> progress = new((bytesDownloaded) =>
                {
                    file.Transferred += bytesDownloaded;
                });

                var tempPath = Path.Combine(_configService.Current.CacheFolder, file.Hash + ".tmp");
                try
                {
                    await DownloadFileHttpClient(file, tempPath, progress, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    File.Delete(tempPath);
                    _logger.LogDebug("Detected cancellation, removing {id}", currentDownloadId);
                    CancelDownload(currentDownloadId);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during download of {hash}", file.Hash);
                    return;
                }

                var tempFileData = await File.ReadAllBytesAsync(tempPath, token).ConfigureAwait(false);
                var extratokenedFile = LZ4Codec.Unwrap(tempFileData);
                File.Delete(tempPath);
                var filePath = Path.Combine(_configService.Current.CacheFolder, file.Hash);
                await File.WriteAllBytesAsync(filePath, extratokenedFile, token).ConfigureAwait(false);
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

        _logger.LogDebug("Download complete, removing {id}", currentDownloadId);
        CancelDownload(currentDownloadId);
    }

    private async Task<List<DownloadFileDto>> FilesGetSizes(List<string> hashes, CancellationToken ct)
    {
        if (!IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");
        var response = await SendRequestAsync(HttpMethod.Get, MareFiles.ServerFilesGetSizesFullPath(_filesCdnUri!), hashes, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<DownloadFileDto>>(cancellationToken: ct).ConfigureAwait(false) ?? new List<DownloadFileDto>();
    }

    private async Task<List<UploadFileDto>> FilesSend(List<string> hashes, CancellationToken ct)
    {
        if (!IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");
        var response = await SendRequestAsync(HttpMethod.Post, MareFiles.ServerFilesFilesSendFullPath(_filesCdnUri!), hashes, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<UploadFileDto>>(cancellationToken: ct).ConfigureAwait(false) ?? new List<UploadFileDto>();
    }

    private async Task<(string, byte[])> GetCompressedFileData(string fileHash, CancellationToken uploadToken)
    {
        var fileCache = _fileDbManager.GetFileCacheByHash(fileHash)!.ResolvedFilepath;
        return (fileHash, LZ4Codec.WrapHC(await File.ReadAllBytesAsync(fileCache, uploadToken).ConfigureAwait(false), 0,
            (int)new FileInfo(fileCache).Length));
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

    private void Reset()
    {
        _uploadCancellationTokenSource?.Cancel();
        _uploadCancellationTokenSource?.Dispose();
        _uploadCancellationTokenSource = null;
        CurrentDownloads.Clear();
        CurrentUploads.Clear();
        _verifiedUploadedHashes.Clear();
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

        if (requestMessage.Content != null && requestMessage.Content is not StreamContent)
        {
            var content = await ((JsonContent)requestMessage.Content).ReadAsStringAsync().ConfigureAwait(false);
            _logger.LogDebug("Sending {method} to {uri} (Content: {content})", requestMessage.Method, requestMessage.RequestUri, content);
        }
        else
        {
            _logger.LogDebug("Sending {method} to {uri}", requestMessage.Method, requestMessage.RequestUri);
        }

        try
        {
            if (ct != null)
                return await _httpClient.SendAsync(requestMessage, ct.Value).ConfigureAwait(false);
            return await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Error during SendRequestInternal for {uri}", requestMessage.RequestUri);
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendRequestStreamAsync(HttpMethod method, Uri uri, ProgressableStreamContent content, CancellationToken ct)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        requestMessage.Content = content;
        return await SendRequestInternalAsync(requestMessage, ct).ConfigureAwait(false);
    }

    private async Task UploadFile(byte[] compressedFile, string fileHash, CancellationToken uploadToken)
    {
        if (!IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");

        _logger.LogInformation("Uploading {file}, {size}", fileHash, UiShared.ByteToString(compressedFile.Length));

        if (uploadToken.IsCancellationRequested) return;

        using var ms = new MemoryStream(compressedFile);

        Progress<UploadProgress> prog = new((prog) =>
        {
            CurrentUploads.Single(f => string.Equals(f.Hash, fileHash, StringComparison.Ordinal)).Transferred = prog.Uploaded;
        });
        var streamContent = new ProgressableStreamContent(ms, prog);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await SendRequestStreamAsync(HttpMethod.Post, MareFiles.ServerFilesUploadFullPath(_filesCdnUri!, fileHash), streamContent, uploadToken).ConfigureAwait(false);
        _logger.LogDebug("Upload Status: {status}", response.StatusCode);
    }

    private async Task UploadMissingFiles(HashSet<string> unverifiedUploadHashes, CancellationToken uploadToken)
    {
        unverifiedUploadHashes = unverifiedUploadHashes.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet(StringComparer.Ordinal);

        _logger.LogDebug("Verifying {count} files", unverifiedUploadHashes.Count);
        var filesToUpload = await FilesSend(unverifiedUploadHashes.ToList(), uploadToken).ConfigureAwait(false);

        foreach (var file in filesToUpload.Where(f => !f.IsForbidden))
        {
            try
            {
                CurrentUploads.Add(new UploadFileTransfer(file)
                {
                    Total = new FileInfo(_fileDbManager.GetFileCacheByHash(file.Hash)!.ResolvedFilepath).Length,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tried to request file {hash} but file was not present", file.Hash);
            }
        }

        foreach (var file in filesToUpload.Where(c => c.IsForbidden))
        {
            if (ForbiddenTransfers.All(f => !string.Equals(f.Hash, file.Hash, StringComparison.Ordinal)))
            {
                ForbiddenTransfers.Add(new UploadFileTransfer(file)
                {
                    LocalFile = _fileDbManager.GetFileCacheByHash(file.Hash)?.ResolvedFilepath ?? string.Empty,
                });
            }

            _verifiedUploadedHashes[file.Hash] = DateTime.UtcNow;
        }

        var totalSize = CurrentUploads.Sum(c => c.Total);
        _logger.LogDebug("Compressing and uploading files");
        Task uploadTask = Task.CompletedTask;
        foreach (var file in CurrentUploads.Where(f => f.CanBeTransferred && !f.IsTransferred).ToList())
        {
            _logger.LogDebug("Compressing {file}", file);
            var data = await GetCompressedFileData(file.Hash, uploadToken).ConfigureAwait(false);
            CurrentUploads.Single(e => string.Equals(e.Hash, data.Item1, StringComparison.Ordinal)).Total = data.Item2.Length;
            await uploadTask.ConfigureAwait(false);
            uploadTask = UploadFile(data.Item2, file.Hash, uploadToken);
            _verifiedUploadedHashes[file.Hash] = DateTime.UtcNow;
            uploadToken.ThrowIfCancellationRequested();
        }

        if (CurrentUploads.Any())
        {
            await uploadTask.ConfigureAwait(false);

            var compressedSize = CurrentUploads.Sum(c => c.Total);
            _logger.LogDebug("Upload complete, compressed {size} to {compressed}", UiShared.ByteToString(totalSize), UiShared.ByteToString(compressedSize));
        }

        foreach (var file in unverifiedUploadHashes.Where(c => !CurrentUploads.Any(u => string.Equals(u.Hash, c, StringComparison.Ordinal))))
        {
            _verifiedUploadedHashes[file] = DateTime.UtcNow;
        }

        CurrentUploads.Clear();
    }

    private HashSet<string> VerifyFiles(CharacterData data)
    {
        HashSet<string> unverifiedUploadHashes = new(StringComparer.Ordinal);
        foreach (var item in data.FileReplacements.SelectMany(c => c.Value.Where(f => string.IsNullOrEmpty(f.FileSwapPath)).Select(v => v.Hash).Distinct(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList())
        {
            if (!_verifiedUploadedHashes.TryGetValue(item, out var verifiedTime))
            {
                verifiedTime = DateTime.MinValue;
            }

            if (verifiedTime < DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)))
            {
                _logger.LogTrace("Verifying {item}, last verified: {date}", item, verifiedTime);
                unverifiedUploadHashes.Add(item);
            }
        }

        return unverifiedUploadHashes;
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
                await SendRequestAsync(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer.DownloadUri, requestId)).ConfigureAwait(false);
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
                    await SendRequestAsync(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer.DownloadUri, requestId)).ConfigureAwait(false);
                }
                catch { }
            }
            _downloadReady.Remove(requestId, out _);
        }
    }

    private class ProgressableStreamContent : StreamContent
    {
        private const int _defaultBufferSize = 4096;
        private readonly int _bufferSize;
        private readonly IProgress<UploadProgress> _progress;
        private readonly Stream _streamToWrite;
        private bool _contentConsumed;

        public ProgressableStreamContent(Stream streamToWrite, IProgress<UploadProgress> downloader)
            : this(streamToWrite, _defaultBufferSize, downloader)
        {
        }

        public ProgressableStreamContent(Stream streamToWrite, int bufferSize, IProgress<UploadProgress> progress)
            : base(streamToWrite, bufferSize)
        {
            if (streamToWrite == null)
            {
                throw new ArgumentNullException(nameof(streamToWrite));
            }

            if (bufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferSize));
            }

            _streamToWrite = streamToWrite;
            _bufferSize = bufferSize;
            _progress = progress;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _streamToWrite.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            PrepareContent();

            var buffer = new byte[_bufferSize];
            var size = _streamToWrite.Length;
            var uploaded = 0;

            using (_streamToWrite)
            {
                while (true)
                {
                    var length = _streamToWrite.Read(buffer, 0, buffer.Length);
                    if (length <= 0)
                    {
                        break;
                    }

                    uploaded += length;
                    _progress.Report(new UploadProgress(uploaded, size));
                    await stream.WriteAsync(buffer.AsMemory(0, length)).ConfigureAwait(false);
                }
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _streamToWrite.Length;
            return true;
        }

        private void PrepareContent()
        {
            if (_contentConsumed)
            {
                if (_streamToWrite.CanSeek)
                {
                    _streamToWrite.Position = 0;
                }
                else
                {
                    throw new InvalidOperationException("The stream has already been read.");
                }
            }

            _contentConsumed = true;
        }
    }

    private class UploadProgress
    {
        public UploadProgress(long uploaded, long size)
        {
            Uploaded = uploaded;
            Size = size;
        }

        public long Size { get; }
        public long Uploaded { get; }
    }
}