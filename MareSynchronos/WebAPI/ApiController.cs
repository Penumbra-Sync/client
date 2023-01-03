using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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

public partial class ApiController : IDisposable, IMareHubClient
{
    public const string MainServer = "Lunae Crescere Incipientis (Central Server EU)";
    public const string MainServiceUri = "wss://maresynchronos.com";

    public readonly int[] SupportedServerVersions = { IMareHub.ApiVersion };

    private readonly Configuration _pluginConfiguration;
    private readonly DalamudUtil _dalamudUtil;
    private readonly FileCacheManager _fileDbManager;
    private CancellationTokenSource _connectionCancellationTokenSource;
    private Dictionary<string, string> _jwtToken = new(StringComparer.Ordinal);
    private KeyValuePair<string, string> AuthorizationJwtHeader => new("Authorization", "Bearer " + _jwtToken[SecretKey]);

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
        Task.Run(() => CreateConnections(true));
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

                if (!_jwtToken.TryGetValue(SecretKey, out var jwtToken) || forceGetToken)
                {
                    Logger.Debug("Requesting new JWT token");
                    using HttpClient httpClient = new();
                    var postUri = new Uri(new Uri(ApiUri
                        .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                        .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)), MareAuth.AuthFullPath);
                    using var sha256 = SHA256.Create();
                    var auth = BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(SecretKey))).Replace("-", "", StringComparison.OrdinalIgnoreCase);
                    var result = await httpClient.PostAsync(postUri, new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("auth", auth)
                    })).ConfigureAwait(false);
                    result.EnsureSuccessStatusCode();
                    _jwtToken[SecretKey] = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Logger.Debug("JWT Token Success");
                }

                while (!_dalamudUtil.IsPlayerPresent && !token.IsCancellationRequested)
                {
                    Logger.Debug("Player not loaded in yet, waiting");
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                }

                if (token.IsCancellationRequested) break;

                _mareHub = BuildHubConnection(IMareHub.Path);

                await _mareHub.StartAsync(token).ConfigureAwait(false);

                OnUpdateSystemInfo((dto) => Client_UpdateSystemInfo(dto));

                _connectionDto = await Heartbeat(_dalamudUtil.PlayerNameHashed).ConfigureAwait(false);

                ServerState = ServerState.Connected;

                if (_connectionDto.ServerVersion != IMareHub.ApiVersion)
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
                if (ex.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warn(ex.Message);
                    ServerState = ServerState.Unauthorized;
                    await StopConnection(token).ConfigureAwait(false);
                    return;
                }

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
        _ = Task.Run(() => CreateConnections(false));
        return Task.CompletedTask;
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

    private async Task InitializeData(CancellationToken token)
    {
        if (_mareHub == null) return;

        Logger.Debug("Initializing data");
        OnUserUpdateClientPairs((dto) => Client_UserUpdateClientPairs(dto));
        OnUserChangePairedPlayer((ident, online) => Client_UserChangePairedPlayer(ident, online));
        OnUserReceiveCharacterData((dto, ident) => Client_UserReceiveCharacterData(dto, ident));
        OnGroupChange(async (dto) => await Client_GroupChange(dto).ConfigureAwait(false));
        OnGroupUserChange((dto) => Client_GroupUserChange(dto));

        OnAdminForcedReconnect(() => Client_AdminForcedReconnect());

        PairedClients = await UserGetPairedClients().ConfigureAwait(false);
        Groups = await GroupsGetAll().ConfigureAwait(false);
        GroupPairedClients.Clear();
        foreach (var group in Groups)
        {
            GroupPairedClients.AddRange(await GroupsGetUsersInGroup(group.GID).ConfigureAwait(false));
        }

        if (IsModerator)
        {
            AdminForbiddenFiles = await AdminGetForbiddenFiles().ConfigureAwait(false);
            AdminBannedUsers = await AdminGetBannedUsers().ConfigureAwait(false);
            OnAdminUpdateOrAddBannedUser((dto) => Client_AdminUpdateOrAddBannedUser(dto));
            OnAdminDeleteBannedUser((dto) => Client_AdminDeleteBannedUser(dto));
            OnAdminUpdateOrAddForbiddenFile(dto => Client_AdminUpdateOrAddForbiddenFile(dto));
            OnAdminDeleteForbiddenFile(dto => Client_AdminDeleteForbiddenFile(dto));
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

        _dalamudUtil.LogIn -= DalamudUtilOnLogIn;
        _dalamudUtil.LogOut -= DalamudUtilOnLogOut;

        ServerState = ServerState.Offline;
        Task.Run(async () => await StopConnection(_connectionCancellationTokenSource.Token).ConfigureAwait(false));
        _connectionCancellationTokenSource?.Cancel();
        _healthCheckTokenSource?.Cancel();
        _uploadCancellationTokenSource?.Cancel();
    }

    private HubConnection BuildHubConnection(string hubName)
    {
        return new HubConnectionBuilder()
            .WithUrl(ApiUri + hubName, options =>
            {
                options.Headers.Add(AuthorizationJwtHeader);
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

    public async Task<ConnectionDto> Heartbeat(string characterIdentification)
    {
        return await _mareHub!.InvokeAsync<ConnectionDto>(nameof(Heartbeat), characterIdentification).ConfigureAwait(false);
    }

    public async Task<bool> CheckClientHealth()
    {
        return await _mareHub!.InvokeAsync<bool>(nameof(CheckClientHealth)).ConfigureAwait(false);
    }
}
