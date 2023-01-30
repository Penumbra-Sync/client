using System.Collections.Concurrent;
using MareSynchronos.API.Routes;
using MareSynchronos.FileCache;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using MareSynchronos.API.Dto;
using MareSynchronos.API.SignalR;
using MareSynchronos.Managers;
using Dalamud.Utility;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Delegates;
using MareSynchronos.Mediator;

namespace MareSynchronos.WebAPI;
public partial class ApiController : IDisposable, IMareHubClient
{
    public const string MainServer = "Lunae Crescere Incipientis (Central Server EU)";
    public const string MainServiceUri = "wss://maresynchronos.com";

    public readonly int[] SupportedServerVersions = { IMareHub.ApiVersion };

    private readonly ConfigurationService _configService;
    private readonly DalamudUtil _dalamudUtil;
    private readonly FileCacheManager _fileDbManager;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly MareMediator _mediator;
    private CancellationTokenSource _connectionCancellationTokenSource;
    private HubConnection? _mareHub;

    private CancellationTokenSource? _uploadCancellationTokenSource = new();
    private CancellationTokenSource? _healthCheckTokenSource = new();

    private ConnectionDto? _connectionDto;
    public ServerInfo ServerInfo => _connectionDto?.ServerInfo ?? new ServerInfo();
    public string AuthFailureMessage { get; private set; } = string.Empty;

    public SystemInfoDto SystemInfoDto { get; private set; } = new();
    public bool IsModerator => (_connectionDto?.IsAdmin ?? false) || (_connectionDto?.IsModerator ?? false);

    public bool IsAdmin => _connectionDto?.IsAdmin ?? false;

    private HttpClient _httpClient;

    public ApiController(ConfigurationService configService, DalamudUtil dalamudUtil, FileCacheManager fileDbManager, PairManager pairManager, ServerConfigurationManager serverManager, MareMediator mediator)
    {
        Logger.Verbose("Creating " + nameof(ApiController));

        _configService = configService;
        _dalamudUtil = dalamudUtil;
        _fileDbManager = fileDbManager;
        _pairManager = pairManager;
        _serverManager = serverManager;
        _mediator = mediator;
        _connectionCancellationTokenSource = new CancellationTokenSource();

        _mediator.Subscribe<DalamudLoginMessage>(this, (_) => DalamudUtilOnLogIn());
        _mediator.Subscribe<DalamudLogoutMessage>(this, (_) => DalamudUtilOnLogOut());
        ServerState = ServerState.Offline;
        _verifiedUploadedHashes = new(StringComparer.Ordinal);
        _httpClient = new();

        if (_dalamudUtil.IsLoggedIn)
        {
            DalamudUtilOnLogIn();
        }
    }

    private void DalamudUtilOnLogOut()
    {
        Task.Run(async () => await StopConnection(_connectionCancellationTokenSource.Token, ServerState.Disconnected).ConfigureAwait(false));
        ServerState = ServerState.Offline;
    }

    private void DalamudUtilOnLogIn()
    {
        Task.Run(() => CreateConnections(forceGetToken: true));
    }

    public event VoidDelegate? Connected;
    public event VoidDelegate? Disconnected;
    public event VoidDelegate? DownloadStarted;
    public event VoidDelegate? DownloadFinished;

    public ConcurrentDictionary<int, List<DownloadFileTransfer>> CurrentDownloads { get; } = new();

    public List<FileTransfer> CurrentUploads { get; } = new();

    public List<FileTransfer> ForbiddenTransfers { get; } = new();

    public bool IsConnected => ServerState == ServerState.Connected;
    public bool IsDownloading => !CurrentDownloads.IsEmpty;
    public bool IsUploading => CurrentUploads.Count > 0;

    public bool ServerAlive => ServerState is ServerState.Connected or ServerState.RateLimited or ServerState.Unauthorized or ServerState.Disconnected;

    public string UID => _connectionDto?.User.UID ?? string.Empty;
    public string DisplayName => _connectionDto?.User.AliasOrUID ?? string.Empty;
    public int OnlineUsers => SystemInfoDto.OnlineUsers;

    private ServerState _serverState;
    private bool _initialized;

    public ServerState ServerState
    {
        get => _serverState;
        private set
        {
            Logger.Debug($"New ServerState: {value}, prev ServerState: {_serverState}");
            _serverState = value;
        }
    }

