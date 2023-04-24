using Dalamud.Interface.Colors;
using Dalamud.Interface;
using MareSynchronos.PlayerData.Pairs;
using System.Numerics;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.WebAPI;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.MareConfiguration;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Handlers;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI.VM;

public class DrawUserPairVM : DrawPairVMBase
{
    private readonly ILogger<DrawUserPairVM> _logger;
    private readonly SelectGroupForPairUi _selectGroupForPairUi;

    public DrawUserPairVM(ILogger<DrawUserPairVM> logger, Pair pair, MareMediator mediator, ApiController apiController, ServerConfigurationManager serverConfigurationManager,
        MareConfigService mareConfigService, SelectGroupForPairUi selectGroupForPairUi)
        : base(pair, apiController, mediator, serverConfigurationManager, mareConfigService)
    {
        _logger = logger;
        _selectGroupForPairUi = selectGroupForPairUi;
    }

    public bool AnimationDisabled => AnimationDisabledFromSource || AnimationDisabledFromTarget;
    public bool AnimationDisabledFromSource => _pair.UserPair!.OwnPermissions.IsDisableAnimations();
    public bool AnimationDisabledFromTarget => _pair.UserPair!.OtherPermissions.IsDisableAnimations();
    public ButtonCommand ChangeAnimationsCommand { get; private set; } = new();
    public ButtonCommand ChangeSoundsCommand { get; private set; } = new();
    public ButtonCommand ChangeVFXCommand { get; private set; } = new();
    public ButtonCommand CyclePauseStateCommand { get; private set; } = new();
    public FlyoutMenuCommand FlyoutMenu { get; private set; } = new();
    public bool HasModifiedPermissions => SoundDisabled || AnimationDisabled;
    public bool OneSidedPair => _pair.UserPair!.OwnPermissions.IsPaired() && !_pair.UserPair!.OtherPermissions.IsPaired();
    public ButtonCommand PauseCommand { get; private set; } = new();
    public ButtonCommand ReloadLastDataCommand { get; private set; } = new();
    public ButtonCommand RemovePairCommand { get; private set; } = new();
    public ButtonCommand SelectPairGroupsCommand { get; private set; } = new();
    public bool SoundDisabled => SoundDisabledFromSource || SoundDisabledFromTarget;
    public bool SoundDisabledFromSource => _pair.UserPair!.OwnPermissions.IsDisableSounds();
    public bool SoundDisabledFromTarget => _pair.UserPair!.OtherPermissions.IsDisableSounds();
    public bool VFXDisabled => VFXDisabledFromSource || VFXDisabledFromTarget;
    public bool VFXDisabledFromSource => _pair.UserPair!.OwnPermissions.IsDisableVFX();
    public bool VFXDisabledFromTarget => _pair.UserPair!.OtherPermissions.IsDisableVFX();

    public (FontAwesomeIcon Icon, Vector4 Color, string PopupText) GetConnection()
    {
        FontAwesomeIcon connectionIcon;
        Vector4 connectionColor;
        string connectionText;
        if (OneSidedPair)
        {
            connectionIcon = FontAwesomeIcon.ArrowUp;
            connectionText = _pair.UserData.AliasOrUID + " has not added you back";
            connectionColor = ImGuiColors.DalamudRed;
        }
        else if (IsPaused)
        {
            connectionIcon = FontAwesomeIcon.PauseCircle;
            connectionText = "Pairing status with " + _pair.UserData.AliasOrUID + " is paused";
            connectionColor = ImGuiColors.DalamudYellow;
        }
        else
        {
            connectionIcon = FontAwesomeIcon.Check;
            connectionText = "You are paired with " + _pair.UserData.AliasOrUID;
            connectionColor = ImGuiColors.ParsedGreen;
        }

        return (connectionIcon, connectionColor, connectionText);
    }

    public (FontAwesomeIcon Icon, Vector4 Color, string PopupText) GetVisibility()
    {
        if (!IsVisible) return (FontAwesomeIcon.None, Vector4.Zero, string.Empty);

        return (FontAwesomeIcon.Eye, ImGuiColors.ParsedGreen, $"{_pair.UserData.AliasOrUID} is visible: {_pair.PlayerName}");
    }

    public void SetPaused(bool pause)
    {
        var perm = _pair.UserPair!.OwnPermissions;
        perm.SetPaused(pause);
        Task.Run(() => _apiController.UserSetPairPermissions(new(_pair.UserData, perm)));
    }

