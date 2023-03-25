using Dalamud.Interface.Colors;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.Components;
using MareSynchronos.WebAPI;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;
using System.Numerics;
using MareSynchronos.MareConfiguration.Models;

namespace MareSynchronos.UI.VM;

public sealed class CompactVM : ImguiVM
{
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly Func<string, Pair, DrawUserPair> _drawUserPairFactory;
    private readonly MareConfigService _mareConfigService;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    private string _characterFilter = string.Empty;

    public CompactVM(ILogger<CompactVM> logger, MareMediator mediator, PairManager pairManager, ApiController apiController, DalamudUtilService dalamudUtilService,
                MareConfigService mareConfigService, Func<string, Pair, DrawUserPair> drawUserPairFactory, ServerConfigurationManager serverConfigurationManager) : base(logger, mediator)
    {
        _pairManager = pairManager;
        _apiController = apiController;
        _dalamudUtilService = dalamudUtilService;
        _mareConfigService = mareConfigService;
        _drawUserPairFactory = drawUserPairFactory;
        _serverConfigurationManager = serverConfigurationManager;

        Mediator.Subscribe<PairManagerUpdateMessage>(this, OnPairManagerUpdate);

        RecreateLazy();
    }

    public bool CanAddPair => !_pairManager.DirectPairs.Any(p => string.Equals(p.UserData.UID, PairToAdd, StringComparison.Ordinal) || string.Equals(p.UserData.Alias, PairToAdd, StringComparison.Ordinal));

    public string CharacterOrCommentFilter
    {
        get => _characterFilter;
        private set
        {
            _characterFilter = value;
            RecreateLazy();
        }
    }

    public Lazy<List<Pair>> FilteredUsers { get; private set; } = new();
    public bool IsConnected => _apiController.ServerState is ServerState.Connected;
    public bool IsNoSecretKey => _apiController.ServerState is ServerState.NoSecretKey;
    public bool IsReconnecting => _apiController.ServerState is (ServerState.Reconnecting or ServerState.Disconnecting);
    public Pair? LastAddedUser { get; private set; }
    public string LastAddedUserComment { get; private set; } = string.Empty;
    public bool ManuallyDisconnected => _serverConfigurationManager.CurrentServer!.FullPause;
    public Lazy<List<DrawUserPair>> OfflineUsers { get; private set; } = new();
    public int OnlineUserCount => _apiController.OnlineUsers;
    public Lazy<List<DrawUserPair>> OnlineUsers { get; private set; } = new();
    public bool OpenPopupOnAdd => _mareConfigService.Current.OpenPopupOnAdd;
    public string PairToAdd { get; private set; } = string.Empty;

    public bool ReverseUserSort
    {
        get => _mareConfigService.Current.ReverseUserSort;
        set
        {
            _mareConfigService.Current.ReverseUserSort = value;
            _mareConfigService.Save();
        }
    }

    public int SecretKeyIdx { get; set; } = 0;

    public Dictionary<int, SecretKey> SecretKeys => _serverConfigurationManager.CurrentServer!.SecretKeys;

    public string ServerName => _serverConfigurationManager.CurrentServer.ServerName;

    public string ShardString =>
#if DEBUG
                $"Shard: {_apiController.ServerInfo.ShardName}"
#else
    string.Equals(_apiController.ServerInfo.ShardName, "Main", StringComparison.OrdinalIgnoreCase) ? string.Empty : $"Shard: {_apiController.ServerInfo.ShardName}"
#endif
    ;

    public bool ShowCharacterNameInsteadOfNotesForVisible => _mareConfigService.Current.ShowCharacterNameInsteadOfNotesForVisible;
    public (Version Version, bool IsCurrent) Version => (_apiController.CurrentClientVersion, _apiController.IsCurrentVersion);
    public Lazy<List<DrawUserPair>> VisibleUsers { get; private set; } = new();

    public void AddPair()
    {
        _ = _apiController.UserAddPair(new(new(PairToAdd)));
        PairToAdd = string.Empty;
    }

    public bool CheckLastAddedUser()
    {
        if (LastAddedUser != null) return true;
        if (_pairManager.LastAddedUser == null) return false;

        LastAddedUser = _pairManager.LastAddedUser;
        _pairManager.LastAddedUser = null;
        LastAddedUserComment = string.Empty;

        return true;
    }

    public string GetServerError()
    {
        return _apiController.ServerState switch
        {
            ServerState.Connecting => "Attempting to connect to the server.",
            ServerState.Reconnecting => "Connection to server interrupted, attempting to reconnect to the server.",
            ServerState.Disconnected => "You are currently disconnected from the Mare Synchronos server.",
            ServerState.Disconnecting => "Disconnecting from the server",
            ServerState.Unauthorized => "Server Response: " + _apiController.AuthFailureMessage,
            ServerState.Offline => "Your selected Mare Synchronos server is currently offline.",
            ServerState.VersionMisMatch =>
                "Your plugin or the server you are connecting to is out of date. Please update your plugin now. If you already did so, contact the server provider to update their server to the latest version.",
            ServerState.RateLimited => "You are rate limited for (re)connecting too often. Disconnect, wait 10 minutes and try again.",
            ServerState.Connected => string.Empty,
            ServerState.NoSecretKey => "You have no secret key set for this current character. Use the button below or open the settings and set a secret key for the current character. You can reuse the same secret key for multiple characters.",
            _ => string.Empty
        };
    }

