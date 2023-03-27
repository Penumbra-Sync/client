using Dalamud.Interface.Colors;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.SignalR.Utils;
using System.Numerics;
using MareSynchronos.MareConfiguration.Models;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.UI.Handlers;

namespace MareSynchronos.UI.VM;

public sealed class CompactVM : ImguiVM
{
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly MareConfigService _mareConfigService;
    private readonly MareMediator _mediator;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public CompactVM(MareMediator mediator, ApiController apiController, DalamudUtilService dalamudUtilService,
                    MareConfigService mareConfigService, ServerConfigurationManager serverConfigurationManager)
    {
        _mediator = mediator;
        _apiController = apiController;
        _dalamudUtilService = dalamudUtilService;
        _mareConfigService = mareConfigService;
        _serverConfigurationManager = serverConfigurationManager;

        SetupCommands();
    }

    public ButtonCommand AddCurrentUserCommand { get; private set; } = new();

    public ButtonCommand ConnectCommand { get; private set; } = new();

    public ButtonCommand CopyUidCommand { get; private set; } = new();

    public ButtonCommand EditUserProfileCommand { get; private set; } = new();

    public bool IsConnected => _apiController.ServerState is ServerState.Connected;

    public bool IsNoSecretKey => _apiController.ServerState is ServerState.NoSecretKey;

    public bool IsReconnecting => _apiController.ServerState is (ServerState.Reconnecting or ServerState.Disconnecting);

    public bool ManuallyDisconnected => _serverConfigurationManager.CurrentServer!.FullPause;

    public int OnlineUserCount => _apiController.OnlineUsers;

    public ButtonCommand OpenSettingsCommand { get; private set; } = new();

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

    private void AddCurrentCharacter()
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

    private void SetupCommands()
    {
        EditUserProfileCommand = new ButtonCommand()
            .WithState(0, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.UserCircle)
                .WithAction(() => _mediator.Publish(new UiToggleMessage(typeof(EditProfileUi))))
                .WithTooltip("Edit your Mare Profile"));

        ConnectCommand = new ButtonCommand()
            .WithState(0, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.Link)
                .WithAction(ToggleConnection)
                .WithTooltip("Disconnect from " + ServerName)
                .WithForeground(ImGuiColors.HealerGreen)
                .WithEnabled(() => !IsReconnecting))
            .WithState(1, new ButtonCommand.State()
                .WithAction(ToggleConnection)
                .WithIcon(FontAwesomeIcon.Unlink)
                .WithForeground(ImGuiColors.DalamudRed)
                .WithTooltip("Connect to " + ServerName)
                .WithEnabled(() => !IsReconnecting))
            .WithStateSelector(() => ManuallyDisconnected ? 1 : 0);

        CopyUidCommand = new ButtonCommand()
            .WithState(0, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.Copy)
                .WithTooltip("Copy your UID to clipboard")
                .WithAction(() => ImGui.SetClipboardText(_apiController.DisplayName)));

        OpenSettingsCommand = new ButtonCommand()
            .WithState(0, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.Cog)
                .WithTooltip("Open Settings")
                .WithAction(() => _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)))));

        AddCurrentUserCommand = new ButtonCommand()
            .WithState(0, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.Plus)
                .WithText("Add current character with secret key")
                .WithAction(AddCurrentCharacter));
    }

    private void ToggleConnection()
    {
        _serverConfigurationManager.CurrentServer.FullPause = !_serverConfigurationManager.CurrentServer.FullPause;
        _serverConfigurationManager.Save();
        _ = _apiController.CreateConnections();
    }
}