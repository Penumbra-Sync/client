using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LZ4;
using MareSynchronos.API;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI
{
    public partial class ApiController
    {
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
            await _mareHub!.SendAsync(Api.SendFileDeleteAllFiles);
        }

        private async Task<string> DownloadFile(int downloadId, string hash, CancellationToken ct)
        {
            using WebClient wc = new();
            wc.Headers.Add("Authorization", SecretKey);
            DownloadProgressChangedEventHandler progChanged = (s, e) =>
            {
                try
                {
                    CurrentDownloads[downloadId].Single(f => f.Hash == hash).Transferred = e.BytesReceived;
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
            var baseUri = new Uri(Regex.Replace(ApiUri, "^ws", "http"), UriKind.Absolute);
            var relativeUri = new Uri("cache/" + hash, UriKind.Relative);
            var fileUri = new Uri(baseUri, relativeUri);

            await wc.DownloadFileTaskAsync(fileUri, fileName);

            CurrentDownloads[downloadId].Single(f => f.Hash == hash).Transferred = CurrentDownloads[downloadId].Single(f => f.Hash == hash).Total;

            wc.DownloadProgressChanged -= progChanged;
            return fileName;
        }

        public int GetDownloadId() => _downloadId++;

        public async Task DownloadFiles(int currentDownloadId, List<FileReplacementDto> fileReplacementDto, CancellationToken ct)
        {
            Logger.Debug("Downloading files (Download ID " + currentDownloadId + ")");

            List<DownloadFileDto> downloadFileInfoFromService = new List<DownloadFileDto>();
            foreach (var file in fileReplacementDto)
            {
                downloadFileInfoFromService.Add(await _mareHub!.InvokeAsync<DownloadFileDto>(Api.InvokeFileGetFileSize, file.Hash, ct));
            }

            CurrentDownloads[currentDownloadId] = downloadFileInfoFromService.Distinct().Select(d => new DownloadFileTransfer(d))
                .Where(d => d.CanBeTransferred).ToList();

            foreach (var dto in downloadFileInfoFromService.Where(c => c.IsForbidden))
            {
                if (ForbiddenTransfers.All(f => f.Hash != dto.Hash))
                {
                    ForbiddenTransfers.Add(new DownloadFileTransfer(dto));
                }
            }

            foreach (var file in CurrentDownloads[currentDownloadId].Where(f => f.CanBeTransferred))
            {
                var hash = file.Hash;
                var tempFile = await DownloadFile(currentDownloadId, hash, ct);
                if (ct.IsCancellationRequested)
                {
                    File.Delete(tempFile);
                    Logger.Debug("Detected cancellation, removing " + currentDownloadId);
                    CurrentDownloads.Remove(currentDownloadId);
                    break;
                }

                var tempFileData = await File.ReadAllBytesAsync(tempFile, ct);
                var extractedFile = LZ4Codec.Unwrap(tempFileData);
                File.Delete(tempFile);
                var filePath = Path.Combine(_pluginConfiguration.CacheFolder, file.Hash);
                await File.WriteAllBytesAsync(filePath, extractedFile, ct);
                var fi = new FileInfo(filePath);
                Func<DateTime> RandomDayFunc()
                {
                    DateTime start = new DateTime(1995, 1, 1);
                    Random gen = new Random();
                    int range = (DateTime.Today - start).Days;
                    return () => start.AddDays(gen.Next(range));
                }

                fi.CreationTime = RandomDayFunc().Invoke();
                fi.LastAccessTime = RandomDayFunc().Invoke();
                fi.LastWriteTime = RandomDayFunc().Invoke();
            }

            var allFilesInDb = false;
            while (!allFilesInDb && !ct.IsCancellationRequested)
            {
                await using (var db = new FileCacheContext())
                {
                    allFilesInDb = CurrentDownloads[currentDownloadId]
                        .Where(c => c.CanBeTransferred)
                        .All(h => db.FileCaches.Any(f => f.Hash == h.Hash));
                }

                await Task.Delay(250, ct);
            }

            Logger.Debug("Download complete, removing " + currentDownloadId);
            CurrentDownloads.Remove(currentDownloadId);
        }

        public async Task PushCharacterData(CharacterCacheDto character, List<string> visibleCharacterIds)
        {
            if (!IsConnected || SecretKey == "-") return;
            Logger.Debug("Sending Character data to service " + ApiUri);

            CancelUpload();
            _uploadCancellationTokenSource = new CancellationTokenSource();
            var uploadToken = _uploadCancellationTokenSource.Token;
            Logger.Verbose("New Token Created");

            var filesToUpload = await _mareHub!.InvokeAsync<List<UploadFileDto>>(Api.InvokeFileSendFiles, character.FileReplacements.SelectMany(c => c.Value.Select(v => v.Hash)).Distinct(), uploadToken);

            foreach (var file in filesToUpload.Where(f => !f.IsForbidden))
            {
                await using var db = new FileCacheContext();
                try
                {
                    CurrentUploads.Add(new UploadFileTransfer(file)
                    {
                        Total = new FileInfo(db.FileCaches.FirstOrDefault(f => f.Hash.ToLower() == file.Hash.ToLower())
                            ?.Filepath ?? string.Empty).Length
                    });
                }
                catch (Exception ex)
                {
                    Logger.Warn("Tried to request file " + file.Hash + " but file was not present");
                    Logger.Warn(ex.StackTrace!);
                }
            }

            await using (var db = new FileCacheContext())
            {
                foreach (var file in filesToUpload.Where(c => c.IsForbidden))
                {
                    if (ForbiddenTransfers.All(f => f.Hash != file.Hash))
                    {
                        ForbiddenTransfers.Add(new UploadFileTransfer(file)
                        {
                            LocalFile = db.FileCaches.FirstOrDefault(f => f.Hash == file.Hash)?.Filepath ?? string.Empty
                        });
                    }
                }
            }

            var totalSize = CurrentUploads.Sum(c => c.Total);
            Logger.Debug("Compressing and uploading files");
            foreach (var file in CurrentUploads.Where(f => f.CanBeTransferred && !f.IsTransferred).ToList())
            {
                Logger.Debug("Compressing and uploading " + file);
                var data = await GetCompressedFileData(file.Hash, uploadToken);
                CurrentUploads.Single(e => e.Hash == data.Item1).Total = data.Item2.Length;
                await UploadFile(data.Item2, file.Hash, uploadToken);
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
            var anyUploadsOpen = await _mareHub!.InvokeAsync<bool>(Api.InvokeFileIsUploadFinished, uploadToken);
            Logger.Debug("Uploads open: " + anyUploadsOpen);
            while (anyUploadsOpen && !uploadToken.IsCancellationRequested)
            {
                anyUploadsOpen = await _mareHub!.InvokeAsync<bool>(Api.InvokeFileIsUploadFinished, uploadToken);
                await Task.Delay(TimeSpan.FromSeconds(0.5), uploadToken);
                Logger.Debug("Waiting for uploads to finish");
            }

            CurrentUploads.Clear();

            if (!uploadToken.IsCancellationRequested)
            {
                Logger.Info("Pushing character data for " + character.GetHashCode() + " to " + string.Join(", ", visibleCharacterIds));
                StringBuilder sb = new StringBuilder();
                foreach (var item in character.FileReplacements)
                {
                    sb.AppendLine($"FileReplacements for {item.Key}: {item.Value.Count}");
                }
                foreach (var item in character.GlamourerData)
                {
                    sb.AppendLine($"GlamourerData for {item.Key}: {!string.IsNullOrEmpty(item.Value)}");
                }
                Logger.Debug("Chara data contained: " + Environment.NewLine + sb.ToString());
                await _mareHub!.InvokeAsync(Api.InvokeUserPushCharacterDataToVisibleClients, character, visibleCharacterIds, uploadToken);
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
            await using var db = new FileCacheContext();
            var fileCache = db.FileCaches.First(f => f.Hash == fileHash);
            return (fileHash, LZ4Codec.WrapHC(await File.ReadAllBytesAsync(fileCache.Filepath, uploadToken), 0,
                (int)new FileInfo(fileCache.Filepath).Length));
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
                while ((bytesRead = await ms.ReadAsync(buffer, 0, chunkSize, token)) > 0 && !token.IsCancellationRequested)
                {
                    CurrentUploads.Single(f => f.Hash == fileHash).Transferred += bytesRead;
                    token.ThrowIfCancellationRequested();
                    yield return bytesRead == chunkSize ? buffer.ToArray() : buffer.Take(bytesRead).ToArray();
                }
            }

            await _mareHub!.SendAsync(Api.SendFileUploadFileStreamAsync, fileHash, AsyncFileData(uploadToken), uploadToken);
        }

        public void CancelDownload(int downloadId)
        {
            CurrentDownloads.Remove(downloadId);
        }
    }

}
