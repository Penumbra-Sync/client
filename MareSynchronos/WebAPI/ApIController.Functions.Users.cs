using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MareSynchronos.API;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI
{
    public partial class ApiController
    {
        public async Task DeleteAccount()
        {
            _pluginConfiguration.ClientSecret.Remove(ApiUri);
            _pluginConfiguration.Save();
            await _fileHub!.SendAsync("DeleteAllFiles");
            await _userHub!.SendAsync("DeleteAccount");
            await CreateConnections();
        }

        public async Task GetCharacterData(Dictionary<string, int> hashedCharacterNames)
        {
            await _userHub!.InvokeAsync("GetCharacterData",
                hashedCharacterNames);
        }

        public async Task Register()
        {
            if (!ServerAlive) return;
            Logger.Debug("Registering at service " + ApiUri);
            var response = await _userHub!.InvokeAsync<string>("Register");
            _pluginConfiguration.ClientSecret[ApiUri] = response;
            _pluginConfiguration.Save();
            ChangingServers?.Invoke(null, EventArgs.Empty);
            await CreateConnections();
        }

        public async Task SendCharacterData(CharacterCacheDto character, List<string> visibleCharacterIds)
        {
            if (!IsConnected || SecretKey == "-") return;
            Logger.Debug("Sending Character data to service " + ApiUri);

            CancelUpload();
            _uploadCancellationTokenSource = new CancellationTokenSource();
            var uploadToken = _uploadCancellationTokenSource.Token;
            Logger.Verbose("New Token Created");

            var filesToUpload = await _fileHub!.InvokeAsync<List<UploadFileDto>>("SendFiles", character.FileReplacements.Select(c => c.Hash).Distinct(), uploadToken);

            IsUploading = true;

            foreach (var file in filesToUpload.Where(f => f.IsForbidden == false))
            {
                await using var db = new FileCacheContext();
                CurrentUploads.Add(new FileTransfer()
                {
                    Hash = file.Hash,
                    Total = new FileInfo(db.FileCaches.First(f => f.Hash == file.Hash).Filepath).Length
                });
            }

            Logger.Verbose("Compressing and uploading files");
            foreach (var file in filesToUpload)
            {
                Logger.Verbose("Compressing and uploading " + file);
                var data = await GetCompressedFileData(file.Hash, uploadToken);
                CurrentUploads.Single(e => e.Hash == data.Item1).Total = data.Item2.Length;
                _ = UploadFile(data.Item2, file.Hash, uploadToken);
                if (!uploadToken.IsCancellationRequested) continue;
                Logger.Warn("Cancel in filesToUpload loop detected");
                CurrentUploads.Clear();
                break;
            }

            Logger.Verbose("Upload tasks complete, waiting for server to confirm");
            var anyUploadsOpen = await _fileHub!.InvokeAsync<bool>("IsUploadFinished", uploadToken);
            Logger.Verbose("Uploads open: " + anyUploadsOpen);
            while (anyUploadsOpen && !uploadToken.IsCancellationRequested)
            {
                anyUploadsOpen = await _fileHub!.InvokeAsync<bool>("IsUploadFinished", uploadToken);
                await Task.Delay(TimeSpan.FromSeconds(0.5), uploadToken);
                Logger.Verbose("Waiting for uploads to finish");
            }

            CurrentUploads.Clear();
            IsUploading = false;

            if (!uploadToken.IsCancellationRequested)
            {
                Logger.Verbose("=== Pushing character data ===");
                await _userHub!.InvokeAsync("PushCharacterData", character, visibleCharacterIds, uploadToken);
            }
            else
            {
                Logger.Warn("=== Upload operation was cancelled ===");
            }

            Logger.Verbose("Upload complete for " + character.Hash);
            _uploadCancellationTokenSource = null;
        }

        public async Task<List<string>> GetOnlineCharacters()
        {
            return await _userHub!.InvokeAsync<List<string>>("GetOnlineCharacters");
        }

        public async Task SendPairedClientAddition(string uid)
        {
            if (!IsConnected || SecretKey == "-") return;
            await _userHub!.SendAsync("SendPairedClientAddition", uid);
        }

        public async Task SendPairedClientPauseChange(string uid, bool paused)
        {
            if (!IsConnected || SecretKey == "-") return;
            await _userHub!.SendAsync("SendPairedClientPauseChange", uid, paused);
        }

        public async Task SendPairedClientRemoval(string uid)
        {
            if (!IsConnected || SecretKey == "-") return;
            await _userHub!.SendAsync("SendPairedClientRemoval", uid);
        }
    }

}