    public Vector4 GetUidColor()
    {
        return _apiController.ServerState switch
        {
            ServerState.Connecting => ImGuiColors.DalamudYellow,
            ServerState.Reconnecting => ImGuiColors.DalamudRed,
            ServerState.Connected => ImGuiColors.ParsedGreen,
            ServerState.Disconnected => ImGuiColors.DalamudYellow,
            ServerState.Disconnecting => ImGuiColors.DalamudYellow,
            ServerState.Unauthorized => ImGuiColors.DalamudRed,
            ServerState.VersionMisMatch => ImGuiColors.DalamudRed,
            ServerState.Offline => ImGuiColors.DalamudRed,
            ServerState.RateLimited => ImGuiColors.DalamudYellow,
            ServerState.NoSecretKey => ImGuiColors.DalamudYellow,
            _ => ImGuiColors.DalamudRed
        };
    }

    public string GetUidText()
    {
        return _apiController.ServerState switch
        {
            ServerState.Reconnecting => "Reconnecting",
            ServerState.Connecting => "Connecting",
            ServerState.Disconnected => "Disconnected",
            ServerState.Disconnecting => "Disconnecting",
            ServerState.Unauthorized => "Unauthorized",
            ServerState.VersionMisMatch => "Version mismatch",
            ServerState.Offline => "Unavailable",
            ServerState.RateLimited => "Rate Limited",
            ServerState.NoSecretKey => "No Secret Key",
            ServerState.Connected => _apiController.DisplayName,
            _ => string.Empty
        };
    }

    public void SetNoteForLastAddedUser()
    {
        if (LastAddedUser == null) return;
        _serverConfigurationManager.SetNoteForUid(LastAddedUser.UserData.UID, LastAddedUserComment);
        LastAddedUser = null;
        LastAddedUserComment = string.Empty;
    }

    internal void AddCurrentCharacter()
    {
        _serverConfigurationManager.CurrentServer!.Authentications.Add(new MareConfiguration.Models.Authentication()
        {
            CharacterName = _dalamudUtilService.PlayerName,
            WorldId = _dalamudUtilService.WorldId,
            SecretKeyIdx = SecretKeyIdx
        });

        _serverConfigurationManager.Save();

        _ = _apiController.CreateConnections(forceGetToken: true);
    }

    internal void ToggleConnection()
    {
        _serverConfigurationManager.CurrentServer.FullPause = !_serverConfigurationManager.CurrentServer.FullPause;
        _serverConfigurationManager.Save();
        _ = _apiController.CreateConnections();
    }

    private Lazy<List<Pair>> FilteredUserLazy() => new Lazy<List<Pair>>(_pairManager.DirectPairs.Where(p =>
                                        {
                                            if (string.IsNullOrEmpty(CharacterOrCommentFilter)) return true;
                                            return p.UserData.AliasOrUID.Contains(CharacterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ||
                                                    (p.GetNote()?.Contains(CharacterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                                    (p.PlayerName?.Contains(CharacterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ?? false);
                                        }).ToList());

    private void OnPairManagerUpdate(PairManagerUpdateMessage obj)
    {
        RecreateLazy();
    }

    private void RecreateLazy()
    {
        Logger.LogTrace("Recreating UI Pairs Lazily");

        FilteredUsers = FilteredUserLazy();

        var users = FilteredUsers.Value
            .OrderBy(
                u => ShowCharacterNameInsteadOfNotesForVisible && !string.IsNullOrEmpty(u.PlayerName)
                    ? u.PlayerName
                    : (u.GetNote() ?? u.UserData.AliasOrUID), StringComparer.OrdinalIgnoreCase).ToList();

        if (ReverseUserSort)
        {
            users.Reverse();
        }

        OnlineUsers = new Lazy<List<DrawUserPair>>(users.Where(u => u.IsOnline || u.UserPair!.OwnPermissions.IsPaused())
            .Select(c => _drawUserPairFactory("Online" + c.UserData.UID, c)).ToList());
        VisibleUsers = new Lazy<List<DrawUserPair>>(users.Where(u => u.IsVisible)
            .Select(c => _drawUserPairFactory("Visible" + c.UserData.UID, c)).ToList());
        OfflineUsers = new Lazy<List<DrawUserPair>>(users.Where(u => !u.IsOnline && !u.UserPair!.OwnPermissions.IsPaused())
            .Select(c => _drawUserPairFactory("Offline" + c.UserData.UID, c)).ToList());
    }
}