using Dalamud.Interface.Internal.Notifications;
using System.Collections.Concurrent;
using MareSynchronos.API.Routes;
using MareSynchronos.FileCache;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using MareSynchronos.API.Dto;
using MareSynchronos.API.SignalR;
using MareSynchronos.Managers;
using Dalamud.Utility;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Mediator;
using MareSynchronos.Factories;

namespace MareSynchronos.WebAPI;
public partial class ApiController : MediatorSubscriberBase, IDisposable, IMareHubClient
{
    public const string MainServer = "Lunae Crescere Incipientis (Central Server EU)";
    public const string MainServiceUri = "wss://maresynchronos.com";

    private readonly HubFactory _hubFactory;
    private readonly MareConfigService _configService;
    private readonly DalamudUtil _dalamudUtil;
    private readonly FileCacheManager _fileDbManager;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly FileTransferManager _fileTransferManager;
    private CancellationTokenSource _connectionCancellationTokenSource;
    private HubConnection? _mareHub;

    private CancellationTokenSource? _healthCheckTokenSource = new();
    private bool _doNotNotifyOnNextInfo = false;

    private ConnectionDto? _connectionDto;
    public ServerInfo ServerInfo => _connectionDto?.ServerInfo ?? new ServerInfo();
    public string AuthFailureMessage { get; private set; } = string.Empty;
    public SystemInfoDto SystemInfoDto { get; private set; } = new();

