using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MareSynchronos.API;
using MareSynchronos.FileCache;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.WebAPI;

public delegate void SimpleStringDelegate(string str);

public partial class ApiController : IDisposable
{
    public const string MainServer = "Lunae Crescere Incipientis (Central Server EU)";
    public const string MainServiceUri = "wss://maresynchronos.com";

    public readonly int[] SupportedServerVersions = { Api.Version };

    private readonly Configuration _pluginConfiguration;
    private readonly DalamudUtil _dalamudUtil;
    private readonly FileCacheManager _fileDbManager;
    private CancellationTokenSource _connectionCancellationTokenSource;

    private HubConnection? _mareHub;

    private CancellationTokenSource? _uploadCancellationTokenSource = new();
    private CancellationTokenSource? _healthCheckTokenSource = new();

    private ConnectionDto? _connectionDto;
    public ServerInfoDto ServerInfo => _connectionDto?.ServerInfo ?? new ServerInfoDto();

    public SystemInfoDto SystemInfoDto { get; private set; } = new();
    public bool IsModerator => (_connectionDto?.IsAdmin ?? false) || (_connectionDto?.IsModerator ?? false);

    public bool IsAdmin => _connectionDto?.IsAdmin ?? false;

    public ApiController(Configuration pluginConfiguration, DalamudUtil dalamudUtil, FileCacheManager fileDbManager)
    {
        Logger.Verbose("Creating " + nameof(ApiController));

        _pluginConfiguration = pluginConfiguration;
        _dalamudUtil = dalamudUtil;
        _fileDbManager = fileDbManager;
        _connectionCancellationTokenSource = new CancellationTokenSource();
        _dalamudUtil.LogIn += DalamudUtilOnLogIn;
        _dalamudUtil.LogOut += DalamudUtilOnLogOut;
        ServerState = ServerState.Offline;
        _verifiedUploadedHashes = new(StringComparer.Ordinal);

        if (_dalamudUtil.IsLoggedIn)
        {
            DalamudUtilOnLogIn();
        }
    }

    private void DalamudUtilOnLogOut()
    {
        Task.Run(async () => await StopConnection(_connectionCancellationTokenSource.Token).ConfigureAwait(false));
        ServerState = ServerState.Offline;
    }

    private void DalamudUtilOnLogIn()
    {
        Task.Run(CreateConnections);
    }


    public event EventHandler<CharacterReceivedEventArgs>? CharacterReceived;

    public event VoidDelegate? Connected;

    public event VoidDelegate? Disconnected;

    public event SimpleStringDelegate? PairedClientOffline;

    public event SimpleStringDelegate? PairedClientOnline;
    public event VoidDelegate? DownloadStarted;
    public event VoidDelegate? DownloadFinished;

    public ConcurrentDictionary<int, List<DownloadFileTransfer>> CurrentDownloads { get; } = new();

    public List<FileTransfer> CurrentUploads { get; } = new();

    public List<FileTransfer> ForbiddenTransfers { get; } = new();

    public List<BannedUserDto> AdminBannedUsers { get; private set; } = new();

    public List<ForbiddenFileDto> AdminForbiddenFiles { get; private set; } = new();

    public bool IsConnected => ServerState == ServerState.Connected;

    public bool IsDownloading => CurrentDownloads.Count > 0;

    public bool IsUploading => CurrentUploads.Count > 0;

    public List<ClientPairDto> PairedClients { get; set; } = new();
    public List<GroupPairDto> GroupPairedClients { get; set; } = new();
    public List<GroupDto> Groups { get; set; } = new();

    public string SecretKey => _pluginConfiguration.ClientSecret.ContainsKey(ApiUri)
        ? _pluginConfiguration.ClientSecret[ApiUri] : string.Empty;

    public bool ServerAlive => ServerState is ServerState.Connected or ServerState.RateLimited or ServerState.Unauthorized or ServerState.Disconnected;

