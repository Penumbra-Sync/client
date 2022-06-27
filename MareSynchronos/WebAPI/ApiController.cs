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
    public partial class ApiController : IDisposable
    {
#if DEBUG
        public const string MainServer = "darkarchons Debug Server (Dev Server (CH))";
        public const string MainServiceUri = "https://darkarchon.internet-box.ch:5001";
#else
        public const string MainServer = "Lunae Crescere Incipientis (Central Server EU)";
        public const string MainServiceUri = "to be defined";
#endif

        private readonly Configuration _pluginConfiguration;

        private CancellationTokenSource _cts;

        private HubConnection? _fileHub;

        private HubConnection? _heartbeatHub;

        private CancellationTokenSource? _uploadCancellationTokenSource;

        private HubConnection? _userHub;

        public ApiController(Configuration pluginConfiguration)
        {
            Logger.Debug("Creating " + nameof(ApiController));

            _pluginConfiguration = pluginConfiguration;
            _cts = new CancellationTokenSource();

            Task.Run(CreateConnections);
        }

        public event EventHandler? ChangingServers;

        public event EventHandler<CharacterReceivedEventArgs>? CharacterReceived;

        public event EventHandler? Connected;

        public event EventHandler? Disconnected;

        public event EventHandler? PairedClientOffline;

        public event EventHandler? PairedClientOnline;

        public event EventHandler? PairedWithOther;

        public event EventHandler? UnpairedFromOther;

        public ConcurrentDictionary<string, (long, long)> CurrentDownloads { get; } = new();

        public ConcurrentDictionary<string, (long, long)> CurrentUploads { get; } = new();

        public bool IsConnected => !string.IsNullOrEmpty(UID);

        public bool IsDownloading { get; private set; }

        public bool IsUploading { get; private set; }

        public List<ClientPairDto> PairedClients { get; set; } = new();

        public string SecretKey => _pluginConfiguration.ClientSecret.ContainsKey(ApiUri) ? _pluginConfiguration.ClientSecret[ApiUri] : "-";

        public bool ServerAlive =>
            (_heartbeatHub?.State ?? HubConnectionState.Disconnected) == HubConnectionState.Connected;

        public Dictionary<string, string> ServerDictionary => new Dictionary<string, string>() { { MainServiceUri, MainServer } }
            .Concat(_pluginConfiguration.CustomServerList)
            .ToDictionary(k => k.Key, k => k.Value);
        public string UID { get; private set; } = string.Empty;

        private string ApiUri => _pluginConfiguration.ApiUri;

        public async Task CreateConnections()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            await StopAllConnections(token);

            while (!ServerAlive && !token.IsCancellationRequested)
            {
                await StopAllConnections(token);

                try
                {
                    Logger.Debug("Building connection");
                    _heartbeatHub = BuildHubConnection("heartbeat");
                    _userHub = BuildHubConnection("user");
                    _fileHub = BuildHubConnection("files");

                    await _heartbeatHub.StartAsync(token);
                    await _userHub.StartAsync(token);
                    await _fileHub.StartAsync(token);

                    if (_pluginConfiguration.FullPause)
                    {
                        UID = string.Empty;
                        return;
                    }
                    UID = await _heartbeatHub.InvokeAsync<string>("Heartbeat", token);
                    if (!string.IsNullOrEmpty(UID) && !token.IsCancellationRequested) // user is authorized
                    {
                        Logger.Debug("Initializing data");
                        _userHub.On<ClientPairDto, string>("UpdateClientPairs", UpdateLocalClientPairs);
                        _userHub.On<CharacterCacheDto, string>("ReceiveCharacterData", ReceiveCharacterData);
                        _userHub.On<string>("RemoveOnlinePairedPlayer",
                            (s) => PairedClientOffline?.Invoke(s, EventArgs.Empty));
                        _userHub.On<string>("AddOnlinePairedPlayer",
                            (s) => PairedClientOnline?.Invoke(s, EventArgs.Empty));

                        PairedClients = await _userHub!.InvokeAsync<List<ClientPairDto>>("GetPairedClients", token);

                        _heartbeatHub.Closed += HeartbeatHubOnClosed;
                        _heartbeatHub.Reconnected += HeartbeatHubOnReconnected;
                        _heartbeatHub.Reconnecting += HeartbeatHubOnReconnecting;
                        Connected?.Invoke(this, EventArgs.Empty);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex.Message);
                    Logger.Warn(ex.StackTrace);
                    Logger.Debug("Failed to establish connection, retrying");
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
            }
        }

        public void Dispose()
        {
            Logger.Debug("Disposing " + nameof(ApiController));

            Task.Run(async () => await StopAllConnections(_cts.Token));
            _cts?.Cancel();
        }

        private HubConnection BuildHubConnection(string hubName)
        {
            return new HubConnectionBuilder()
                .WithUrl(ApiUri + "/" + hubName, options =>
                {
                    if (!string.IsNullOrEmpty(SecretKey) && !_pluginConfiguration.FullPause)
                    {
                        options.Headers.Add("Authorization", SecretKey);
                    }
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
                .WithAutomaticReconnect(new ForeverRetryPolicy())
                .Build();
        }

        private Task HeartbeatHubOnClosed(Exception? arg)
        {
            Logger.Debug("Connection closed");
            Disconnected?.Invoke(null, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private Task HeartbeatHubOnReconnected(string? arg)
        {
            Logger.Debug("Connection restored");
            Connected?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private Task HeartbeatHubOnReconnecting(Exception? arg)
        {
            Logger.Debug("Connection closed... Reconnecting…");
            Disconnected?.Invoke(null, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private async Task StopAllConnections(CancellationToken token)
        {
            if (_heartbeatHub is { State: HubConnectionState.Connected })
            {
                await _heartbeatHub.StopAsync(token);
                _heartbeatHub.Closed -= HeartbeatHubOnClosed;
                _heartbeatHub.Reconnected -= HeartbeatHubOnReconnected;
                _heartbeatHub.Reconnecting += HeartbeatHubOnReconnecting;
                await _heartbeatHub.DisposeAsync();
            }

            if (_fileHub is { State: HubConnectionState.Connected })
            {
                await _fileHub.StopAsync(token);
                await _fileHub.DisposeAsync();
            }

            if (_userHub is { State: HubConnectionState.Connected })
            {
                await _userHub.StopAsync(token);
                await _userHub.DisposeAsync();
            }
        }
    }

    public partial class ApiController
    {
        public void CancelUpload()
        {
            if (_uploadCancellationTokenSource != null)
            {
                PluginLog.Warning("Cancelling upload");
                _uploadCancellationTokenSource?.Cancel();
                _fileHub!.InvokeAsync("AbortUpload");
            }
        }

        public async Task DeleteAccount()
        {
            _pluginConfiguration.ClientSecret.Remove(ApiUri);
            await _fileHub!.SendAsync("DeleteAllFiles");
            await _userHub!.SendAsync("DeleteAccount");
            await CreateConnections();
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
                CurrentDownloads[hash] = (CurrentDownloads[hash].Item1 + data.Length, CurrentDownloads[hash].Item2);
                await fs.WriteAsync(data, ct);
            }
            return fileName;
        }

        public async Task DownloadFiles(List<FileReplacementDto> fileReplacementDto, CancellationToken ct)
        {
            IsDownloading = true;

            foreach (var file in fileReplacementDto)
            {
                var fileSize = await _fileHub!.InvokeAsync<long>("GetFileSize", file.Hash, ct);
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
                var tempFile = await DownloadFile(hash, ct);
                if (ct.IsCancellationRequested)
                {
                    File.Delete(tempFile);
                    break;
                }

                var tempFileData = await File.ReadAllBytesAsync(tempFile, ct);
                var extractedFile = LZ4Codec.Unwrap(tempFileData);
                File.Delete(tempFile);
                var ext = file.GamePaths.First().Split(".").Last();
                var filePath = Path.Combine(_pluginConfiguration.CacheFolder, file.Hash + "." + ext);
                await File.WriteAllBytesAsync(filePath, extractedFile, ct);
                Logger.Debug("File downloaded to " + filePath);
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

        public async Task GetCharacterData(Dictionary<string, int> hashedCharacterNames)
        {
            await _userHub!.InvokeAsync("GetCharacterData",
                hashedCharacterNames);
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

        private async Task<(string, byte[])> GetCompressedFileData(string fileHash, CancellationToken uploadToken)
        {
            await using var db = new FileCacheContext();
            var fileCache = db.FileCaches.First(f => f.Hash == fileHash);
            return (fileHash, LZ4Codec.WrapHC(await File.ReadAllBytesAsync(fileCache.Filepath, uploadToken), 0,
                (int)new FileInfo(fileCache.Filepath).Length));
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

    public class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            return TimeSpan.FromSeconds(5);
        }
    }
}