    public ApiController(ILogger<ApiController> logger, HubFactory hubFactory, MareConfigService configService, DalamudUtil dalamudUtil, FileCacheManager fileDbManager,
        PairManager pairManager, ServerConfigurationManager serverManager, MareMediator mediator, FileTransferManager fileTransferManager) : base(logger, mediator)
    {
        _logger.LogTrace("Creating " + nameof(ApiController));

        _hubFactory = hubFactory;
        _configService = configService;
        _dalamudUtil = dalamudUtil;
        _fileDbManager = fileDbManager;
        _pairManager = pairManager;
        _serverManager = serverManager;
        _fileTransferManager = fileTransferManager;
        _connectionCancellationTokenSource = new CancellationTokenSource();

        Mediator.Subscribe<DalamudLoginMessage>(this, (_) => DalamudUtilOnLogIn());
        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) => DalamudUtilOnLogOut());
        Mediator.Subscribe<HubClosedMessage>(this, (msg) => MareHubOnClosed(((HubClosedMessage)msg).Exception));
        Mediator.Subscribe<HubReconnectedMessage>(this, (msg) => MareHubOnReconnected(((HubReconnectedMessage)msg).Arg));
        Mediator.Subscribe<HubReconnectingMessage>(this, (msg) => MareHubOnReconnecting(((HubReconnectingMessage)msg).Exception));

        ServerState = ServerState.Offline;

        if (_dalamudUtil.IsLoggedIn)
        {
            DalamudUtilOnLogIn();
        }
    }

    private void DalamudUtilOnLogOut()
    {
        Task.Run(async () => await StopConnection(ServerState.Disconnected).ConfigureAwait(false));
        ServerState = ServerState.Offline;
    }

    private void DalamudUtilOnLogIn()
    {
        Task.Run(() => CreateConnections(forceGetToken: true));
    }

    public ConcurrentDictionary<int, List<DownloadFileTransfer>> CurrentDownloads => _fileTransferManager.CurrentDownloads;

    public List<FileTransfer> CurrentUploads => _fileTransferManager.CurrentUploads;

    public List<FileTransfer> ForbiddenTransfers => _fileTransferManager.ForbiddenTransfers;

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
            _logger.LogDebug("New ServerState: {value}, prev ServerState: {_serverState}", value, _serverState);
            _serverState = value;
        }
    }

    public async Task CreateConnections(bool forceGetToken = false)
    {
        _logger.LogDebug("CreateConnections called");

        _fileTransferManager.Reset();

        if (_serverManager.CurrentServer?.FullPause ?? true)
        {
            _logger.LogInformation("Not recreating Connection, paused");
            _connectionDto = null;
            await StopConnection(ServerState.Disconnected).ConfigureAwait(false);
            _connectionCancellationTokenSource.Cancel();
            return;
        }

        var secretKey = _serverManager.GetSecretKey();
        if (secretKey.IsNullOrEmpty())
        {
            _logger.LogWarning("No secret key set for current character");
            _connectionDto = null;
            await StopConnection(ServerState.NoSecretKey).ConfigureAwait(false);
            _connectionCancellationTokenSource.Cancel();
            return;
        }

        await StopConnection(ServerState.Disconnected).ConfigureAwait(false);

        _logger.LogInformation("Recreating Connection");

        _connectionCancellationTokenSource.Cancel();
        _connectionCancellationTokenSource = new CancellationTokenSource();
        var token = _connectionCancellationTokenSource.Token;
        _fileTransferManager.Clear();
        while (ServerState is not ServerState.Connected && !token.IsCancellationRequested)
        {
            AuthFailureMessage = string.Empty;

            await StopConnection(ServerState.Disconnected).ConfigureAwait(false);
            ServerState = ServerState.Connecting;

            try
            {
                _logger.LogDebug("Building connection");

                if (_serverManager.GetToken() == null || forceGetToken)
                {
                    _logger.LogDebug("Requesting new JWT");
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
                    _logger.LogDebug("JWT Success");
                }

                while (!_dalamudUtil.IsPlayerPresent && !token.IsCancellationRequested)
                {
                    _logger.LogDebug("Player not loaded in yet, waiting");
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                }

                if (token.IsCancellationRequested) break;

                _mareHub = _hubFactory.GetOrCreate();

                await _mareHub.StartAsync(token).ConfigureAwait(false);

                await InitializeData().ConfigureAwait(false);

                _connectionDto = await GetConnectionDto().ConfigureAwait(false);

                _fileTransferManager.SetApiUri(_connectionDto.ServerInfo.FileServerAddress);

                ServerState = ServerState.Connected;

                if (_connectionDto.ServerVersion != IMareHub.ApiVersion)
                {
                    await StopConnection(ServerState.VersionMisMatch).ConfigureAwait(false);
                    return;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "HttpRequestException on Connection");

                if (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    await StopConnection(ServerState.Unauthorized).ConfigureAwait(false);
                    return;
                }

                ServerState = ServerState.Reconnecting;
                _logger.LogInformation("Failed to establish connection, retrying");
                await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 20)), token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception on Connection");

                _logger.LogInformation("Failed to establish connection, retrying");
                await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 20)), token).ConfigureAwait(false);
            }
        }
    }

    private async Task ClientHealthCheck(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _mareHub != null)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            _ = await CheckClientHealth().ConfigureAwait(false);
            _logger.LogDebug("Checked Client Health State");
        }
    }

    private async Task InitializeData()
    {
        if (_mareHub == null) return;

        _logger.LogDebug("Initializing data");
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
            _logger.LogDebug("Individual Pair: {userPair}", userPair);
            _pairManager.AddUserPair(userPair, addToLastAddedUser: false);
        }
        foreach (var entry in await GroupsGetAll().ConfigureAwait(false))
        {
            _logger.LogDebug("Group: {entry}", entry);
            _pairManager.AddGroup(entry);
        }
        foreach (var group in _pairManager.GroupPairs.Keys)
        {
            var users = await GroupsGetUsersInGroup(group).ConfigureAwait(false);
            foreach (var user in users)
            {
                _logger.LogDebug("Group Pair: {user}", user);
                _pairManager.AddGroupPair(user);
            }
        }

        foreach (var entry in await UserGetOnlinePairs().ConfigureAwait(false))
        {
            _pairManager.MarkPairOnline(entry, sendNotif: false);
        }

        _healthCheckTokenSource?.Cancel();
        _healthCheckTokenSource?.Dispose();
        _healthCheckTokenSource = new CancellationTokenSource();
        _ = ClientHealthCheck(_healthCheckTokenSource.Token);

        _initialized = true;
        Mediator.Publish(new ConnectedMessage());
    }

    public override void Dispose()
    {
        base.Dispose();
        _healthCheckTokenSource?.Cancel();
        Task.Run(async () => await StopConnection(ServerState.Disconnected).ConfigureAwait(false));
        _connectionCancellationTokenSource?.Cancel();
    }

    private void MareHubOnClosed(Exception? arg)
    {
        _fileTransferManager.Reset();
        _healthCheckTokenSource?.Cancel();
        Mediator.Publish(new DisconnectedMessage());
        _pairManager.ClearPairs();
        ServerState = ServerState.Offline;
        if (arg != null)
        {
            _logger.LogWarning(arg, "Connection closed");
        }
        else
        {
            _logger.LogInformation("Connection closed");
        }
    }

    private void MareHubOnReconnecting(Exception? arg)
    {
        _doNotNotifyOnNextInfo = true;
        _healthCheckTokenSource?.Cancel();
        ServerState = ServerState.Reconnecting;
        Mediator.Publish(new NotificationMessage("Connection lost", "Connection lost to " + _serverManager.CurrentServer!.ServerName, NotificationType.Warning, 5000));
        _logger.LogWarning(arg, "Connection closed... Reconnecting");
    }

    private async void MareHubOnReconnected(string? arg)
    {
        ServerState = ServerState.Connecting;
        try
        {
            _fileTransferManager.Reset();
            await InitializeData().ConfigureAwait(false);
            _connectionDto = await GetConnectionDto().ConfigureAwait(false);
            if (_connectionDto.ServerVersion != IMareHub.ApiVersion)
            {
                await StopConnection(ServerState.VersionMisMatch).ConfigureAwait(false);
                return;
            }
            _fileTransferManager.SetApiUri(_connectionDto.ServerInfo.FileServerAddress);
            ServerState = ServerState.Connected;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failure to obtain data after reconnection");
            await StopConnection(ServerState.Disconnected).ConfigureAwait(false);
        }

    }

    private async Task StopConnection(ServerState state)
    {
        ServerState = state;

        if (_mareHub is not null)
        {
            _initialized = false;
            _healthCheckTokenSource?.Cancel();
            _fileTransferManager.Reset();
            _logger.LogInformation("Stopping existing connection");
            await _hubFactory.DisposeHubAsync().ConfigureAwait(false);
            CurrentUploads.Clear();
            CurrentDownloads.Clear();
            Mediator.Publish(new DisconnectedMessage());
            _mareHub = null;
            _connectionDto = null;
        }
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
