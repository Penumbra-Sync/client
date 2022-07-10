using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Logging;
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
        VersionMisMatch,
        NoAccount
    }

    public partial class ApiController : IDisposable
    {
        public const string MainServer = "Lunae Crescere Incipientis (Central Server EU)";
        public const string MainServiceUri = "wss://v2202207178628194299.powersrv.de:6871";

        public readonly int[] SupportedServerVersions = { API.API.Version };

        private readonly Configuration _pluginConfiguration;
        private readonly DalamudUtil _dalamudUtil;

        private CancellationTokenSource _connectionCancellationTokenSource;

        private HubConnection? _fileHub;

        private HubConnection? _connectionHub;

        private HubConnection? _adminHub;

        private CancellationTokenSource? _uploadCancellationTokenSource;

        private HubConnection? _userHub;
        private ConnectionDto? _connectionDto;
        public SystemInfoDto SystemInfoDto { get; private set; } = new();
        public bool IsModerator => (_connectionDto?.IsAdmin ?? false) || (_connectionDto?.IsModerator ?? false);

        public bool IsAdmin => _connectionDto?.IsAdmin ?? false;

        public ApiController(Configuration pluginConfiguration, DalamudUtil dalamudUtil)
        {
            Logger.Verbose("Creating " + nameof(ApiController));

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

        public Dictionary<int, List<DownloadFileTransfer>> CurrentDownloads { get; } = new();

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
            (_connectionHub?.State ?? HubConnectionState.Disconnected) == HubConnectionState.Connected;

        public Dictionary<string, string> ServerDictionary => new Dictionary<string, string>()
                { { MainServiceUri, MainServer } }
            .Concat(_pluginConfiguration.CustomServerList)
            .ToDictionary(k => k.Key, k => k.Value);

        public string UID => _connectionDto?.UID ?? string.Empty;
        private string ApiUri => _pluginConfiguration.ApiUri;
        public int OnlineUsers => SystemInfoDto.OnlineUsers;

        public ServerState ServerState
        {
            get
            {
                var supportedByServer = SupportedServerVersions.Contains(_connectionDto?.ServerVersion ?? 0);
                bool hasUid = !string.IsNullOrEmpty(UID);
                if (_pluginConfiguration.FullPause)
                    return ServerState.Disconnected;
                if (!ServerAlive)
                    return ServerState.Offline;
                if (!hasUid && _pluginConfiguration.ClientSecret.ContainsKey(ApiUri))
                    return ServerState.Unauthorized;
                if (!supportedByServer)
                    return ServerState.VersionMisMatch;
                if (supportedByServer && hasUid)
                    return ServerState.Connected;

                return ServerState.NoAccount;
            }
        }

        public async Task CreateConnections()
        {
            Logger.Verbose("Recreating Connection");

            await StopAllConnections(_connectionCancellationTokenSource.Token);

            _connectionCancellationTokenSource.Cancel();
            _connectionCancellationTokenSource = new CancellationTokenSource();
            var token = _connectionCancellationTokenSource.Token;
            while (ServerState is not ServerState.Connected && !token.IsCancellationRequested)
            {
                await StopAllConnections(token);

                try
                {
                    Logger.Debug("Building connection");

                    while (!_dalamudUtil.IsPlayerPresent && !token.IsCancellationRequested)
                    {
                        Logger.Debug("Player not loaded in yet, waiting");
                        await Task.Delay(TimeSpan.FromSeconds(1), token);
                    }

                    if (token.IsCancellationRequested) break;

                    _connectionHub = BuildHubConnection(ConnectionHubAPI.Path);
                    _userHub = BuildHubConnection(UserHubAPI.Path);
                    _fileHub = BuildHubConnection(FilesHubAPI.Path);
                    _adminHub = BuildHubConnection(AdminHubAPI.Path);

                    await _connectionHub.StartAsync(token);
                    await _userHub.StartAsync(token);
                    await _fileHub.StartAsync(token);
                    await _adminHub.StartAsync(token);

                    _connectionHub.On<SystemInfoDto>(ConnectionHubAPI.OnUpdateSystemInfo, (dto) => SystemInfoDto = dto);

                    if (_pluginConfiguration.FullPause)
                    {
                        _connectionDto = null;
                        return;
                    }

                    _connectionDto =
                        await _connectionHub.InvokeAsync<ConnectionDto>(ConnectionHubAPI.InvokeHeartbeat, _dalamudUtil.PlayerNameHashed, token);
                    if (ServerState is ServerState.Connected) // user is authorized && server is legit
                    {
                        Logger.Debug("Initializing data");
                        _userHub.On<ClientPairDto, string>(UserHubAPI.OnUpdateClientPairs,
                            UpdateLocalClientPairsCallback);
                        _userHub.On<CharacterCacheDto, string>(UserHubAPI.OnReceiveCharacterData,
                            ReceiveCharacterDataCallback);
                        _userHub.On<string>(UserHubAPI.OnRemoveOnlinePairedPlayer,
                            (s) => PairedClientOffline?.Invoke(s));
                        _userHub.On<string>(UserHubAPI.OnAddOnlinePairedPlayer,
                            (s) => PairedClientOnline?.Invoke(s));
                        _adminHub.On(AdminHubAPI.OnForcedReconnect, UserForcedReconnectCallback);

                        PairedClients =
                            await _userHub!.InvokeAsync<List<ClientPairDto>>(UserHubAPI.InvokeGetPairedClients, token);

                        _connectionHub.Closed += ConnectionHubOnClosed;
                        _connectionHub.Reconnected += ConnectionHubOnReconnected;
                        _connectionHub.Reconnecting += ConnectionHubOnReconnecting;

                        if (IsModerator)
                        {
                            AdminForbiddenFiles =
                                await _adminHub.InvokeAsync<List<ForbiddenFileDto>>(AdminHubAPI.InvokeGetForbiddenFiles,
                                    token);
                            AdminBannedUsers =
                                await _adminHub.InvokeAsync<List<BannedUserDto>>(AdminHubAPI.InvokeGetBannedUsers,
                                    token);
                            _adminHub.On<BannedUserDto>(AdminHubAPI.OnUpdateOrAddBannedUser,
                                UpdateOrAddBannedUserCallback);
                            _adminHub.On<BannedUserDto>(AdminHubAPI.OnDeleteBannedUser, DeleteBannedUserCallback);
                            _adminHub.On<ForbiddenFileDto>(AdminHubAPI.OnUpdateOrAddForbiddenFile,
                                UpdateOrAddForbiddenFileCallback);
                            _adminHub.On<ForbiddenFileDto>(AdminHubAPI.OnDeleteForbiddenFile,
                                DeleteForbiddenFileCallback);
                        }

                        Connected?.Invoke();
                    }
                    else if (ServerState is ServerState.VersionMisMatch or ServerState.NoAccount or ServerState.Unauthorized)
                    {
                        break;
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
            _connectionCancellationTokenSource?.Cancel();
        }

        private HubConnection BuildHubConnection(string hubName)
        {
            return new HubConnectionBuilder()
                .WithUrl(ApiUri + hubName, options =>
                {
                    if (!string.IsNullOrEmpty(SecretKey) && !_pluginConfiguration.FullPause)
                    {
                        options.Headers.Add("Authorization", SecretKey);
                    }

                    options.Transports = HttpTransportType.WebSockets;
                })
                .WithAutomaticReconnect(new ForeverRetryPolicy())
                .Build();
        }

        private Task ConnectionHubOnClosed(Exception? arg)
        {
            CurrentUploads.Clear();
            CurrentDownloads.Clear();
            _uploadCancellationTokenSource?.Cancel();
            Logger.Debug("Connection closed");
            Disconnected?.Invoke();
            return Task.CompletedTask;
        }

        private async Task ConnectionHubOnReconnected(string? arg)
        {
            Logger.Debug("Connection restored");
            await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 10)));
            _connectionDto = _connectionHub!.InvokeAsync<ConnectionDto>(ConnectionHubAPI.InvokeHeartbeat, _dalamudUtil.PlayerNameHashed).Result;
            Connected?.Invoke();
        }

        private Task ConnectionHubOnReconnecting(Exception? arg)
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
            if (_connectionHub is not null)
            {
                await _connectionHub.StopAsync(token);
                _connectionHub.Closed -= ConnectionHubOnClosed;
                _connectionHub.Reconnected -= ConnectionHubOnReconnected;
                _connectionHub.Reconnecting += ConnectionHubOnReconnecting;
                await _connectionHub.DisposeAsync();
                _connectionHub = null;
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
