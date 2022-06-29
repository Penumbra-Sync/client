using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
        public void CancelUpload()
        {
            if (_uploadCancellationTokenSource != null)
            {
                Logger.Warn("Cancelling upload");
                _uploadCancellationTokenSource?.Cancel();
                _fileHub!.InvokeAsync("AbortUpload");
            }
        }

        public async Task DeleteAllMyFiles()
        {
            await _fileHub!.SendAsync("DeleteAllFiles");
        }

        public async Task<string> DownloadFile(string hash, CancellationToken ct)
        {
            var reader = _fileHub!.StreamAsync<byte[]>("DownloadFileAsync", hash, ct);
            string fileName = Path.GetTempFileName();
            await using var fs = File.OpenWrite(fileName);
            await foreach (var data in reader.WithCancellation(ct))
            {
                //Logger.Debug("Getting chunk of " + hash);
                CurrentDownloads.Single(f => f.Hash == hash).Transferred += data.Length;
                await fs.WriteAsync(data, ct);
                Debug.WriteLine("Wrote chunk " + data.Length + " into " + fileName);
            }
            return fileName;
        }

        public async Task DownloadFiles(List<FileReplacementDto> fileReplacementDto, CancellationToken ct)
        {
            IsDownloading = true;

            foreach (var file in fileReplacementDto)
            {
                var downloadFileDto = await _fileHub!.InvokeAsync<DownloadFileDto>("GetFileSize", file.Hash, ct);
                CurrentDownloads.Add(new FileTransfer
                {
                    Total = downloadFileDto.Size,
                    Hash = downloadFileDto.Hash
                });
            }

            List<string> downloadedHashes = new();
            foreach (var file in fileReplacementDto.Where(f => CurrentDownloads.Single(t => f.Hash == t.Hash).Transferred > 0))
            {
                if (downloadedHashes.Contains(file.Hash))
                {
                    continue;
                }

                var hash = file.Hash;
                var tempFile = await DownloadFile(hash, ct);
                if (ct.IsCancellationRequested)
                {
                    File.Delete(tempFile);
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
                downloadedHashes.Add(hash);
            }

            var allFilesInDb = false;
            while (!allFilesInDb && !ct.IsCancellationRequested)
            {
                await using (var db = new FileCacheContext())
                {
                    allFilesInDb = downloadedHashes.All(h => db.FileCaches.Any(f => f.Hash == h));
                }

                await Task.Delay(250, ct);
            }

            CurrentDownloads.Clear();
            IsDownloading = false;
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

            async IAsyncEnumerable<byte[]> AsyncFileData()
            {
                var chunkSize = 1024 * 512; // 512kb
                using var ms = new MemoryStream(compressedFile);
                var buffer = new byte[chunkSize];
                int bytesRead;
                while ((bytesRead = await ms.ReadAsync(buffer, 0, chunkSize, uploadToken)) > 0)
                {
                    CurrentUploads.Single(f => f.Hash == fileHash).Transferred += bytesRead;
                    uploadToken.ThrowIfCancellationRequested();
                    yield return bytesRead == chunkSize ? buffer.ToArray() : buffer.Take(bytesRead).ToArray();
                }
            }

            await _fileHub!.SendAsync("UploadFileStreamAsync", fileHash, AsyncFileData(), uploadToken);
        }
    }

}
