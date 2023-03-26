using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.WebAPI;
using MareSynchronos.UI.VM;
using Dalamud.Interface.Colors;
using Dalamud.Interface;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Extensions;

namespace MareSynchronos.UI.Components;

public abstract class DrawPairVMBase : ImguiVM
{
    protected readonly ApiController _apiController;
    protected readonly MareMediator _mediator;
    protected readonly Pair _pair;
    protected readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly MareConfigService _mareConfigService;

    protected DrawPairVMBase(Pair pair, ApiController apiController, MareMediator mediator, ServerConfigurationManager serverConfigurationManager,
        MareConfigService mareConfigService)
    {
        _pair = pair;
        _apiController = apiController;
        _mediator = mediator;
        _serverConfigurationManager = serverConfigurationManager;
        _mareConfigService = mareConfigService;
        DrawPairId = _pair.UserData.UID + Guid.NewGuid().ToString();
        GroupPairs = new(() => _pair.GroupPair.Select(p => p.Value.GroupAliasOrGID).ToList());
        SetupCommands();
    }

    public string DisplayName => _pair.UserData.AliasOrUID;
    public string DrawPairId { get; }
    public Lazy<List<string>> GroupPairs { get; }
    public bool IsDirectlyPaired => _pair.UserPair != null;

    public bool IsIndirectlyPaired => _pair.GroupPair.Any();

    public bool IsOnline => _pair.IsOnline;

    public bool IsPaused => IsPausedFromSource || IsPausedFromTarget;

    public bool IsPausedFromSource => _pair.UserPair?.OwnPermissions.IsPaused() ?? false;

    public bool IsPausedFromTarget => _pair.UserPair?.OtherPermissions.IsPaused() ?? false;

    public bool IsVisible => _pair.IsVisible;

    public ButtonCommand OpenProfileCommand { get; private set; } = new();

    public string? PlayerName => _pair.PlayerName;

    public ConditionalModal ReportModal { get; protected set; } = new();

    public ButtonCommand ReportProfileCommand { get; private set; } = new();

    public string ReportReason { get; protected set; } = string.Empty;

    public bool ShowReport { get; protected set; } = false;

    public bool ShowUID { get; protected set; } = false;

    public UserData UserData => _pair.UserData;

    public string? GetNote()
    {
        return _serverConfigurationManager.GetNoteForUid(_pair.UserData.UID);
    }

    public (bool IsUid, string Name) GetPlayerText()
    {
        if (ShowUID) return (ShowUID, _pair.UserData.AliasOrUID);
        if (_mareConfigService.Current.ShowCharacterNameInsteadOfNotesForVisible && _pair.IsVisible) return (false, _pair.PlayerName!);

        string? note = _serverConfigurationManager.GetNoteForUid(_pair.UserData.UID);
        if (string.IsNullOrEmpty(note))
        {
            return (true, _pair.UserData.AliasOrUID);
        }

        return (false, note!);
    }

    public void ToggleDisplay()
    {
        ShowUID = !ShowUID;
    }

    internal void SendReport()
    {
        _ = _apiController.UserReportProfile(new(_pair.UserData, ReportReason));
        ReportReason = string.Empty;
        ShowReport = false;
    }

    internal void SetNote(string editUserComment)
    {
        _serverConfigurationManager.SetNoteForUid(_pair.UserData.UID, editUserComment);
        _serverConfigurationManager.SaveNotes();
    }

    protected abstract void SetupCommandsExplicit();

    private void SetupCommands()
    {
        ReportModal = new ConditionalModal()
            .WithTitle("User Profile Report")
            .WithCondition(() => ShowReport)
            .WithOnClose(() => ShowReport = false);

        ReportProfileCommand = new ButtonCommand()
            .WithClosePopup()
            .WithState(0, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.ExclamationTriangle)
                .WithForeground(ImGuiColors.DalamudYellow)
                .WithAction(() => ShowReport = true)
                .WithVisibility(() => !_pair.IsPaused)
                .WithText("Report Mare Profile")
                .WithTooltip($"Report the Mare Profile of {DisplayName} to the administrative team"));

        OpenProfileCommand = new ButtonCommand()
            .WithClosePopup()
            .WithState(0, new ButtonCommand.State()
                .WithAction(() => _mediator.Publish(new ProfileOpenStandaloneMessage(this)))
                .WithVisibility(() => !_pair.IsPaused)
                .WithText("Open Profile")
                .WithIcon(FontAwesomeIcon.User)
                .WithTooltip($"Open Profile for {DisplayName} in a new window"));

        SetupCommandsExplicit();
    }
}