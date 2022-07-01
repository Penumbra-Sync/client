using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MareSynchronos.API;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI
{
    public partial class ApiController : IDisposable
    {
#if DEBUG
        public const string MainServer = "darkarchons Debug Server (Dev Server (CH))";
        public const string MainServiceUri = "wss://darkarchon.internet-box.ch:5000";
#else
        public const string MainServer = "Lunae Crescere Incipientis (Central Server EU)";
        public const string MainServiceUri = "to be defined";
#endif

        private readonly Configuration _pluginConfiguration;
        private readonly DalamudUtil _dalamudUtil;

        private CancellationTokenSource _cts;

        private HubConnection? _fileHub;

        private HubConnection? _heartbeatHub;

        private HubConnection? _adminHub;

        private CancellationTokenSource? _uploadCancellationTokenSource;

        private HubConnection? _userHub;
        private LoggedInUserDto? _loggedInUser;
        public bool IsModerator => (_loggedInUser?.IsAdmin ?? false) || (_loggedInUser?.IsModerator ?? false);

        public bool IsAdmin => _loggedInUser?.IsAdmin ?? false;

        public ApiController(Configuration pluginConfiguration, DalamudUtil dalamudUtil)
        {
            Logger.Debug("Creating " + nameof(ApiController));

            _pluginConfiguration = pluginConfiguration;
            _dalamudUtil = dalamudUtil;
            _cts = new CancellationTokenSource();
            _dalamudUtil.LogIn += DalamudUtilOnLogIn;
            _dalamudUtil.LogOut += DalamudUtilOnLogOut;

            if (_dalamudUtil.IsLoggedIn)
            {
                DalamudUtilOnLogIn();
            }
        }

        private void DalamudUtilOnLogOut()
        {
            Task.Run(async () => await StopAllConnections(_cts.Token));
        }

        private void DalamudUtilOnLogIn()
        {
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

        public List<FileTransfer> CurrentDownloads { get; } = new();

        public List<FileTransfer> CurrentUploads { get; } = new();

        public List<FileTransfer> ForbiddenTransfers { get; } = new();

        public List<BannedUserDto> AdminBannedUsers { get; private set; } = new();

        public List<ForbiddenFileDto> AdminForbiddenFiles { get; private set; } = new();

        public bool IsConnected => !string.IsNullOrEmpty(UID);

        public bool IsDownloading => CurrentDownloads.Count > 0;

        public bool IsUploading => CurrentUploads.Count > 0;

        public List<ClientPairDto> PairedClients { get; set; } = new();

        public string SecretKey => _pluginConfiguration.ClientSecret.ContainsKey(ApiUri) ? _pluginConfiguration.ClientSecret[ApiUri] : "-";

        public bool ServerAlive =>
            (_heartbeatHub?.State ?? HubConnectionState.Disconnected) == HubConnectionState.Connected;

        public Dictionary<string, string> ServerDictionary => new Dictionary<string, string>() { { MainServiceUri, MainServer } }
            .Concat(_pluginConfiguration.CustomServerList)
            .ToDictionary(k => k.Key, k => k.Value);

        public string UID => _loggedInUser?.UID ?? string.Empty;

        private string ApiUri => _pluginConfiguration.ApiUri;
        public int OnlineUsers { get; private set; }

        public async Task CreateConnections()
        {
            await StopAllConnections(_cts.Token);

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            while (!ServerAlive && !token.IsCancellationRequested)
            {
                await StopAllConnections(token);

                try
                {
                    if (_dalamudUtil.PlayerCharacter == null) throw new ArgumentException("Player not initialized");
                    Logger.Debug("Building connection");
                    _heartbeatHub = BuildHubConnection("heartbeat");
                    _userHub = BuildHubConnection("user");
                    _fileHub = BuildHubConnection("files");
                    _adminHub = BuildHubConnection("admin");

                    await _heartbeatHub.StartAsync(token);
                    await _userHub.StartAsync(token);
                    await _fileHub.StartAsync(token);
                    await _adminHub.StartAsync(token);

                    OnlineUsers = await _userHub.InvokeAsync<int>("GetOnlineUsers", token);

                    if (_pluginConfiguration.FullPause)
                    {
                        _loggedInUser = null;
                        return;
                    }

                    _loggedInUser = await _heartbeatHub.InvokeAsync<LoggedInUserDto>("Heartbeat", token);
                    if (!string.IsNullOrEmpty(UID) && !token.IsCancellationRequested) // user is authorized
                    {
                        Logger.Debug("Initializing data");
                        _userHub.On<ClientPairDto, string>("UpdateClientPairs", UpdateLocalClientPairsCallback);
                        _userHub.On<CharacterCacheDto, string>("ReceiveCharacterData", ReceiveCharacterDataCallback);
                        _userHub.On<string>("RemoveOnlinePairedPlayer",
                            (s) => PairedClientOffline?.Invoke(s, EventArgs.Empty));
                        _userHub.On<string>("AddOnlinePairedPlayer",
                            (s) => PairedClientOnline?.Invoke(s, EventArgs.Empty));
                        _userHub.On<int>("UsersOnline", (count) => OnlineUsers = count);
                        _adminHub.On("ForcedReconnect", UserForcedReconnectCallback);

                        PairedClients = await _userHub!.InvokeAsync<List<ClientPairDto>>("GetPairedClients", token);

                        _heartbeatHub.Closed += HeartbeatHubOnClosed;
                        _heartbeatHub.Reconnected += HeartbeatHubOnReconnected;
                        _heartbeatHub.Reconnecting += HeartbeatHubOnReconnecting;

                        if (IsModerator)
                        {
                            AdminForbiddenFiles = await _adminHub.InvokeAsync<List<ForbiddenFileDto>>("GetForbiddenFiles", token);
                            AdminBannedUsers = await _adminHub.InvokeAsync<List<BannedUserDto>>("GetBannedUsers", token);
                            _adminHub.On<BannedUserDto>("UpdateOrAddBannedUser", UpdateOrAddBannedUserCallback);
                            _adminHub.On<BannedUserDto>("DeleteBannedUser", DeleteBannedUserCallback);
                            _adminHub.On<ForbiddenFileDto>("UpdateOrAddForbiddenFile", UpdateOrAddForbiddenFileCallback);
                            _adminHub.On<ForbiddenFileDto>("DeleteForbiddenFile", DeleteForbiddenFileCallback);
                        }

                        Connected?.Invoke(this, EventArgs.Empty);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex.Message);
                    Logger.Warn(ex.StackTrace ?? string.Empty);
                    Logger.Debug("Failed to establish connection, retrying");
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
            }
        }

        public void Dispose()
        {
            Logger.Debug("Disposing " + nameof(ApiController));

            _dalamudUtil.LogIn -= DalamudUtilOnLogIn;
            _dalamudUtil.LogOut -= DalamudUtilOnLogOut;

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
                        options.Headers.Add("CharacterNameHash", _dalamudUtil.PlayerNameHashed);
                    }

                    options.Transports = HttpTransportType.WebSockets;
#if DEBUG
                    options.HttpMessageHandlerFactory = (message) => new HttpClientHandler()
                    {
                        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    };
#endif
                })
                .WithAutomaticReconnect(new ForeverRetryPolicy())
                .Build();
        }

        private Task HeartbeatHubOnClosed(Exception? arg)
        {
            CurrentUploads.Clear();
            CurrentDownloads.Clear();
            _uploadCancellationTokenSource?.Cancel();
            Logger.Debug("Connection closed");
            Disconnected?.Invoke(null, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private Task HeartbeatHubOnReconnected(string? arg)
        {
            Logger.Debug("Connection restored");
            OnlineUsers = _userHub!.InvokeAsync<int>("GetOnlineUsers").Result;
            _loggedInUser = _heartbeatHub!.InvokeAsync<LoggedInUserDto>("Heartbeat").Result;
            Connected?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private Task HeartbeatHubOnReconnecting(Exception? arg)
        {
            CurrentUploads.Clear();
            CurrentDownloads.Clear();
            _uploadCancellationTokenSource?.Cancel();
            Logger.Debug("Connection closed... Reconnecting");
            Disconnected?.Invoke(null, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private async Task StopAllConnections(CancellationToken token)
        {
            if (_heartbeatHub is not null)
            {
                await _heartbeatHub.StopAsync(token);
                _heartbeatHub.Closed -= HeartbeatHubOnClosed;
                _heartbeatHub.Reconnected -= HeartbeatHubOnReconnected;
                _heartbeatHub.Reconnecting += HeartbeatHubOnReconnecting;
                await _heartbeatHub.DisposeAsync();
                _heartbeatHub = null;
            }

            if (_fileHub is not null)
            {
                await _fileHub.StopAsync(token);
                await _fileHub.DisposeAsync();
                _fileHub = null;
            }

            if (_userHub is not null)
            {
                await _userHub.StopAsync(token);
                await _userHub.DisposeAsync();
                _userHub = null;
            }

            if (_adminHub is not null)
            {
                await _adminHub.StopAsync(token);
                await _adminHub.DisposeAsync();
                _adminHub = null;
            }
        }
    }
}
