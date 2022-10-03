using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
    private readonly HashSet<string> _verifiedUploadedHashes;

    private int _downloadId = 0;
    public void CancelUpload()
    {
        if (_uploadCancellationTokenSource != null)
        {
            Logger.Debug("Cancelling upload");
            _uploadCancellationTokenSource?.Cancel();
            _mareHub!.SendAsync(Api.SendFileAbortUpload);
            CurrentUploads.Clear();
        }
    }

    public async Task DeleteAllMyFiles()
    {
        await _mareHub!.SendAsync(Api.SendFileDeleteAllFiles).ConfigureAwait(false);
    }

    private async Task<string> DownloadFile(int downloadId, string hash, Uri downloadUri, CancellationToken ct)
    {
        using WebClient wc = new();
        wc.Headers.Add("Authorization", SecretKey);
        DownloadProgressChangedEventHandler progChanged = (s, e) =>
        {
            try
            {
                CurrentDownloads[downloadId].Single(f => string.Equals(f.Hash, hash, StringComparison.Ordinal)).Transferred = e.BytesReceived;
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not set download progress for " + hash);
                Logger.Warn(ex.Message);
                Logger.Warn(ex.StackTrace ?? string.Empty);
            }
        };
        wc.DownloadProgressChanged += progChanged;

        string fileName = Path.GetTempFileName();

        ct.Register(wc.CancelAsync);

        try
        {
            await wc.DownloadFileTaskAsync(downloadUri, fileName).ConfigureAwait(false);
        }
        catch { }

        CurrentDownloads[downloadId].Single(f => string.Equals(f.Hash, hash, StringComparison.Ordinal)).Transferred = CurrentDownloads[downloadId].Single(f => string.Equals(f.Hash, hash, StringComparison.Ordinal)).Total;

        wc.DownloadProgressChanged -= progChanged;
        return fileName;
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

    private async Task DownloadFilesInternal(int currentDownloadId, List<FileReplacementDto> fileReplacementDto, CancellationToken ct)
    {
        Logger.Debug("Downloading files (Download ID " + currentDownloadId + ")");

        List<DownloadFileDto> downloadFileInfoFromService = new();
        downloadFileInfoFromService.AddRange(await _mareHub!.InvokeAsync<List<DownloadFileDto>>(Api.InvokeGetFilesSizes, fileReplacementDto.Select(f => f.Hash).ToList(), ct).ConfigureAwait(false));

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

        await Parallel.ForEachAsync(CurrentDownloads[currentDownloadId].Where(f => f.CanBeTransferred), new ParallelOptions()
        {
            MaxDegreeOfParallelism = 5,
            CancellationToken = ct
        },
        async (file, token) =>
        {
            var hash = file.Hash;
            var tempFile = await DownloadFile(currentDownloadId, file.Hash, file.DownloadUri, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                File.Delete(tempFile);
                Logger.Debug("Detected cancellation, removing " + currentDownloadId);
                DownloadFinished?.Invoke();
                CancelDownload(currentDownloadId);
                return;
            }

            var tempFileData = await File.ReadAllBytesAsync(tempFile, token).ConfigureAwait(false);
            var extractedFile = LZ4Codec.Unwrap(tempFileData);
            File.Delete(tempFile);
            var filePath = Path.Combine(_pluginConfiguration.CacheFolder, file.Hash);
            await File.WriteAllBytesAsync(filePath, extractedFile, token).ConfigureAwait(false);
            var fi = new FileInfo(filePath);
            Func<DateTime> RandomDayFunc()
            {
                DateTime start = new(1995, 1, 1);
                Random gen = new();
                int range = (DateTime.Today - start).Days;
                return () => start.AddDays(gen.Next(range));
            }

            fi.CreationTime = RandomDayFunc().Invoke();
            fi.LastAccessTime = RandomDayFunc().Invoke();
            fi.LastWriteTime = RandomDayFunc().Invoke();
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
            if (!_verifiedUploadedHashes.Contains(item))
            {
                unverifiedUploadHashes.Add(item);
            }
        }

        if (unverifiedUploadHashes.Any())
        {
            Logger.Debug("Verifying " + unverifiedUploadHashes.Count + " files");
            var filesToUpload = await _mareHub!.InvokeAsync<List<UploadFileDto>>(Api.InvokeFileSendFiles, unverifiedUploadHashes, uploadToken).ConfigureAwait(false);

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
            }

            Logger.Debug("Upload tasks complete, waiting for server to confirm");
            var anyUploadsOpen = await _mareHub!.InvokeAsync<bool>(Api.InvokeFileIsUploadFinished, uploadToken).ConfigureAwait(false);
            Logger.Debug("Uploads open: " + anyUploadsOpen);
            while (anyUploadsOpen && !uploadToken.IsCancellationRequested)
            {
                anyUploadsOpen = await _mareHub!.InvokeAsync<bool>(Api.InvokeFileIsUploadFinished, uploadToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(0.5), uploadToken).ConfigureAwait(false);
                Logger.Debug("Waiting for uploads to finish");
            }

            foreach (var item in unverifiedUploadHashes)
            {
                _verifiedUploadedHashes.Add(item);
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
            await _mareHub!.InvokeAsync(Api.InvokeUserPushCharacterDataToVisibleClients, character, visibleCharacterIds, uploadToken).ConfigureAwait(false);
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

        await _mareHub!.SendAsync(Api.SendFileUploadFileStreamAsync, fileHash, AsyncFileData(uploadToken), uploadToken).ConfigureAwait(false);
    }

    public void CancelDownload(int downloadId)
    {
        while (CurrentDownloads.ContainsKey(downloadId))
        {
            CurrentDownloads.TryRemove(downloadId, out _);
        }
    }
}

