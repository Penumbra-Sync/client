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
using Microsoft.AspNetCore.SignalR;
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
        RateLimited
    }

    public partial class ApiController : IDisposable
    {
        public const string MainServer = "Lunae Crescere Incipientis (Central Server EU)";
        public const string MainServiceUri = "wss://maresynchronos.com";

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
            ServerState = ServerState.Offline;
            _verifiedUploadedHashes = new();

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
            ? _pluginConfiguration.ClientSecret[ApiUri] : string.Empty;

        public bool ServerAlive => ServerState is ServerState.Connected or ServerState.RateLimited or ServerState.Unauthorized or ServerState.Disconnected;

        public Dictionary<string, string> ServerDictionary => new Dictionary<string, string>()
                { { MainServiceUri, MainServer } }
            .Concat(_pluginConfiguration.CustomServerList)
            .ToDictionary(k => k.Key, k => k.Value);

        public string UID => _connectionDto?.UID ?? string.Empty;
        private string ApiUri => _pluginConfiguration.ApiUri;
        public int OnlineUsers => SystemInfoDto.OnlineUsers;

        private ServerState _serverState;
        public ServerState ServerState
        {
            get => _serverState;
            private set
            {
                Logger.Debug($"New ServerState: {value}, prev ServerState: {_serverState}");
                _serverState = value;
            }
        }

        public async Task CreateConnections()
        {
            Logger.Debug("CreateConnections called");

            if (_pluginConfiguration.FullPause)
            {
                Logger.Info("Not recreating Connection, paused");
                ServerState = ServerState.Disconnected;
                _connectionDto = null;
                await StopConnection(_connectionCancellationTokenSource.Token);
                return;
            }

            await StopConnection(_connectionCancellationTokenSource.Token);

            Logger.Info("Recreating Connection");

            _connectionCancellationTokenSource.Cancel();
            _connectionCancellationTokenSource = new CancellationTokenSource();
            var token = _connectionCancellationTokenSource.Token;
            _verifiedUploadedHashes.Clear();
            while (ServerState is not ServerState.Connected && !token.IsCancellationRequested)
            {
                if (string.IsNullOrEmpty(SecretKey))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    continue;
                }

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

                    _connectionDto =
                        await _mareHub.InvokeAsync<ConnectionDto>(Api.InvokeHeartbeat, _dalamudUtil.PlayerNameHashed, token);

                    ServerState = ServerState.Connected;

                    if (_connectionDto.ServerVersion != Api.Version)
                    {
                        ServerState = ServerState.VersionMisMatch;
                        await StopConnection(token);
                        return;
                    }

                    if (ServerState is ServerState.Connected) // user is authorized && server is legit
                    {
                        await InitializeData(token);

                        _mareHub.Closed += MareHubOnClosed;
                        _mareHub.Reconnecting += MareHubOnReconnecting;
                    }
                }
                catch (HubException ex)
                {
                    Logger.Warn(ex.GetType().ToString());
                    Logger.Warn(ex.Message);
                    Logger.Warn(ex.StackTrace ?? string.Empty);

                    ServerState = ServerState.RateLimited;
                    await StopConnection(token);
                    return;
                }
                catch (HttpRequestException ex)
                {
                    Logger.Warn(ex.GetType().ToString());
                    Logger.Warn(ex.Message);
                    Logger.Warn(ex.StackTrace ?? string.Empty);

                    if (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        ServerState = ServerState.Unauthorized;
                        await StopConnection(token);
                        return;
                    }
                    else
                    {
                        ServerState = ServerState.Offline;
                        Logger.Info("Failed to establish connection, retrying");
                        await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 20)), token);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex.GetType().ToString());
                    Logger.Warn(ex.Message);
                    Logger.Warn(ex.StackTrace ?? string.Empty);
                    Logger.Info("Failed to establish connection, retrying");
                    await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 20)), token);
                }
            }
        }

        private async Task InitializeData(CancellationToken token)
        {
            if (_mareHub == null) return;

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
                    options.Headers.Add("Authorization", SecretKey);
                    options.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling;
                })
                .WithAutomaticReconnect(new ForeverRetryPolicy())
                .Build();
        }

        private Task MareHubOnClosed(Exception? arg)
        {
            CurrentUploads.Clear();
            CurrentDownloads.Clear();
            _uploadCancellationTokenSource?.Cancel();
            Disconnected?.Invoke();
            ServerState = ServerState.Offline;
            Logger.Info("Connection closed");
            return Task.CompletedTask;
        }

        private Task MareHubOnReconnecting(Exception? arg)
        {
            ServerState = ServerState.Disconnected;
            Logger.Warn("Connection closed... Reconnecting");
            Logger.Warn(arg?.Message ?? string.Empty);
            Logger.Warn(arg?.StackTrace ?? string.Empty);
            Disconnected?.Invoke();
            ServerState = ServerState.Offline;
            _ = Task.Run(CreateConnections);
            return Task.CompletedTask;
        }

        private async Task StopConnection(CancellationToken token)
        {
            if (_mareHub is not null)
            {
                Logger.Info("Stopping existing connection");
                _mareHub.Closed -= MareHubOnClosed;
                _mareHub.Reconnecting += MareHubOnReconnecting;
                await _mareHub.StopAsync(token);
                await _mareHub.DisposeAsync();
                CurrentUploads.Clear();
                CurrentDownloads.Clear();
                _uploadCancellationTokenSource?.Cancel();
                Disconnected?.Invoke();
                _mareHub = null;
            }

            if (ServerState != ServerState.Disconnected)
            {
                while (ServerState != ServerState.Offline)
                {
                    await Task.Delay(16);
                }
            }
        }
    }
}
