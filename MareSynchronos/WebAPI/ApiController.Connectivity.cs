using System;
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
    public delegate void VoidDelegate();
    public delegate void SimpleStringDelegate(string str);
    public enum ServerState
    {
        Offline,
        Disconnected,
        Connected,
        Unauthorized,
        VersionMisMatch
    }

    public partial class ApiController : IDisposable
    {
#if DEBUG
        public const string MainServer = "Lunae Crescere Incipientis (Central Server EU)";
        public const string MainServiceUri = "wss://v2202207178628194299.powersrv.de:6871";
#else
        public const string MainServer = "Lunae Crescere Incipientis (Central Server EU)";
        public const string MainServiceUri = "wss://v2202207178628194299.powersrv.de:6871";
#endif
        public readonly int[] SupportedServerVersions = { 1 };

        private readonly Configuration _pluginConfiguration;
        private readonly DalamudUtil _dalamudUtil;

        private CancellationTokenSource _connectionCancellationTokenSource;

        private HubConnection? _fileHub;

        private HubConnection? _heartbeatHub;

        private HubConnection? _adminHub;

        private CancellationTokenSource? _uploadCancellationTokenSource;

        private HubConnection? _userHub;
        private ConnectionDto? _connectionDto;
        public bool IsModerator => (_connectionDto?.IsAdmin ?? false) || (_connectionDto?.IsModerator ?? false);

        public bool IsAdmin => _connectionDto?.IsAdmin ?? false;

        public ApiController(Configuration pluginConfiguration, DalamudUtil dalamudUtil)
        {
            Verbose.Debug("Creating " + nameof(ApiController));

            _pluginConfiguration = pluginConfiguration;
            _dalamudUtil = dalamudUtil;
            _connectionCancellationTokenSource = new CancellationTokenSource();
            _dalamudUtil.LogIn += DalamudUtilOnLogIn;
            _dalamudUtil.LogOut += DalamudUtilOnLogOut;

            if (_dalamudUtil.IsLoggedIn)
            {
                DalamudUtilOnLogIn();
            }
        }

        private void DalamudUtilOnLogOut()
        {
            Task.Run(async () => await StopAllConnections(_connectionCancellationTokenSource.Token));
        }

        private void DalamudUtilOnLogIn()
        {
            Task.Run(CreateConnections);
        }


        public event EventHandler<CharacterReceivedEventArgs>? CharacterReceived;

        public event VoidDelegate? RegisterFinalized;

        public event VoidDelegate? Connected;

        public event VoidDelegate? Disconnected;

        public event SimpleStringDelegate? PairedClientOffline;

        public event SimpleStringDelegate? PairedClientOnline;

        public event SimpleStringDelegate? PairedWithOther;

        public event SimpleStringDelegate? UnpairedFromOther;

        public List<FileTransfer> CurrentDownloads { get; } = new();

        public List<FileTransfer> CurrentUploads { get; } = new();

        public List<FileTransfer> ForbiddenTransfers { get; } = new();

        public List<BannedUserDto> AdminBannedUsers { get; private set; } = new();

        public List<ForbiddenFileDto> AdminForbiddenFiles { get; private set; } = new();

        public bool IsConnected => ServerState == ServerState.Connected;

        public bool IsDownloading => CurrentDownloads.Count > 0;

        public bool IsUploading => CurrentUploads.Count > 0;

        public List<ClientPairDto> PairedClients { get; set; } = new();

        public string SecretKey => _pluginConfiguration.ClientSecret.ContainsKey(ApiUri)
            ? _pluginConfiguration.ClientSecret[ApiUri]
            : "-";

        public bool ServerAlive =>
            (_heartbeatHub?.State ?? HubConnectionState.Disconnected) == HubConnectionState.Connected;

        public Dictionary<string, string> ServerDictionary => new Dictionary<string, string>()
                { { MainServiceUri, MainServer } }
            .Concat(_pluginConfiguration.CustomServerList)
            .ToDictionary(k => k.Key, k => k.Value);

        public string UID => _connectionDto?.UID ?? string.Empty;
        private string ApiUri => _pluginConfiguration.ApiUri;
        public int OnlineUsers { get; private set; }

        public ServerState ServerState
        {
            get
            {
                if (_pluginConfiguration.FullPause)
                    return ServerState.Disconnected;
                if (!ServerAlive)
                    return ServerState.Offline;
                if (ServerAlive && !SupportedServerVersions.Contains(_connectionDto?.ServerVersion ?? 0) && !string.IsNullOrEmpty(UID))
                    return ServerState.VersionMisMatch;
                if (ServerAlive && SupportedServerVersions.Contains(_connectionDto?.ServerVersion ?? 0)
                                && string.IsNullOrEmpty(UID))
                    return ServerState.Unauthorized;
                return ServerState.Connected;
            }
        }

        public async Task CreateConnections()
        {
            Logger.Verbose("Recreating Connection");

            await StopAllConnections(_connectionCancellationTokenSource.Token);

            _connectionCancellationTokenSource.Cancel();
            _connectionCancellationTokenSource = new CancellationTokenSource();
            var token = _connectionCancellationTokenSource.Token;

            while (!ServerAlive && !token.IsCancellationRequested)
            {
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
                    _userHub.On<int>("UsersOnline", (count) => OnlineUsers = count);

                    if (_pluginConfiguration.FullPause)
                    {
                        _connectionDto = null;
                        return;
                    }

                    _connectionDto = await _heartbeatHub.InvokeAsync<ConnectionDto>("Heartbeat", token);
                    if (ServerState is ServerState.Connected) // user is authorized && server is legit
                    {
                        Logger.Debug("Initializing data");
                        _userHub.On<ClientPairDto, string>("UpdateClientPairs", UpdateLocalClientPairsCallback);
                        _userHub.On<CharacterCacheDto, string>("ReceiveCharacterData", ReceiveCharacterDataCallback);
                        _userHub.On<string>("RemoveOnlinePairedPlayer",
                            (s) => PairedClientOffline?.Invoke(s));
                        _userHub.On<string>("AddOnlinePairedPlayer",
                            (s) => PairedClientOnline?.Invoke(s));
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

                        Connected?.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex.Message);
                    Logger.Warn(ex.StackTrace ?? string.Empty);
                    Logger.Debug("Failed to establish connection, retrying");
                    await StopAllConnections(token);
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
            }
        }

        public void Dispose()
        {
            Logger.Verbose("Disposing " + nameof(ApiController));

            _dalamudUtil.LogIn -= DalamudUtilOnLogIn;
            _dalamudUtil.LogOut -= DalamudUtilOnLogOut;

            Task.Run(async () => await StopAllConnections(_connectionCancellationTokenSource.Token));
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
            Disconnected?.Invoke();
            return Task.CompletedTask;
        }

        private Task HeartbeatHubOnReconnected(string? arg)
        {
            Logger.Debug("Connection restored");
            OnlineUsers = _userHub!.InvokeAsync<int>("GetOnlineUsers").Result;
            _connectionDto = _heartbeatHub!.InvokeAsync<ConnectionDto>("Heartbeat").Result;
            Connected?.Invoke();
            return Task.CompletedTask;
        }

        private Task HeartbeatHubOnReconnecting(Exception? arg)
        {
            CurrentUploads.Clear();
            CurrentDownloads.Clear();
            _uploadCancellationTokenSource?.Cancel();
            Logger.Debug("Connection closed... Reconnecting");
            Disconnected?.Invoke();
            return Task.CompletedTask;
        }

        private async Task StopAllConnections(CancellationToken token)
        {
            Logger.Verbose("Stopping all connections");
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