    public Dictionary<string, string> ServerDictionary => new Dictionary<string, string>(StringComparer.Ordinal)
            { { MainServiceUri, MainServer } }
        .Concat(_pluginConfiguration.CustomServerList)
        .ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal);

    public string UID => _connectionDto?.UID ?? string.Empty;
    public string DisplayName => _connectionDto?.UID ?? string.Empty;
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
            await StopConnection(_connectionCancellationTokenSource.Token).ConfigureAwait(false);
            return;
        }

        await StopConnection(_connectionCancellationTokenSource.Token).ConfigureAwait(false);

        Logger.Info("Recreating Connection");

        _connectionCancellationTokenSource.Cancel();
        _connectionCancellationTokenSource = new CancellationTokenSource();
        var token = _connectionCancellationTokenSource.Token;
        _verifiedUploadedHashes.Clear();
        while (ServerState is not ServerState.Connected && !token.IsCancellationRequested)
        {
            if (string.IsNullOrEmpty(SecretKey))
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                continue;
            }

            await StopConnection(token).ConfigureAwait(false);

            try
            {
                Logger.Debug("Building connection");

                while (!_dalamudUtil.IsPlayerPresent && !token.IsCancellationRequested)
                {
                    Logger.Debug("Player not loaded in yet, waiting");
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                }

                if (token.IsCancellationRequested) break;

                _mareHub = BuildHubConnection(Api.Path);

                await _mareHub.StartAsync(token).ConfigureAwait(false);

                _mareHub.On<SystemInfoDto>(Api.OnUpdateSystemInfo, (dto) => SystemInfoDto = dto);

                _connectionDto =
                    await _mareHub.InvokeAsync<ConnectionDto>(Api.InvokeHeartbeat, _dalamudUtil.PlayerNameHashed, token).ConfigureAwait(false);

                ServerState = ServerState.Connected;

                if (_connectionDto.ServerVersion != Api.Version)
                {
                    ServerState = ServerState.VersionMisMatch;
                    await StopConnection(token).ConfigureAwait(false);
                    return;
                }

                if (ServerState is ServerState.Connected) // user is authorized && server is legit
                {
                    await InitializeData(token).ConfigureAwait(false);

                    _mareHub.Closed += MareHubOnClosed;
                    _mareHub.Reconnecting += MareHubOnReconnecting;
                    _mareHub.Reconnected += MareHubOnReconnected;
                }
            }
            catch (HubException ex)
            {
                Logger.Warn(ex.GetType().ToString());
                Logger.Warn(ex.Message);
                Logger.Warn(ex.StackTrace ?? string.Empty);

                ServerState = ServerState.RateLimited;
                await StopConnection(token).ConfigureAwait(false);
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
                    await StopConnection(token).ConfigureAwait(false);
                    return;
                }
                else
                {
                    ServerState = ServerState.Offline;
                    Logger.Info("Failed to establish connection, retrying");
                    await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 20)), token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex.GetType().ToString());
                Logger.Warn(ex.Message);
                Logger.Warn(ex.StackTrace ?? string.Empty);
                Logger.Info("Failed to establish connection, retrying");
                await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 20)), token).ConfigureAwait(false);
            }
        }
    }

    private Task MareHubOnReconnected(string? arg)
    {
        _ = Task.Run(CreateConnections);
        return Task.CompletedTask;
    }

    private async Task ClientHealthCheck(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) break;
            var needsRestart = await _mareHub!.InvokeAsync<bool>(Api.InvokeCheckClientHealth, ct).ConfigureAwait(false);
            Logger.Debug("Checked Client Health State, healthy: " + !needsRestart);
            if (needsRestart)
            {
                _ = CreateConnections();
            }
        }
    }

    private async Task InitializeData(CancellationToken token)
    {
        if (_mareHub == null) return;

        Logger.Debug("Initializing data");
        _mareHub.On<ClientPairDto>(Api.OnUserUpdateClientPairs,
            UpdateLocalClientPairsCallback);
        _mareHub.On<CharacterCacheDto, string>(Api.OnUserReceiveCharacterData,
            ReceiveCharacterDataCallback);
        _mareHub.On<string>(Api.OnUserRemoveOnlinePairedPlayer,
            (s) => PairedClientOffline?.Invoke(s));
        _mareHub.On<string>(Api.OnUserAddOnlinePairedPlayer,
            (s) => PairedClientOnline?.Invoke(s));
        _mareHub.On(Api.OnAdminForcedReconnect, UserForcedReconnectCallback);
        _mareHub.On<GroupDto>(Api.OnGroupChange, GroupChangedCallback);
        _mareHub.On<GroupPairDto>(Api.OnGroupUserChange, GroupPairChangedCallback);

        PairedClients =
            await _mareHub!.InvokeAsync<List<ClientPairDto>>(Api.InvokeUserGetPairedClients, token).ConfigureAwait(false);
        Groups = await GetGroups().ConfigureAwait(false);
        GroupPairedClients.Clear();
        foreach (var group in Groups)
        {
            GroupPairedClients.AddRange(await GetUsersInGroup(group.GID).ConfigureAwait(false));
        }

        if (IsModerator)
        {
            AdminForbiddenFiles =
                await _mareHub.InvokeAsync<List<ForbiddenFileDto>>(Api.InvokeAdminGetForbiddenFiles,
                    token).ConfigureAwait(false);
            AdminBannedUsers =
                await _mareHub.InvokeAsync<List<BannedUserDto>>(Api.InvokeAdminGetBannedUsers,
                    token).ConfigureAwait(false);
            _mareHub.On<BannedUserDto>(Api.OnAdminUpdateOrAddBannedUser,
                UpdateOrAddBannedUserCallback);
            _mareHub.On<BannedUserDto>(Api.OnAdminDeleteBannedUser, DeleteBannedUserCallback);
            _mareHub.On<ForbiddenFileDto>(Api.OnAdminUpdateOrAddForbiddenFile,
                UpdateOrAddForbiddenFileCallback);
            _mareHub.On<ForbiddenFileDto>(Api.OnAdminDeleteForbiddenFile,
                DeleteForbiddenFileCallback);
        }

        _healthCheckTokenSource?.Cancel();
        _healthCheckTokenSource?.Dispose();
        _healthCheckTokenSource = new CancellationTokenSource();
        _ = ClientHealthCheck(_healthCheckTokenSource.Token);

        Connected?.Invoke();
    }

    public void Dispose()
    {
        Logger.Verbose("Disposing " + nameof(ApiController));

        _dalamudUtil.LogIn -= DalamudUtilOnLogIn;
        _dalamudUtil.LogOut -= DalamudUtilOnLogOut;

        Task.Run(async () => await StopConnection(_connectionCancellationTokenSource.Token).ConfigureAwait(false));
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
            .ConfigureLogging(a =>
            {
                a.ClearProviders().AddProvider(new DalamudLoggingProvider());
                a.SetMinimumLevel(LogLevel.Warning);
            })
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
        _connectionDto = null;
        _healthCheckTokenSource?.Cancel();
        ServerState = ServerState.Disconnected;
        Logger.Warn("Connection closed... Reconnecting");
        Logger.Warn(arg?.Message ?? string.Empty);
        Logger.Warn(arg?.StackTrace ?? string.Empty);
        Disconnected?.Invoke();
        ServerState = ServerState.Offline;
        return Task.CompletedTask;
    }

    private async Task StopConnection(CancellationToken token)
    {
        if (_mareHub is not null)
        {
            _uploadCancellationTokenSource?.Cancel();
            Logger.Info("Stopping existing connection");
            _mareHub.Closed -= MareHubOnClosed;
            _mareHub.Reconnecting -= MareHubOnReconnecting;
            _mareHub.Reconnected -= MareHubOnReconnected;
            await _mareHub.StopAsync(token).ConfigureAwait(false);
            await _mareHub.DisposeAsync().ConfigureAwait(false);
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
                await Task.Delay(16).ConfigureAwait(false);
            }
        }
    }
}