    protected override void SetupCommandsExplicit()
    {
        ReloadLastDataCommand = new ButtonCommand()
            .WithClosePopup()
            .WithState(0, new ButtonCommand.State()
                .WithAction(() => _pair.ApplyLastReceivedData(true))
                .WithVisibility(() => IsVisible)
                .WithText("Reload last data")
                .WithIcon(FontAwesomeIcon.Sync)
                .WithTooltip("This reapplies the last received character data to this character"));

        CyclePauseStateCommand = new ButtonCommand()
            .WithClosePopup()
            .WithState(0, new ButtonCommand.State()
                .WithAction(() =>
                {
                    _logger.LogInformation("Cycling pause");
                    _apiController.CyclePause(_pair.UserData);
                })
                .WithVisibility(() => IsOnline)
                .WithText("Cycle Pause State")
                .WithIcon(FontAwesomeIcon.PlayCircle)
                .WithTooltip($"Cycles the pause state for {DisplayName}"));

        SelectPairGroupsCommand = new ButtonCommand()
            .WithClosePopup()
            .WithState(0, new ButtonCommand.State()
                .WithAction(() => _selectGroupForPairUi.Open(_pair))
                .WithText("Pair Groups")
                .WithIcon(FontAwesomeIcon.Folder)
                .WithVisibility(() => !OneSidedPair)
                .WithTooltip($"Choose Pair Groups for {DisplayName}"));

        ChangeSoundsCommand = new ButtonCommand()
            .WithClosePopup()
            .WithState(0, new ButtonCommand.State()
                .WithAction(() =>
                {
                    var perm = _pair.UserPair!.OwnPermissions;
                    perm.SetDisableSounds(true);
                    _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
                })
                .WithVisibility(() => !OneSidedPair)
                .WithIcon(FontAwesomeIcon.VolumeMute)
                .WithText($"Disable sound sync")
                .WithTooltip($"Disable sound sync with {DisplayName}"))
            .WithState(1, new ButtonCommand.State()
                .WithAction(() =>
                {
                    var perm = _pair.UserPair!.OwnPermissions;
                    perm.SetDisableSounds(false);
                    _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
                })
                .WithVisibility(() => !OneSidedPair)
                .WithIcon(FontAwesomeIcon.VolumeUp)
                .WithText($"Enable sound sync")
                .WithTooltip($"Enable sound sync with {DisplayName}"))
            .WithStateSelector(() => _pair.UserPair!.OwnPermissions.IsDisableSounds() ? 1 : 0);

        ChangeAnimationsCommand = new ButtonCommand()
            .WithClosePopup()
            .WithState(0, new ButtonCommand.State()
                .WithAction(() =>
                {
                    var perm = _pair.UserPair!.OwnPermissions;
                    perm.SetDisableAnimations(true);
                    _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
                })
                .WithVisibility(() => !OneSidedPair)
                .WithIcon(FontAwesomeIcon.Stop)
                .WithText($"Disable animation sync")
                .WithTooltip($"Disable animation sync with {DisplayName}"))
            .WithState(1, new ButtonCommand.State()
                .WithAction(() =>
                {
                    var perm = _pair.UserPair!.OwnPermissions;
                    perm.SetDisableAnimations(false);
                    _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
                })
                .WithVisibility(() => !OneSidedPair)
                .WithIcon(FontAwesomeIcon.Running)
                .WithText($"Enable animation sync")
                .WithTooltip($"Enable animation sync with {DisplayName}"))
            .WithStateSelector(() => _pair.UserPair!.OwnPermissions.IsDisableAnimations() ? 1 : 0);

        ChangeVFXCommand = new ButtonCommand()
            .WithClosePopup()
            .WithState(0, new ButtonCommand.State()
                .WithAction(() =>
                {
                    var perm = _pair.UserPair!.OwnPermissions;
                    perm.SetDisableVFX(true);
                    _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
                })
                .WithVisibility(() => !OneSidedPair)
                .WithIcon(FontAwesomeIcon.Circle)
                .WithText($"Disable VFX sync")
                .WithTooltip($"Disable VFX sync with {DisplayName}"))
            .WithState(1, new ButtonCommand.State()
                .WithAction(() =>
                {
                    var perm = _pair.UserPair!.OwnPermissions;
                    perm.SetDisableVFX(false);
                    _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
                })
                .WithVisibility(() => !OneSidedPair)
                .WithIcon(FontAwesomeIcon.Sun)
                .WithText($"Enable VFX sync")
                .WithTooltip($"Enable VFX sync with {DisplayName}"))
            .WithStateSelector(() => _pair.UserPair!.OwnPermissions.IsDisableVFX() ? 1 : 0);

        PauseCommand = new ButtonCommand()
            .WithClosePopup()
            .WithState(0, new ButtonCommand.State()
                .WithAction(() =>
                {
                    var perm = _pair.UserPair!.OwnPermissions;
                    perm.SetPaused(true);
                    _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
                })
                .WithVisibility(() => !OneSidedPair)
                .WithIcon(FontAwesomeIcon.Pause)
                .WithTooltip($"Pause {DisplayName}"))
            .WithState(1, new ButtonCommand.State()
                .WithAction(() =>
                {
                    var perm = _pair.UserPair!.OwnPermissions;
                    perm.SetPaused(false);
                    _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
                })
                .WithVisibility(() => !OneSidedPair)
                .WithIcon(FontAwesomeIcon.Play)
                .WithTooltip($"Unpause {DisplayName}"))
            .WithStateSelector(() => _pair.UserPair!.OwnPermissions.IsPaused() ? 1 : 0);

        RemovePairCommand = new ButtonCommand()
            .WithClosePopup()
            .WithRequireCtrl()
            .WithState(0, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.Trash)
                .WithAction(() => _ = _apiController.UserRemovePair(new(_pair.UserData)))
                .WithText("Remove Pair")
                .WithTooltip($"Unpair permanently from {DisplayName}"));

        FlyoutMenu = new FlyoutMenuCommand()
            .WithCommand("Data", OpenProfileCommand)
            .WithCommand("Data", ReloadLastDataCommand)
            .WithCommand("Data", CyclePauseStateCommand)
            .WithCommand("Data", SelectPairGroupsCommand)
            .WithCommand("Permissions", ChangeSoundsCommand)
            .WithCommand("Permissions", ChangeAnimationsCommand)
            .WithCommand("Permissions", ChangeVFXCommand)
            .WithCommand("Permissions", RemovePairCommand)
            .WithCommand("Reporting", ReportProfileCommand);
    }
}