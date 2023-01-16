using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LZ4;
using MareSynchronos.API;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    private readonly Dictionary<string, DateTime> _verifiedUploadedHashes;
    private readonly ConcurrentDictionary<Guid, bool> _downloadReady = new();

    private int _downloadId = 0;
    public async void CancelUpload()
    {
        if (_uploadCancellationTokenSource != null)
        {
            Logger.Debug("Cancelling upload");
            _uploadCancellationTokenSource?.Cancel();
            CurrentUploads.Clear();
            await FilesAbortUpload().ConfigureAwait(false);
        }
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
        var response = await SendRequestAsync<object>(HttpMethod.Get, MareFiles.RequestRequestFileFullPath(downloadFileTransfer.DownloadUri, downloadFileTransfer.Hash), ct: ct).ConfigureAwait(false);
        var responseString = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var requestId = Guid.Parse(responseString.Trim('"'));
        if (!_downloadReady.ContainsKey(requestId))
        {
            _downloadReady[requestId] = false;
        }
        return requestId;
    }

    private async Task WaitForDownloadReady(DownloadFileTransfer downloadFileTransfer, Guid requestId, CancellationToken ct)
    {
        bool alreadyCancelled = false;
        try
        {
            while (!ct.IsCancellationRequested && _downloadReady.TryGetValue(requestId, out bool isReady) && !isReady)
            {
                Logger.Verbose($"Waiting for {requestId} to become ready for download");
                await Task.Delay(250, ct).ConfigureAwait(false);
            }

            Logger.Debug($"Download {requestId} ready");
        }
        catch (TaskCanceledException)
        {
            try
            {
                await SendRequestAsync<object>(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer.DownloadUri, requestId), ct).ConfigureAwait(false);
                alreadyCancelled = true;
            }
            catch { }

            throw;
        }
        finally
        {
            if (ct.IsCancellationRequested && !alreadyCancelled)
            {
                try
                {
                    await SendRequestAsync<object>(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer.DownloadUri, requestId), ct).ConfigureAwait(false);
                }
                catch { }
            }
            _downloadReady.Remove(requestId, out _);
        }
    }

    private async Task<string> DownloadFileHttpClient(DownloadFileTransfer fileTransfer, IProgress<long> progress, CancellationToken ct)
    {
        var requestId = await GetQueueRequest(fileTransfer, ct).ConfigureAwait(false);

        Logger.Debug($"GUID {requestId} for file {fileTransfer.Hash} on server {fileTransfer.DownloadUri}");

        await WaitForDownloadReady(fileTransfer, requestId, ct).ConfigureAwait(false);

        HttpResponseMessage response = null!;
        var requestUrl = MareFiles.CacheGetFullPath(fileTransfer.DownloadUri, requestId);

        Logger.Debug($"Downloading {requestUrl} for file {fileTransfer.Hash}");
        try
        {
            response = await SendRequestAsync<object>(HttpMethod.Get, requestUrl, ct: ct).ConfigureAwait(false);
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
            var fileName = Path.GetTempFileName();

            var fileStream = File.Create(fileName);
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

                Logger.Debug($"{requestUrl} downloaded to {fileName}");
                return fileName;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error during file download of {requestUrl}", ex);
            try
            {
                File.Delete(fileName);
            }
            catch { }
            throw;
        }
    }

    public int GetDownloadId() => _downloadId++;

    public async Task DownloadFiles(int currentDownloadId, List<FileReplacementDto> fileReplacementDto, CancellationToken ct)
    {
        DownloadStarted?.Invoke();
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
            DownloadFinished?.Invoke();
        }
    }

    private async Task<HttpResponseMessage> SendRequestAsync<T>(HttpMethod method, Uri uri, T content = default, CancellationToken? ct = null) where T : class
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        if (content != default)
        {
            requestMessage.Content = JsonContent.Create(content);
        }
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Authorization);

        if (content != default)
        {
            Logger.Debug("Sending " + method + " to " + uri + " (Content: " + await (((JsonContent)requestMessage.Content).ReadAsStringAsync()) + ")");
        }
        else
        {
            Logger.Debug("Sending " + method + " to " + uri);
        }

        if (ct.HasValue)
            return await _httpClient.SendAsync(requestMessage, ct.Value).ConfigureAwait(false);

        return await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
    }

    private async Task DownloadFilesInternal(int currentDownloadId, List<FileReplacementDto> fileReplacementDto, CancellationToken ct)
    {
        Logger.Debug("Downloading files (Download ID " + currentDownloadId + ")");

        List<DownloadFileDto> downloadFileInfoFromService = new();
        downloadFileInfoFromService.AddRange(await FilesGetSizes(fileReplacementDto.Select(f => f.Hash).ToList()).ConfigureAwait(false));

        Logger.Debug("Files with size 0 or less: " + string.Join(", ", downloadFileInfoFromService.Where(f => f.Size <= 0).Select(f => f.Hash)));

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
            CancellationToken = ct
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

                var tempFile = await DownloadFileHttpClient(file, progress, token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                {
                    File.Delete(tempFile);
                    Logger.Debug("Detetokened cancellation, removing " + currentDownloadId);
                    CancelDownload(currentDownloadId);
                    return;
                }

                var tempFileData = await File.ReadAllBytesAsync(tempFile, token).ConfigureAwait(false);
                var extratokenedFile = LZ4Codec.Unwrap(tempFileData);
                File.Delete(tempFile);
                var filePath = Path.Combine(_pluginConfiguration.CacheFolder, file.Hash);
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
                    Logger.Warn("Issue adding file to the DB");
                    Logger.Warn(ex.Message);
                    Logger.Warn(ex.StackTrace);
                }
            }
        }).ConfigureAwait(false);

        Logger.Debug("Download complete, removing " + currentDownloadId);
        CancelDownload(currentDownloadId);
    }

    public async Task PushCharacterData(CharacterCacheDto character, List<string> visibleCharacterIds)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", StringComparison.Ordinal)) return;
        Logger.Debug("Sending Character data to service " + ApiUri);

        CancelUpload();
        _uploadCancellationTokenSource = new CancellationTokenSource();
        var uploadToken = _uploadCancellationTokenSource.Token;
        Logger.Verbose("New Token Created");

        List<string> unverifiedUploadHashes = new();
        foreach (var item in character.FileReplacements.SelectMany(c => c.Value.Where(f => string.IsNullOrEmpty(f.FileSwapPath)).Select(v => v.Hash).Distinct(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList())
        {
            if (!_verifiedUploadedHashes.TryGetValue(item, out var verifiedTime))
            {
                verifiedTime = DateTime.MinValue;
            }

            if (verifiedTime < DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)))
            {
                Logger.Verbose("Verifying " + item + ", last verified: " + verifiedTime);
                unverifiedUploadHashes.Add(item);
            }
        }

        if (unverifiedUploadHashes.Any())
        {
            unverifiedUploadHashes = unverifiedUploadHashes.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToList();

            Logger.Debug("Verifying " + unverifiedUploadHashes.Count + " files");
            var filesToUpload = await FilesSend(unverifiedUploadHashes).ConfigureAwait(false);

            foreach (var file in filesToUpload.Where(f => !f.IsForbidden))
            {
                try
                {
                    CurrentUploads.Add(new UploadFileTransfer(file)
                    {
                        Total = new FileInfo(_fileDbManager.GetFileCacheByHash(file.Hash)!.ResolvedFilepath).Length
                    });
                }
                catch (Exception ex)
                {
                    Logger.Warn("Tried to request file " + file.Hash + " but file was not present");
                    Logger.Warn(ex.StackTrace!);
                }
            }

            foreach (var file in filesToUpload.Where(c => c.IsForbidden))
            {
                if (ForbiddenTransfers.All(f => !string.Equals(f.Hash, file.Hash, StringComparison.Ordinal)))
                {
                    ForbiddenTransfers.Add(new UploadFileTransfer(file)
                    {
                        LocalFile = _fileDbManager.GetFileCacheByHash(file.Hash)?.ResolvedFilepath ?? string.Empty
                    });
                }
            }

            var totalSize = CurrentUploads.Sum(c => c.Total);
            Logger.Debug("Compressing and uploading files");
            foreach (var file in CurrentUploads.Where(f => f.CanBeTransferred && !f.IsTransferred).ToList())
            {
                Logger.Debug("Compressing and uploading " + file);
                var data = await GetCompressedFileData(file.Hash, uploadToken).ConfigureAwait(false);
                CurrentUploads.Single(e => string.Equals(e.Hash, data.Item1, StringComparison.Ordinal)).Total = data.Item2.Length;
                await UploadFile(data.Item2, file.Hash, uploadToken).ConfigureAwait(false);
                if (!uploadToken.IsCancellationRequested) continue;
                Logger.Warn("Cancel in filesToUpload loop detected");
                CurrentUploads.Clear();
                break;
            }

            if (CurrentUploads.Any())
            {
                var compressedSize = CurrentUploads.Sum(c => c.Total);
                Logger.Debug($"Compressed {totalSize} to {compressedSize} ({(compressedSize / (double)totalSize):P2})");

                Logger.Debug("Upload tasks complete, waiting for server to confirm");
                Logger.Debug("Uploads open: " + CurrentUploads.Any(c => c.IsInTransfer));
                const double waitStep = 1.0d;
                while (CurrentUploads.Any(c => c.IsInTransfer) && !uploadToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(waitStep), uploadToken).ConfigureAwait(false);
                    Logger.Debug("Waiting for uploads to finish");
                }
            }

            foreach (var item in unverifiedUploadHashes)
            {
                _verifiedUploadedHashes[item] = DateTime.UtcNow;
            }

            CurrentUploads.Clear();
        }
        else
        {
            Logger.Debug("All files already verified");
        }

        if (!uploadToken.IsCancellationRequested)
        {
            Logger.Info("Pushing character data for " + character.GetHashCode() + " to " + string.Join(", ", visibleCharacterIds));
            StringBuilder sb = new();
            foreach (var item in character.FileReplacements)
            {
                sb.AppendLine($"FileReplacements for {item.Key}: {item.Value.Count}");
            }
            foreach (var item in character.GlamourerData)
            {
                sb.AppendLine($"GlamourerData for {item.Key}: {!string.IsNullOrEmpty(item.Value)}");
            }
            Logger.Debug("Chara data contained: " + Environment.NewLine + sb.ToString());
            await UserPushData(character, visibleCharacterIds).ConfigureAwait(false);
        }
        else
        {
            Logger.Warn("=== Upload operation was cancelled ===");
        }

        Logger.Verbose("Upload complete for " + character.GetHashCode());
        _uploadCancellationTokenSource = null;
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