    public async Task CreateConnections(bool forceGetToken = false)
    {
        Logger.Debug("CreateConnections called");

        _httpClient?.Dispose();
        _httpClient = new();

        if (_serverManager.CurrentServer?.FullPause ?? true)
        {
            Logger.Info("Not recreating Connection, paused");
            _connectionDto = null;
            await StopConnection(_connectionCancellationTokenSource.Token, ServerState.Disconnected).ConfigureAwait(false);
            return;
        }

        var secretKey = _serverManager.GetSecretKey();
        if (secretKey.IsNullOrEmpty())
        {
            Logger.Warn("No secret key set for current character");
            _connectionDto = null;
            await StopConnection(_connectionCancellationTokenSource.Token, ServerState.NoSecretKey).ConfigureAwait(false);
            return;
        }

        await StopConnection(_connectionCancellationTokenSource.Token, ServerState.Disconnected).ConfigureAwait(false);

        Logger.Info("Recreating Connection");

        _connectionCancellationTokenSource.Cancel();
        _connectionCancellationTokenSource = new CancellationTokenSource();
        var token = _connectionCancellationTokenSource.Token;
        _verifiedUploadedHashes.Clear();
        while (ServerState is not ServerState.Connected && !token.IsCancellationRequested)
        {
            AuthFailureMessage = string.Empty;

            await StopConnection(token, ServerState.Disconnected).ConfigureAwait(false);
            ServerState = ServerState.Connecting;

            try
            {
                Logger.Debug("Building connection");

                if (_serverManager.GetToken() == null || forceGetToken)
                {
                    Logger.Debug("Requesting new JWT");
                    using HttpClient httpClient = new();
                    var postUri = MareAuth.AuthFullPath(new Uri(_serverManager.CurrentApiUrl
                        .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                        .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));
                    var auth = secretKey.GetHash256();
                    var result = await httpClient.PostAsync(postUri, new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("auth", auth),
                        new KeyValuePair<string, string>("charaIdent", _dalamudUtil.PlayerNameHashed),
                    })).ConfigureAwait(false);
                    AuthFailureMessage = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
                    result.EnsureSuccessStatusCode();
                    _serverManager.SaveToken(await result.Content.ReadAsStringAsync().ConfigureAwait(false));
                    Logger.Debug("JWT Success");
                }

                while (!_dalamudUtil.IsPlayerPresent && !token.IsCancellationRequested)
                {
                    Logger.Debug("Player not loaded in yet, waiting");
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                }

                if (token.IsCancellationRequested) break;

                _mareHub = BuildHubConnection(IMareHub.Path);

                await _mareHub.StartAsync(token).ConfigureAwait(false);

                await InitializeData().ConfigureAwait(false);

                _connectionDto = await GetConnectionDto().ConfigureAwait(false);

                ServerState = ServerState.Connected;

                if (_connectionDto.ServerVersion != IMareHub.ApiVersion)
                {
                    await StopConnection(token, ServerState.VersionMisMatch).ConfigureAwait(false);
                    return;
                }

                if (ServerState is ServerState.Connected) // user is authorized && server is legit
                {
                    _mareHub.Closed += MareHubOnClosed;
                    _mareHub.Reconnecting += MareHubOnReconnecting;
                    _mareHub.Reconnected += MareHubOnReconnected;
                }
            }
            catch (HttpRequestException ex)
            {
                Logger.Warn(ex.GetType().ToString());
                Logger.Warn(ex.Message);
                Logger.Warn(ex.StackTrace ?? string.Empty);

                if (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    await StopConnection(token, ServerState.Unauthorized).ConfigureAwait(false);
                    return;
                }
                else
                {
                    ServerState = ServerState.Reconnecting;
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

    private async Task ClientHealthCheck(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _mareHub != null)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) break;
            var needsRestart = await CheckClientHealth().ConfigureAwait(false);
            Logger.Debug("Checked Client Health State, healthy: " + !needsRestart);
            if (needsRestart)
            {
                ServerState = ServerState.Offline;
                _ = CreateConnections();
            }
        }
    }

    private async Task InitializeData()
    {
        if (_mareHub == null) return;

        Logger.Debug("Initializing data");
        OnDownloadReady((guid) => Client_DownloadReady(guid));
        OnReceiveServerMessage((sev, msg) => Client_ReceiveServerMessage(sev, msg));
        OnUpdateSystemInfo((dto) => Client_UpdateSystemInfo(dto));

        OnUserSendOffline((dto) => Client_UserSendOffline(dto));
        OnUserAddClientPair((dto) => Client_UserAddClientPair(dto));
        OnUserReceiveCharacterData((dto) => Client_UserReceiveCharacterData(dto));
        OnUserRemoveClientPair(dto => Client_UserRemoveClientPair(dto));
        OnUserSendOnline(dto => Client_UserSendOnline(dto));
        OnUserUpdateOtherPairPermissions(dto => Client_UserUpdateOtherPairPermissions(dto));
        OnUserUpdateSelfPairPermissions(dto => Client_UserUpdateSelfPairPermissions(dto));

        OnGroupChangePermissions((dto) => Client_GroupChangePermissions(dto));
        OnGroupDelete((dto) => Client_GroupDelete(dto));
        OnGroupPairChangePermissions((dto) => Client_GroupPairChangePermissions(dto));
        OnGroupPairChangeUserInfo((dto) => Client_GroupPairChangeUserInfo(dto));
        OnGroupPairJoined((dto) => Client_GroupPairJoined(dto));
        OnGroupPairLeft((dto) => Client_GroupPairLeft(dto));
        OnGroupSendFullInfo((dto) => Client_GroupSendFullInfo(dto));
        OnGroupSendInfo((dto) => Client_GroupSendInfo(dto));

        foreach (var userPair in await UserGetPairedClients().ConfigureAwait(false))
        {
            Logger.Debug($"Pair: {userPair}");
            _pairManager.AddUserPair(userPair, addToLastAddedUser: false);
        }
        foreach (var entry in await GroupsGetAll().ConfigureAwait(false))
        {
            Logger.Debug($"Group: {entry}");
            _pairManager.AddGroup(entry);
        }
        foreach (var group in _pairManager.GroupPairs.Keys)
        {
            var users = await GroupsGetUsersInGroup(group).ConfigureAwait(false);
            foreach (var user in users)
            {
                Logger.Debug($"GroupPair: {user}");
                _pairManager.AddGroupPair(user);
            }
        }

        foreach (var entry in await UserGetOnlinePairs().ConfigureAwait(false))
        {
            _pairManager.MarkPairOnline(entry, this);
        }

        _healthCheckTokenSource?.Cancel();
        _healthCheckTokenSource?.Dispose();
        _healthCheckTokenSource = new CancellationTokenSource();
        _ = ClientHealthCheck(_healthCheckTokenSource.Token);

        _initialized = true;
        Connected?.Invoke();
    }

    public void Dispose()
    {
        Logger.Verbose("Disposing " + nameof(ApiController));

        ServerState = ServerState.Offline;
        Task.Run(async () => await StopConnection(_connectionCancellationTokenSource.Token, ServerState.Disconnected).ConfigureAwait(false));
        _connectionCancellationTokenSource?.Cancel();
        _healthCheckTokenSource?.Cancel();
        _uploadCancellationTokenSource?.Cancel();
    }

    private HubConnection BuildHubConnection(string hubName)
    {
        return new HubConnectionBuilder()
            .WithUrl(_serverManager.CurrentApiUrl + hubName, options =>
            {
                options.Headers.Add("Authorization", "Bearer " + _serverManager.GetToken());
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
        _healthCheckTokenSource?.Cancel();
        Disconnected?.Invoke();
        _pairManager.ClearPairs();
        ServerState = ServerState.Offline;
        Logger.Info("Connection closed");
        return Task.CompletedTask;
    }

    private Task MareHubOnReconnecting(Exception? arg)
    {
        _connectionDto = null;
        _healthCheckTokenSource?.Cancel();
        ServerState = ServerState.Reconnecting;
        Logger.Warn("Connection closed... Reconnecting");
        Logger.Warn(arg?.Message ?? string.Empty);
        Logger.Warn(arg?.StackTrace ?? string.Empty);
        Disconnected?.Invoke();
        _pairManager.ClearPairs();
        return Task.CompletedTask;
    }

    private async Task MareHubOnReconnected(string? arg)
    {
        ServerState = ServerState.Connecting;
        await InitializeData().ConfigureAwait(false);
        _connectionDto = await GetConnectionDto().ConfigureAwait(false);
        ServerState = ServerState.Connected;
    }

    private async Task StopConnection(CancellationToken token, ServerState state)
    {
        ServerState = state;

        if (_mareHub is not null)
        {
            _initialized = false;
            _healthCheckTokenSource?.Cancel();
            _uploadCancellationTokenSource?.Cancel();
            Logger.Info("Stopping existing connection");
            _mareHub.Closed -= MareHubOnClosed;
            _mareHub.Reconnecting -= MareHubOnReconnecting;
            _mareHub.Reconnected -= MareHubOnReconnected;
            await _mareHub.StopAsync(token).ConfigureAwait(false);
            await _mareHub.DisposeAsync().ConfigureAwait(false);
            CurrentUploads.Clear();
            CurrentDownloads.Clear();
            Disconnected?.Invoke();
            _pairManager.ClearPairs();
            _mareHub = null;
        }
    }

    public async Task<ConnectionDto> Heartbeat(string characterIdentification)
    {
        return await _mareHub!.InvokeAsync<ConnectionDto>(nameof(Heartbeat), characterIdentification).ConfigureAwait(false);
    }

    public async Task<ConnectionDto> GetConnectionDto()
    {
        return await _mareHub!.InvokeAsync<ConnectionDto>(nameof(GetConnectionDto)).ConfigureAwait(false);
    }

    public async Task<bool> CheckClientHealth()
    {
        return await _mareHub!.InvokeAsync<bool>(nameof(CheckClientHealth)).ConfigureAwait(false);
    }
}
