using Dalamud.Configuration;
using Dalamud.Logging;
using MareSynchronos.Models;
using System;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Types;
using LZ4;
using MareSynchronos.API;
using MareSynchronos.FileCacheDB;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI
{
    public class CharacterReceivedEventArgs : EventArgs
    {
        public CharacterCacheDto CharacterData { get; set; }
        public string CharacterNameHash { get; set; }
    }

    public class ApiController : IDisposable
    {
        private readonly Configuration pluginConfiguration;
        private const string MainService = "https://darkarchon.internet-box.ch:5001";
        public string UID { get; private set; } = string.Empty;
        public string SecretKey => pluginConfiguration.ClientSecret.ContainsKey(ApiUri) ? pluginConfiguration.ClientSecret[ApiUri] : "-";
        private string CacheFolder => pluginConfiguration.CacheFolder;
        public bool UseCustomService
        {
            get => pluginConfiguration.UseCustomService;
            set
            {
                pluginConfiguration.UseCustomService = value;
                pluginConfiguration.Save();
            }
        }
        private string ApiUri => UseCustomService ? pluginConfiguration.ApiUri : MainService;

        public bool ServerAlive =>
            (_heartbeatHub?.State ?? HubConnectionState.Disconnected) == HubConnectionState.Connected;
        public bool IsConnected => !string.IsNullOrEmpty(UID);

        public event EventHandler? Connected;
        public event EventHandler? Disconnected;
        public event EventHandler<CharacterReceivedEventArgs>? CharacterReceived;
        public event EventHandler? RemovedFromWhitelist;
        public event EventHandler? AddedToWhitelist;

        public List<WhitelistDto> WhitelistEntries { get; set; } = new List<WhitelistDto>();

        readonly CancellationTokenSource cts;
        private HubConnection? _heartbeatHub;
        private IDisposable? _fileUploadRequest;
        private HubConnection? _fileHub;
        private HubConnection? _userHub;

        public ApiController(Configuration pluginConfiguration)
        {
            this.pluginConfiguration = pluginConfiguration;
            cts = new CancellationTokenSource();

            _ = Heartbeat();
        }

        public async Task Heartbeat()
        {
            while (!ServerAlive && !cts.Token.IsCancellationRequested)
            {
                try
                {
                    PluginLog.Debug("Attempting to establish heartbeat connection to " + ApiUri);
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
                                        (sender, certificate, chain, sslPolicyErrors) => true;
                                return message;
                            };
#endif
                        }).Build();
                    PluginLog.Debug("Heartbeat service built to: " + ApiUri);

                    await _heartbeatHub.StartAsync(cts.Token);
                    UID = await _heartbeatHub!.InvokeAsync<string>("Heartbeat");
                    PluginLog.Debug("Heartbeat started: " + ApiUri);
                    try
                    {
                        await InitializeHubConnections();
                        await LoadInitialData();
                        Connected?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error(ex, "Error during Heartbeat initialization");
                    }

                    _heartbeatHub.Closed += OnHeartbeatHubOnClosed;
                    _heartbeatHub.Reconnected += OnHeartbeatHubOnReconnected;
                    PluginLog.Debug("Heartbeat established to: " + ApiUri);
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Creating heartbeat failure");
                }
            }
        }

        private async Task LoadInitialData()
        {
            var whiteList = await _userHub!.InvokeAsync<List<WhitelistDto>>("GetWhitelist");
            WhitelistEntries = whiteList.ToList();
        }

        public void RestartHeartbeat()
        {
            PluginLog.Debug("Restarting heartbeat");

            _heartbeatHub!.Closed -= OnHeartbeatHubOnClosed;
            _heartbeatHub!.Reconnected -= OnHeartbeatHubOnReconnected;
            Task.Run(async () =>
            {
                await _heartbeatHub.StopAsync(cts.Token);
                await _heartbeatHub.DisposeAsync();
                _heartbeatHub = null!;
                _ = Heartbeat();
            });
        }

        private async Task OnHeartbeatHubOnReconnected(string? s)
        {
            PluginLog.Debug("Reconnected: " + ApiUri);
            UID = await _heartbeatHub!.InvokeAsync<string>("Heartbeat");
        }

        private Task OnHeartbeatHubOnClosed(Exception? exception)
        {
            PluginLog.Debug("Connection closed: " + ApiUri);
            Disconnected?.Invoke(null, EventArgs.Empty);
            RestartHeartbeat();
            return Task.CompletedTask;
        }

        private async Task DisposeHubConnections()
        {
            if (_fileHub != null)
            {
                PluginLog.Debug("Disposing File Hub");
                _fileUploadRequest?.Dispose();
                await _fileHub!.StopAsync();
                await _fileHub!.DisposeAsync();
            }

            if (_userHub != null)
            {
                PluginLog.Debug("Disposing User Hub");
                await _userHub.StopAsync();
                await _userHub.DisposeAsync();
            }
        }

        private async Task InitializeHubConnections()
        {
            await DisposeHubConnections();

            PluginLog.Debug("Creating User Hub");
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
            _userHub.On<WhitelistDto, string>("UpdateWhitelist", UpdateLocalWhitelist);
            _userHub.On<CharacterCacheDto, string>("ReceiveCharacterData", ReceiveCharacterData);

            PluginLog.Debug("Creating File Hub");
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
            await _fileHub.StartAsync(cts.Token);

            _fileUploadRequest = _fileHub!.On<string>("FileRequest", UploadFile);
        }

        private void UpdateLocalWhitelist(WhitelistDto dto, string characterIdentifier)
        {
            var entry = WhitelistEntries.SingleOrDefault(e => e.OtherUID == dto.OtherUID);
            if (entry == null)
            {
                RemovedFromWhitelist?.Invoke(characterIdentifier, EventArgs.Empty);
                return;
            }

            if ((entry.IsPausedFromOthers != dto.IsPausedFromOthers || entry.IsSynced != dto.IsSynced || entry.IsPaused != dto.IsPaused)
                && !dto.IsPaused && dto.IsSynced && !dto.IsPausedFromOthers)
            {
                AddedToWhitelist?.Invoke(characterIdentifier, EventArgs.Empty);
            }

            entry.IsPaused = dto.IsPaused;
            entry.IsPausedFromOthers = dto.IsPausedFromOthers;
            entry.IsSynced = dto.IsSynced;

            if (dto.IsPaused || dto.IsPausedFromOthers || !dto.IsSynced)
            {
                RemovedFromWhitelist?.Invoke(characterIdentifier, EventArgs.Empty);
            }
        }

        private async Task UploadFile(string fileHash)
        {
            PluginLog.Debug("Requested fileHash: " + fileHash);

            await using var db = new FileCacheContext();
            var fileCache = db.FileCaches.First(f => f.Hash == fileHash);
            var compressedFile = LZ4Codec.WrapHC(await File.ReadAllBytesAsync(fileCache.Filepath), 0,
                (int)new FileInfo(fileCache.Filepath).Length);
            var response = await _fileHub!.InvokeAsync<bool>("UploadFile", fileHash, compressedFile, cts.Token);
            PluginLog.Debug("Success: " + response);
        }

        public async Task Register()
        {
            if (!ServerAlive) return;
            PluginLog.Debug("Registering at service " + ApiUri);
            var response = await _userHub!.InvokeAsync<string>("Register");
            pluginConfiguration.ClientSecret[ApiUri] = response;
            pluginConfiguration.Save();
            RestartHeartbeat();
        }

        public async Task SendCharacterData(CharacterCacheDto character, List<string> visibleCharacterIds)
        {
            if (!IsConnected || SecretKey == "-") return;
            PluginLog.Debug("Sending Character data to service " + ApiUri);

            await _fileHub!.InvokeAsync("SendFiles", character.FileReplacements, cts.Token);

            while (await _fileHub!.InvokeAsync<bool>("IsUploadFinished"))
            {
                await Task.Delay(TimeSpan.FromSeconds(0.5));
                PluginLog.Debug("Waiting for uploads to finish");
            }

            await _userHub!.InvokeAsync("PushCharacterData", character, visibleCharacterIds);
        }

        public Task ReceiveCharacterData(CharacterCacheDto character, string characterHash)
        {
            PluginLog.Debug("Received DTO for " + characterHash);
            CharacterReceived?.Invoke(null, new CharacterReceivedEventArgs()
            {
                CharacterData = character,
                CharacterNameHash = characterHash
            });
            return Task.CompletedTask;
        }

        public async Task<byte[]> DownloadData(string hash)
        {
            return await _fileHub!.InvokeAsync<byte[]>("DownloadFile", hash);
        }

        public async Task GetCharacterData(Dictionary<string, int> hashedCharacterNames)
        {
            await _userHub!.InvokeAsync("GetCharacterData",
                hashedCharacterNames);
        }

        public async Task SendWhitelistPauseChange(string uid, bool paused)
        {
            if (!IsConnected || SecretKey == "-") return;
            await _userHub!.SendAsync("SendWhitelistPauseChange", uid, paused);
        }

        public async Task SendWhitelistAddition(string uid)
        {
            if (!IsConnected || SecretKey == "-") return;
            await _userHub!.SendAsync("SendWhitelistAddition", uid);
        }

        public async Task SendWhitelistRemoval(string uid)
        {
            if (!IsConnected || SecretKey == "-") return;
            await _userHub!.SendAsync("SendWhitelistRemoval", uid);
        }

        public async Task SendWhitelist()
        {
            if (!IsConnected || SecretKey == "-") return;
            await _userHub!.SendAsync("SendWhitelist", WhitelistEntries.ToList());
            WhitelistEntries = (await _userHub!.InvokeAsync<List<WhitelistDto>>("GetWhitelist")).ToList();
        }

        public async Task<List<string>> GetWhitelist()
        {
            PluginLog.Debug("Getting whitelist from service " + ApiUri);

            List<string> whitelist = new();
            return whitelist;
        }

        public void Dispose()
        {
            cts?.Cancel();
            _ = DisposeHubConnections();
        }

        public async Task SendCharacterName(string hashedName)
        {
            await _userHub!.SendAsync("SendCharacterNameHash", hashedName);
        }

        public async Task SendVisibilityData(List<string> visibilities)
        {
            if (!IsConnected) return;
            await _userHub!.SendAsync("SendVisibilityList", visibilities);
        }
    }
}
