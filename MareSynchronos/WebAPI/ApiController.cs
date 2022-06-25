using Dalamud.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LZ4;
using MareSynchronos.API;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Utils;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI
{
    public class ApiController : IDisposable
    {
        public const string MainServer = "Lunae Crescere Incipientis (Central Server EU)";

        public const string MainServiceUri = "https://darkarchon.internet-box.ch:5001";
        readonly CancellationTokenSource _cts;
        private readonly Configuration _pluginConfiguration;
        private HubConnection? _fileHub;
        private HubConnection? _heartbeatHub;
        private CancellationTokenSource? _uploadCancellationTokenSource;
        private HubConnection? _userHub;
        public ApiController(Configuration pluginConfiguration)
        {
            Logger.Debug("Creating " + nameof(ApiController));

            _pluginConfiguration = pluginConfiguration;
            _cts = new CancellationTokenSource();

            _ = Heartbeat();
        }

        public event EventHandler<CharacterReceivedEventArgs>? CharacterReceived;

        public event EventHandler? Connected;

        public event EventHandler? Disconnected;

        public event EventHandler? PairedClientOffline;

        public event EventHandler? PairedClientOnline;

        public event EventHandler? PairedWithOther;

        public event EventHandler? UnpairedFromOther;
        public event EventHandler? AccountDeleted;

        public ConcurrentDictionary<string, (long, long)> CurrentDownloads { get; } = new();
        public ConcurrentDictionary<string, (long, long)> CurrentUploads { get; } = new();
        public bool IsConnected => !string.IsNullOrEmpty(UID);
        public bool IsDownloading { get; private set; } = false;
        public bool IsUploading { get; private set; } = false;
        public List<ClientPairDto> PairedClients { get; set; } = new();
        public string SecretKey => _pluginConfiguration.ClientSecret.ContainsKey(ApiUri) ? _pluginConfiguration.ClientSecret[ApiUri] : "-";
        public bool ServerAlive =>
            (_heartbeatHub?.State ?? HubConnectionState.Disconnected) == HubConnectionState.Connected;

        public string UID { get; private set; } = string.Empty;
        public bool UseCustomService
        {
            get => _pluginConfiguration.UseCustomService;
            set
            {
                _pluginConfiguration.UseCustomService = value;
                _pluginConfiguration.Save();
            }
        }

        private string ApiUri => UseCustomService ? _pluginConfiguration.ApiUri : MainServiceUri;
        private string CacheFolder => _pluginConfiguration.CacheFolder;
        public void CancelUpload()
        {
            if (_uploadCancellationTokenSource != null)
            {
                PluginLog.Warning("Cancelling upload");
                _uploadCancellationTokenSource?.Cancel();
                _fileHub!.InvokeAsync("AbortUpload");
            }
        }

        public void Dispose()
        {
            Logger.Debug("Disposing " + nameof(ApiController));

            _cts?.Cancel();
            _ = DisposeHubConnections();
        }

        public async Task<byte[]> DownloadFile(string hash)
        {
            IsDownloading = true;
            var reader = await _fileHub!.StreamAsChannelAsync<byte[]>("DownloadFile", hash);
            List<byte> downloadedData = new();
            int i = 0;
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out var data))
                {
                    CurrentDownloads[hash] = (CurrentDownloads[hash].Item1 + data.Length, CurrentDownloads[hash].Item2);
                    downloadedData.AddRange(data);
                }
            }

            IsDownloading = false;
            return downloadedData.ToArray();
        }

        public async Task DownloadFiles(List<FileReplacementDto> fileReplacementDto)
        {
            foreach (var file in fileReplacementDto)
            {
                var fileSize = await _fileHub!.InvokeAsync<long>("GetFileSize", file.Hash);
                CurrentDownloads[file.Hash] = (0, fileSize);
            }

            List<string> downloadedHashes = new();
            foreach (var file in fileReplacementDto.Where(f => CurrentDownloads[f.Hash].Item2 > 0))
            {
                if (downloadedHashes.Contains(file.Hash))
                {
                    continue;
                }
                var hash = file.Hash;
                var data = await DownloadFile(hash);
                var extractedFile = LZ4Codec.Unwrap(data);
                var ext = file.GamePaths.First().Split(".").Last();
                var filePath = Path.Combine(_pluginConfiguration.CacheFolder, file.Hash + "." + ext);
                await File.WriteAllBytesAsync(filePath, extractedFile);
                Logger.Debug("File downloaded to " + filePath);
                downloadedHashes.Add(hash);
            }

            bool allFilesInDb = false;
            while (!allFilesInDb)
            {
                await using (var db = new FileCacheContext())
                {
                    allFilesInDb = downloadedHashes.All(h => db.FileCaches.Any(f => f.Hash == h));
                }

                await Task.Delay(250);
            }

            CurrentDownloads.Clear();
        }

        public async Task GetCharacterData(Dictionary<string, int> hashedCharacterNames)
        {
            await _userHub!.InvokeAsync("GetCharacterData",
                hashedCharacterNames);
        }

        public async Task Heartbeat()
        {
            while (!ServerAlive && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    Logger.Debug("Attempting to establish heartbeat connection to " + ApiUri);
                    _heartbeatHub = new HubConnectionBuilder()
                        .WithUrl(ApiUri + "/heartbeat", options =>
                        {
                            if (!string.IsNullOrEmpty(SecretKey))
                            {
                                options.Headers.Add("Authorization", SecretKey);
                            }

#if DEBUG
                            options.HttpMessageHandlerFactory = (message) =>
                            {
                                if (message is HttpClientHandler clientHandler)
                                    clientHandler.ServerCertificateCustomValidationCallback +=
                                        (_, _, _, _) => true;
                                return message;
                            };
#endif
                        }).Build();

                    await _heartbeatHub.StartAsync(_cts.Token);
                    UID = await _heartbeatHub!.InvokeAsync<string>("Heartbeat");
                    Logger.Debug("Heartbeat started: " + ApiUri);
                    try
                    {
                        await InitializeHubConnections();
                        await LoadInitialData();
                        Connected?.Invoke(this, EventArgs.Empty);
                    }
                    catch
                    {
                        //PluginLog.Error(ex, "Error during Heartbeat initialization");
                    }

                    _heartbeatHub.Closed += OnHeartbeatHubOnClosed;
                    _heartbeatHub.Reconnected += OnHeartbeatHubOnReconnected;
                    Logger.Debug("Heartbeat established to: " + ApiUri);
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Creating heartbeat failure");
                }
            }
        }

        public Task ReceiveCharacterData(CharacterCacheDto character, string characterHash)
        {
            Logger.Debug("Received DTO for " + characterHash);
            CharacterReceived?.Invoke(null, new CharacterReceivedEventArgs(characterHash, character));
            return Task.CompletedTask;
        }

        public async Task Register()
        {
            if (!ServerAlive) return;
            Logger.Debug("Registering at service " + ApiUri);
            var response = await _userHub!.InvokeAsync<string>("Register");
            _pluginConfiguration.ClientSecret[ApiUri] = response;
            _pluginConfiguration.Save();
            RestartHeartbeat();
        }

        public void RestartHeartbeat()
        {
            Logger.Debug("Restarting heartbeat");

            _heartbeatHub!.Closed -= OnHeartbeatHubOnClosed;
            _heartbeatHub!.Reconnected -= OnHeartbeatHubOnReconnected;
            Task.Run(async () =>
            {
                await _heartbeatHub.StopAsync(_cts.Token);
                await _heartbeatHub.DisposeAsync();
                _heartbeatHub = null!;
                _ = Heartbeat();
            });
        }

        public async Task SendCharacterData(CharacterCacheDto character, List<string> visibleCharacterIds)
        {
            if (!IsConnected || SecretKey == "-") return;
            Logger.Debug("Sending Character data to service " + ApiUri);

            CancelUpload();
            _uploadCancellationTokenSource = new CancellationTokenSource();
            var uploadToken = _uploadCancellationTokenSource.Token;
            Logger.Debug("New Token Created");

            var filesToUpload = await _fileHub!.InvokeAsync<List<string>>("SendFiles", character.FileReplacements.Select(c => c.Hash).Distinct(), uploadToken);

            IsUploading = true;

            Logger.Debug("Compressing files");
            Dictionary<string, byte[]> compressedFileData = new();
            foreach (var file in filesToUpload)
            {
                Logger.Debug(file);
                var data = await GetCompressedFileData(file, uploadToken);
                compressedFileData.Add(data.Item1, data.Item2);
                CurrentUploads[data.Item1] = (0, data.Item2.Length);
            }
            Logger.Debug("Files compressed, uploading files");
            foreach (var data in compressedFileData)
            {
                await UploadFile(data.Value, data.Key, uploadToken);
                if (uploadToken.IsCancellationRequested)
                {
                    PluginLog.Warning("Cancel in filesToUpload loop detected");
                    CurrentUploads.Clear();
                    break;
                }
            }
            Logger.Debug("Upload tasks complete, waiting for server to confirm");
            var anyUploadsOpen = await _fileHub!.InvokeAsync<bool>("IsUploadFinished", uploadToken);
            Logger.Debug("Uploads open: " + anyUploadsOpen);
            while (anyUploadsOpen && !uploadToken.IsCancellationRequested)
            {
                anyUploadsOpen = await _fileHub!.InvokeAsync<bool>("IsUploadFinished", uploadToken);
                await Task.Delay(TimeSpan.FromSeconds(0.5), uploadToken);
                Logger.Debug("Waiting for uploads to finish");
            }

            CurrentUploads.Clear();
            IsUploading = false;

            if (!uploadToken.IsCancellationRequested)
            {
                Logger.Debug("=== Pushing character data ===");
                await _userHub!.InvokeAsync("PushCharacterData", character, visibleCharacterIds, uploadToken);
            }
            else
            {
                PluginLog.Warning("=== Upload operation was cancelled ===");
            }

            Logger.Debug("== Upload complete for " + character.JobId);
            _uploadCancellationTokenSource = null;
        }

        public async Task<List<string>> SendCharacterName(string hashedName)
        {
            return await _userHub!.InvokeAsync<List<string>>("SendCharacterNameHash", hashedName);
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

        public async Task UpdateCurrentDownloadSize(string hash)
        {
            long fileSize = await _fileHub!.InvokeAsync<long>("GetFileSize", hash);
        }

        public async Task DeleteAllMyFiles()
        {
            await _fileHub!.SendAsync("DeleteAllFiles");
        }

        public async Task DeleteAccount()
        {
            _pluginConfiguration.ClientSecret.Remove(ApiUri);
            await _fileHub!.SendAsync("DeleteAllFiles");
            await _userHub!.SendAsync("DeleteAccount");
            _ = OnHeartbeatHubOnClosed(null);
            AccountDeleted?.Invoke(null, EventArgs.Empty);
        }

        private async Task DisposeHubConnections()
        {
            if (_fileHub != null)
            {
                Logger.Debug("Disposing File Hub");
                CancelUpload();
                await _fileHub!.StopAsync();
                await _fileHub!.DisposeAsync();
            }

            if (_userHub != null)
            {
                Logger.Debug("Disposing User Hub");
                await _userHub.StopAsync();
                await _userHub.DisposeAsync();
            }
        }

        private async Task<(string, byte[])> GetCompressedFileData(string fileHash, CancellationToken uploadToken)
        {
            await using var db = new FileCacheContext();
            var fileCache = db.FileCaches.First(f => f.Hash == fileHash);
            return (fileHash, LZ4Codec.WrapHC(await File.ReadAllBytesAsync(fileCache.Filepath, uploadToken), 0,
                (int)new FileInfo(fileCache.Filepath).Length));
        }

        private async Task InitializeHubConnections()
        {
            await DisposeHubConnections();

            Logger.Debug("Creating User Hub");
            _userHub = new HubConnectionBuilder()
                .WithUrl(ApiUri + "/user", options =>
                {
                    options.Headers.Add("Authorization", SecretKey);
#if DEBUG
                    options.HttpMessageHandlerFactory = (message) =>
                    {
                        if (message is HttpClientHandler clientHandler)
                            clientHandler.ServerCertificateCustomValidationCallback +=
                                (sender, certificate, chain, sslPolicyErrors) => true;
                        return message;
                    };
#endif
                })
                .Build();
            await _userHub.StartAsync();
            _userHub.On<ClientPairDto, string>("UpdateClientPairs", UpdateLocalClientPairs);
            _userHub.On<CharacterCacheDto, string>("ReceiveCharacterData", ReceiveCharacterData);
            _userHub.On<string>("RemoveOnlinePairedPlayer", (s) => PairedClientOffline?.Invoke(s, EventArgs.Empty));
            _userHub.On<string>("AddOnlinePairedPlayer", (s) => PairedClientOnline?.Invoke(s, EventArgs.Empty));

            Logger.Debug("Creating File Hub");
            _fileHub = new HubConnectionBuilder()
                .WithUrl(ApiUri + "/files", options =>
                {
                    options.Headers.Add("Authorization", SecretKey);
#if DEBUG
                    options.HttpMessageHandlerFactory = (message) =>
                    {
                        if (message is HttpClientHandler clientHandler)
                            clientHandler.ServerCertificateCustomValidationCallback +=
                                (sender, certificate, chain, sslPolicyErrors) => true;
                        return message;
                    };
#endif
                })
                .Build();
            await _fileHub.StartAsync(_cts.Token);
        }

        private async Task LoadInitialData()
        {
            var pairedClients = await _userHub!.InvokeAsync<List<ClientPairDto>>("GetPairedClients");
            PairedClients = pairedClients.ToList();
        }

        private Task OnHeartbeatHubOnClosed(Exception? exception)
        {
            Logger.Debug("Connection closed: " + ApiUri);
            Disconnected?.Invoke(null, EventArgs.Empty);
            RestartHeartbeat();
            return Task.CompletedTask;
        }

        private async Task OnHeartbeatHubOnReconnected(string? s)
        {
            Logger.Debug("Reconnected: " + ApiUri);
            UID = await _heartbeatHub!.InvokeAsync<string>("Heartbeat");
        }

        private void UpdateLocalClientPairs(ClientPairDto dto, string characterIdentifier)
        {
            var entry = PairedClients.SingleOrDefault(e => e.OtherUID == dto.OtherUID);
            if (dto.IsRemoved)
            {
                PairedClients.RemoveAll(p => p.OtherUID == dto.OtherUID);
                UnpairedFromOther?.Invoke(characterIdentifier, EventArgs.Empty);
                return;
            }
            if (entry == null)
            {
                PairedClients.Add(dto);
                return;
            }

            if ((entry.IsPausedFromOthers != dto.IsPausedFromOthers || entry.IsSynced != dto.IsSynced || entry.IsPaused != dto.IsPaused)
                && !dto.IsPaused && dto.IsSynced && !dto.IsPausedFromOthers)
            {
                PairedWithOther?.Invoke(characterIdentifier, EventArgs.Empty);
            }

            entry.IsPaused = dto.IsPaused;
            entry.IsPausedFromOthers = dto.IsPausedFromOthers;
            entry.IsSynced = dto.IsSynced;

            if (dto.IsPaused || dto.IsPausedFromOthers || !dto.IsSynced)
            {
                UnpairedFromOther?.Invoke(characterIdentifier, EventArgs.Empty);
            }
        }

        private async Task UploadFile(byte[] compressedFile, string fileHash, CancellationToken uploadToken)
        {
            if (uploadToken.IsCancellationRequested) return;
            var chunkSize = 1024 * 512; // 512kb
            var chunks = (int)Math.Ceiling(compressedFile.Length / (double)chunkSize);
            var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(chunkSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = true
            });
            await _fileHub!.SendAsync("UploadFile", fileHash, channel.Reader, uploadToken);
            for (var i = 0; i < chunks; i++)
            {
                var uploadChunk = compressedFile.Skip(i * chunkSize).Take(chunkSize).ToArray();
                channel.Writer.TryWrite(uploadChunk);
                CurrentUploads[fileHash] = (CurrentUploads[fileHash].Item1 + uploadChunk.Length, CurrentUploads[fileHash].Item2);
                if (uploadToken.IsCancellationRequested) break;
            }

            channel.Writer.Complete();
        }
    }

    public class CharacterReceivedEventArgs : EventArgs
    {
        public CharacterReceivedEventArgs(string characterNameHash, CharacterCacheDto characterData)
        {
            CharacterData = characterData;
            CharacterNameHash = characterNameHash;
        }

        public CharacterCacheDto CharacterData { get; set; }
        public string CharacterNameHash { get; set; }
    }
}
