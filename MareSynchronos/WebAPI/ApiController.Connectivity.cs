using System;
using System.Collections.Generic;
using System.Linq;
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
        VersionMisMatch,
        NoAccount
    }

    public partial class ApiController : IDisposable
    {
        public const string MainServer = "Lunae Crescere Incipientis (Central Server EU)";
        public const string MainServiceUri = "wss://v2202207178628194299.powersrv.de:6872";

        public readonly int[] SupportedServerVersions = { Api.Version };

        private readonly Configuration _pluginConfiguration;
        private readonly DalamudUtil _dalamudUtil;

        private CancellationTokenSource _connectionCancellationTokenSource;

        private HubConnection? _mareHub;

        private CancellationTokenSource? _uploadCancellationTokenSource = new();

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
            Task.Run(async () => await StopConnection(_connectionCancellationTokenSource.Token));
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
            (_mareHub?.State ?? HubConnectionState.Disconnected) == HubConnectionState.Connected;

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

            await StopConnection(_connectionCancellationTokenSource.Token);

            _connectionCancellationTokenSource.Cancel();
            _connectionCancellationTokenSource = new CancellationTokenSource();
            var token = _connectionCancellationTokenSource.Token;
            while (ServerState is not ServerState.Connected && !token.IsCancellationRequested)
            {
                await StopConnection(token);

                try
                {
                    Logger.Debug("Building connection");

                    while (!_dalamudUtil.IsPlayerPresent && !token.IsCancellationRequested)
                    {
                        Logger.Debug("Player not loaded in yet, waiting");
                        await Task.Delay(TimeSpan.FromSeconds(1), token);
                    }

                    if (token.IsCancellationRequested) break;

                    _mareHub = BuildHubConnection(Api.Path);

                    await _mareHub.StartAsync(token);

                    _mareHub.On<SystemInfoDto>(Api.OnUpdateSystemInfo, (dto) => SystemInfoDto = dto);

                    if (_pluginConfiguration.FullPause)
                    {
                        _connectionDto = null;
                        return;
                    }

                    _connectionDto =
                        await _mareHub.InvokeAsync<ConnectionDto>(Api.InvokeHeartbeat, _dalamudUtil.PlayerNameHashed, token);
                    if (ServerState is ServerState.Connected) // user is authorized && server is legit
                    {
                        Logger.Debug("Initializing data");
                        _mareHub.On<ClientPairDto, string>(Api.OnUserUpdateClientPairs,
                            UpdateLocalClientPairsCallback);
                        _mareHub.On<CharacterCacheDto, string>(Api.OnUserReceiveCharacterData,
                            ReceiveCharacterDataCallback);
                        _mareHub.On<string>(Api.OnUserRemoveOnlinePairedPlayer,
                            (s) => PairedClientOffline?.Invoke(s));
                        _mareHub.On<string>(Api.OnUserAddOnlinePairedPlayer,
                            (s) => PairedClientOnline?.Invoke(s));
                        _mareHub.On(Api.OnAdminForcedReconnect, UserForcedReconnectCallback);

                        PairedClients =
                            await _mareHub!.InvokeAsync<List<ClientPairDto>>(Api.InvokeUserGetPairedClients, token);

                        _mareHub.Closed += MareHubOnClosed;
                        _mareHub.Reconnected += MareHubOnReconnected;
                        _mareHub.Reconnecting += MareHubOnReconnecting;

                        if (IsModerator)
                        {
                            AdminForbiddenFiles =
                                await _mareHub.InvokeAsync<List<ForbiddenFileDto>>(Api.InvokeAdminGetForbiddenFiles,
                                    token);
                            AdminBannedUsers =
                                await _mareHub.InvokeAsync<List<BannedUserDto>>(Api.InvokeAdminGetBannedUsers,
                                    token);
                            _mareHub.On<BannedUserDto>(Api.OnAdminUpdateOrAddBannedUser,
                                UpdateOrAddBannedUserCallback);
                            _mareHub.On<BannedUserDto>(Api.OnAdminDeleteBannedUser, DeleteBannedUserCallback);
                            _mareHub.On<ForbiddenFileDto>(Api.OnAdminUpdateOrAddForbiddenFile,
                                UpdateOrAddForbiddenFileCallback);
                            _mareHub.On<ForbiddenFileDto>(Api.OnAdminDeleteForbiddenFile,
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
                    await StopConnection(token);
                    await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 20)), token);
                }
            }
        }

        public void Dispose()
        {
            Logger.Verbose("Disposing " + nameof(ApiController));

            _dalamudUtil.LogIn -= DalamudUtilOnLogIn;
            _dalamudUtil.LogOut -= DalamudUtilOnLogOut;

            Task.Run(async () => await StopConnection(_connectionCancellationTokenSource.Token));
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

        private Task MareHubOnClosed(Exception? arg)
        {
            CurrentUploads.Clear();
            CurrentDownloads.Clear();
            _uploadCancellationTokenSource?.Cancel();
            Logger.Debug("Connection closed");
            Disconnected?.Invoke();
            return Task.CompletedTask;
        }

        private async Task MareHubOnReconnected(string? arg)
        {
            Logger.Debug("Connection restored");
            await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 10)));
            _connectionDto = await _mareHub!.InvokeAsync<ConnectionDto>(Api.InvokeHeartbeat, _dalamudUtil.PlayerNameHashed);
            Connected?.Invoke();
        }

        private Task MareHubOnReconnecting(Exception? arg)
        {
            CurrentUploads.Clear();
            CurrentDownloads.Clear();
            _uploadCancellationTokenSource?.Cancel();
            Logger.Debug("Connection closed... Reconnecting");
            Disconnected?.Invoke();
            return Task.CompletedTask;
        }

        private async Task StopConnection(CancellationToken token)
        {
            Logger.Verbose("Stopping all connections");
            if (_mareHub is not null)
            {
                await _mareHub.StopAsync(token);
                _mareHub.Closed -= MareHubOnClosed;
                _mareHub.Reconnected -= MareHubOnReconnected;
                _mareHub.Reconnecting += MareHubOnReconnecting;
                await _mareHub.DisposeAsync();
                _mareHub = null;
            }
        }
    }
}
