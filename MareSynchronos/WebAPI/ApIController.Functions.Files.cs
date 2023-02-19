using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using Dalamud.Utility;
using LZ4;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Files;
using MareSynchronos.API.Routes;
using MareSynchronos.Mediator;
using MareSynchronos.UI;
using MareSynchronos.WebAPI.Utils;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    private readonly Dictionary<string, DateTime> _verifiedUploadedHashes;
    private readonly ConcurrentDictionary<Guid, bool> _downloadReady = new();
    private bool _currentUploadCancelled = false;

    private int _downloadId = 0;
    public async Task<bool> CancelUpload()
    {
        if (CurrentUploads.Any())
        {
            _logger.LogDebug("Cancelling current upload");
            _uploadCancellationTokenSource?.Cancel();
            _uploadCancellationTokenSource?.Dispose();
            _uploadCancellationTokenSource = null;
            CurrentUploads.Clear();
            await FilesAbortUpload().ConfigureAwait(false);
            return true;
        }

        return false;
    }

    public async Task FilesAbortUpload()
    {
        await _mareHub!.SendAsync(nameof(FilesAbortUpload)).ConfigureAwait(false);
    }

    public async Task FilesDeleteAll()
    {
        _verifiedUploadedHashes.Clear();
        await _mareHub!.SendAsync(nameof(FilesDeleteAll)).ConfigureAwait(false);
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

            _logger.LogDebug($"Download {requestId} ready");
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

    private async Task DownloadFileHttpClient(DownloadFileTransfer fileTransfer, string tempPath, IProgress<long> progress, CancellationToken ct)
    {
        var requestId = await GetQueueRequest(fileTransfer, ct).ConfigureAwait(false);

        _logger.LogDebug($"GUID {requestId} for file {fileTransfer.Hash} on server {fileTransfer.DownloadUri}");

        await WaitForDownloadReady(fileTransfer, requestId, ct).ConfigureAwait(false);

        HttpResponseMessage response = null!;
        var requestUrl = MareFiles.CacheGetFullPath(fileTransfer.DownloadUri, requestId);

        _logger.LogDebug($"Downloading {requestUrl} for file {fileTransfer.Hash}");
        try
        {
            response = await SendRequestAsync(HttpMethod.Get, requestUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, $"Error during download of {requestUrl}, HttpStatusCode: {ex.StatusCode}");
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

                _logger.LogDebug($"{requestUrl} downloaded to {tempPath}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Error during file download of {requestUrl}");
            try
            {
                if (!tempPath.IsNullOrEmpty())
                    File.Delete(tempPath);
            }
            catch { }
            throw;
        }
    }

    public int GetDownloadId() => _downloadId++;

    public async Task DownloadFiles(int currentDownloadId, List<FileReplacementData> fileReplacementDto, CancellationToken ct)
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

    private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, Uri uri, CancellationToken? ct = null)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        return await SendRequestInternalAsync(requestMessage, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendRequestInternalAsync(HttpRequestMessage requestMessage, CancellationToken? ct = null)
    {
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this._serverManager.GetToken());

        if (requestMessage.Content != null)
        {
            _logger.LogDebug("Sending " + requestMessage.Method + " to " + requestMessage.RequestUri + " (Content: " + await (((JsonContent)requestMessage.Content).ReadAsStringAsync()) + ")");
        }
        else
        {
            _logger.LogDebug("Sending " + requestMessage.Method + " to " + requestMessage.RequestUri);
        }

        try
        {
            if (ct != null)
                return await _httpClient.SendAsync(requestMessage, ct.Value).ConfigureAwait(false);
            return await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Error during SendRequestInternal for " + requestMessage.RequestUri);
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendRequestAsync<T>(HttpMethod method, Uri uri, T content, CancellationToken ct) where T : class
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        requestMessage.Content = JsonContent.Create(content);
        return await SendRequestInternalAsync(requestMessage, ct).ConfigureAwait(false);
    }

    private async Task DownloadFilesInternal(int currentDownloadId, List<FileReplacementData> fileReplacement, CancellationToken ct)
    {
        _logger.LogDebug("Downloading files (Download ID " + currentDownloadId + ")");

        List<DownloadFileDto> downloadFileInfoFromService = new();
        downloadFileInfoFromService.AddRange(await FilesGetSizes(fileReplacement.Select(f => f.Hash).ToList()).ConfigureAwait(false));

        _logger.LogDebug("Files with size 0 or less: " + string.Join(", ", downloadFileInfoFromService.Where(f => f.Size <= 0).Select(f => f.Hash)));

        CurrentDownloads[currentDownloadId] = downloadFileInfoFromService.Distinct().Select(d => new DownloadFileTransfer(d))
            .Where(d => d.CanBeTransferred).ToList();

        foreach (var dto in downloadFileInfoFromService.Where(c => c.IsForbidden))
        {
            if (ForbiddenTransfers.All(f => !string.Equals(f.Hash, dto.Hash, StringComparison.Ordinal)))
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
                    _logger.LogDebug("Detected cancellation, removing " + currentDownloadId);
                    CancelDownload(currentDownloadId);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during download of " + file.Hash);
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

        _logger.LogDebug("Download complete, removing " + currentDownloadId);
        CancelDownload(currentDownloadId);
    }

    public async Task PushCharacterData(CharacterData data, List<UserData> visibleCharacters)
    {
        if (!IsConnected) return;

        try
        {
            _currentUploadCancelled = await CancelUpload().ConfigureAwait(false);

            _uploadCancellationTokenSource = new CancellationTokenSource();
            var uploadToken = _uploadCancellationTokenSource.Token;
            _logger.LogDebug($"Sending Character data {data.DataHash.Value} to service {_serverManager.CurrentApiUrl}");

            HashSet<string> unverifiedUploads = VerifyFiles(data);
            if (unverifiedUploads.Any())
            {
                await UploadMissingFiles(unverifiedUploads, uploadToken).ConfigureAwait(false);
                _logger.LogInformation("Upload complete for " + data.DataHash.Value);
            }
            await PushCharacterDataInternal(data, visibleCharacters.ToList()).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Upload operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during upload of files");
        }
        finally
        {
            if (!_currentUploadCancelled)
                _currentUploadCancelled = await CancelUpload().ConfigureAwait(false);
        }
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
                _logger.LogTrace("Verifying " + item + ", last verified: " + verifiedTime);
                unverifiedUploadHashes.Add(item);
            }
        }

        return unverifiedUploadHashes;
    }

    private async Task UploadMissingFiles(HashSet<string> unverifiedUploadHashes, CancellationToken uploadToken)
    {
        unverifiedUploadHashes = unverifiedUploadHashes.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet(StringComparer.Ordinal);

        _logger.LogDebug("Verifying " + unverifiedUploadHashes.Count + " files");
        var filesToUpload = await FilesSend(unverifiedUploadHashes.ToList()).ConfigureAwait(false);

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
                _logger.LogWarning(ex, "Tried to request file " + file.Hash + " but file was not present");
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
        foreach (var file in CurrentUploads.Where(f => f.CanBeTransferred && !f.IsTransferred).ToList())
        {
            _logger.LogDebug("Compressing and uploading " + file);
            var data = await GetCompressedFileData(file.Hash, uploadToken).ConfigureAwait(false);
            CurrentUploads.Single(e => string.Equals(e.Hash, data.Item1, StringComparison.Ordinal)).Total = data.Item2.Length;
            await UploadFile(data.Item2, file.Hash, uploadToken).ConfigureAwait(false);
            _verifiedUploadedHashes[file.Hash] = DateTime.UtcNow;
            uploadToken.ThrowIfCancellationRequested();
        }

        if (CurrentUploads.Any())
        {
            var compressedSize = CurrentUploads.Sum(c => c.Total);
            _logger.LogDebug($"Compressed {UiShared.ByteToString(totalSize)} to {UiShared.ByteToString(compressedSize)} ({(compressedSize / (double)totalSize):P2})");

            _logger.LogDebug("Upload tasks complete, waiting for server to confirm");
            _logger.LogDebug("Uploads open: " + CurrentUploads.Any(c => c.IsInTransfer));
            const double waitStep = 1.0d;
            while (CurrentUploads.Any(c => c.IsInTransfer) && !uploadToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(waitStep), uploadToken).ConfigureAwait(false);
                _logger.LogDebug("Waiting for uploads to finish");
            }
        }

        foreach(var file in unverifiedUploadHashes.Where(c=>!CurrentUploads.Any(u=> string.Equals(u.Hash, c, StringComparison.Ordinal))))
        {
            _verifiedUploadedHashes[file] = DateTime.UtcNow;
        }

        CurrentUploads.Clear();
    }

    private async Task PushCharacterDataInternal(CharacterData character, List<UserData> visibleCharacters)
    {
        _logger.LogInformation("Pushing character data for " + character.DataHash.Value + " to " + string.Join(", ", visibleCharacters.Select(c => c.AliasOrUID)));
        StringBuilder sb = new();
        foreach (var item in character.FileReplacements)
        {
            sb.AppendLine($"FileReplacements for {item.Key}: {item.Value.Count}");
        }
        foreach (var item in character.GlamourerData)
        {
            sb.AppendLine($"GlamourerData for {item.Key}: {!string.IsNullOrEmpty(item.Value)}");
        }
        _logger.LogDebug("Chara data contained: " + Environment.NewLine + sb.ToString());
        await UserPushData(new(visibleCharacters, character)).ConfigureAwait(false);
    }

    private async Task<(string, byte[])> GetCompressedFileData(string fileHash, CancellationToken uploadToken)
    {
        var fileCache = _fileDbManager.GetFileCacheByHash(fileHash)!.ResolvedFilepath;
        return (fileHash, LZ4Codec.WrapHC(await File.ReadAllBytesAsync(fileCache, uploadToken).ConfigureAwait(false), 0,
            (int)new FileInfo(fileCache).Length));
    }

    private async Task UploadFile(byte[] compressedFile, string fileHash, CancellationToken uploadToken)
    {
        if (uploadToken.IsCancellationRequested) return;

        async IAsyncEnumerable<byte[]> AsyncFileData([EnumeratorCancellation] CancellationToken token)
        {
            var chunkSize = 1024 * 512; // 512kb
            using var ms = new MemoryStream(compressedFile);
            var buffer = new byte[chunkSize];
            int bytesRead;
            while ((bytesRead = await ms.ReadAsync(buffer, 0, chunkSize, token).ConfigureAwait(false)) > 0 && !token.IsCancellationRequested)
            {
                CurrentUploads.Single(f => string.Equals(f.Hash, fileHash, StringComparison.Ordinal)).Transferred += bytesRead;
                token.ThrowIfCancellationRequested();
                yield return bytesRead == chunkSize ? buffer.ToArray() : buffer.Take(bytesRead).ToArray();
            }
        }

        await FilesUploadStreamAsync(fileHash, AsyncFileData(uploadToken)).ConfigureAwait(false);
    }

    public async Task FilesUploadStreamAsync(string hash, IAsyncEnumerable<byte[]> fileContent)
    {
        await _mareHub!.InvokeAsync(nameof(FilesUploadStreamAsync), hash, fileContent).ConfigureAwait(false);
    }

    public async Task<bool> FilesIsUploadFinished()
    {
        return await _mareHub!.InvokeAsync<bool>(nameof(FilesIsUploadFinished)).ConfigureAwait(false);
    }

    public async Task<List<DownloadFileDto>> FilesGetSizes(List<string> hashes)
    {
        return await _mareHub!.InvokeAsync<List<DownloadFileDto>>(nameof(FilesGetSizes), hashes).ConfigureAwait(false);
    }

    public async Task<List<UploadFileDto>> FilesSend(List<string> fileListHashes)
    {
        return await _mareHub!.InvokeAsync<List<UploadFileDto>>(nameof(FilesSend), fileListHashes).ConfigureAwait(false);
    }


    public void CancelDownload(int downloadId)
    {
        while (CurrentDownloads.ContainsKey(downloadId))
        {
            CurrentDownloads.TryRemove(downloadId, out _);
        }
    }
}

