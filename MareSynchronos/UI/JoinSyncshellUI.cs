using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

internal class JoinSyncshellUI : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private string _desiredSyncshellToJoin = string.Empty;
    private GroupJoinInfoDto? _groupJoinInfo = null;
    private DefaultPermissionsDto _ownPermissions = null!;
    private string _previousPassword = string.Empty;
    private string _syncshellPassword = string.Empty;

    public JoinSyncshellUI(ILogger<JoinSyncshellUI> logger, MareMediator mediator,
        UiSharedService uiSharedService, ApiController apiController, PerformanceCollectorService performanceCollectorService) 
        : base(logger, mediator, "Join existing Syncshell###MareSynchronosJoinSyncshell", performanceCollectorService)
    {
        _uiSharedService = uiSharedService;
        _apiController = apiController;
        SizeConstraints = new()
        {
            MinimumSize = new(700, 400),
            MaximumSize = new(700, 400)
        };

        Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);

        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize;
    }

    public override void OnOpen()
    {
        _desiredSyncshellToJoin = string.Empty;
        _syncshellPassword = string.Empty;
        _previousPassword = string.Empty;
        _groupJoinInfo = null;
        _ownPermissions = _apiController.DefaultPermissions.DeepClone()!;
    }

    protected override void DrawInternal()
    {
        using (_uiSharedService.UidFont.Push())
            ImGui.TextUnformatted(_groupJoinInfo == null || !_groupJoinInfo.Success ? "Join Syncshell" : "Finalize join Syncshell " + _groupJoinInfo.GroupAliasOrGID);
        ImGui.Separator();

        if (_groupJoinInfo == null || !_groupJoinInfo.Success)
        {
            UiSharedService.TextWrapped("Here you can join existing Syncshells. " +
                "Please keep in mind that you cannot join more than " + _apiController.ServerInfo.MaxGroupsJoinedByUser + " syncshells on this server." + Environment.NewLine +
                "Joining a Syncshell will pair you implicitly with all existing users in the Syncshell." + Environment.NewLine +
                "All permissions to all users in the Syncshell will be set to the preferred Syncshell permissions on joining, excluding prior set preferred permissions.");
            ImGui.Separator();
            ImGui.TextUnformatted("Note: Syncshell ID and Password are case sensitive. MSS- is part of Syncshell IDs, unless using Vanity IDs.");

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Syncshell ID");
            ImGui.SameLine(200);
            ImGui.InputTextWithHint("##syncshellId", "Full Syncshell ID", ref _desiredSyncshellToJoin, 20);

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Syncshell Password");
            ImGui.SameLine(200);
            ImGui.InputTextWithHint("##syncshellpw", "Password", ref _syncshellPassword, 50, ImGuiInputTextFlags.Password);
            using (ImRaii.Disabled(string.IsNullOrEmpty(_desiredSyncshellToJoin) || string.IsNullOrEmpty(_syncshellPassword)))
            {
                if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.Plus, "Join Syncshell"))
                {
                    _groupJoinInfo = _apiController.GroupJoin(new GroupPasswordDto(new API.Data.GroupData(_desiredSyncshellToJoin), _syncshellPassword)).Result;
                    _previousPassword = _syncshellPassword;
                    _syncshellPassword = string.Empty;
                }
            }
            if (_groupJoinInfo != null && !_groupJoinInfo.Success)
            {
                UiSharedService.ColorTextWrapped("Failed to join the Syncshell. This is due to one of following reasons:" + Environment.NewLine +
                    "- The Syncshell does not exist or the password is incorrect" + Environment.NewLine +
                    "- You are already in that Syncshell or are banned from that Syncshell" + Environment.NewLine +
                    "- The Syncshell is at capacity or has invites disabled" + Environment.NewLine, ImGuiColors.DalamudYellow);
            }
        }
        else
        {
            ImGui.TextUnformatted("You are about to join the Syncshell " + _groupJoinInfo.GroupAliasOrGID + " by " + _groupJoinInfo.OwnerAliasOrUID);
            ImGuiHelpers.ScaledDummy(2f);
            ImGui.TextUnformatted("This Syncshell staff has set the following suggested Syncshell permissions:");
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- Sounds ");
            _uiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableSounds());
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- Animations");
            _uiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableAnimations());
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- VFX");
            _uiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableVFX());

            if (_groupJoinInfo.GroupPermissions.IsPreferDisableSounds() != _ownPermissions.DisableGroupSounds
                || _groupJoinInfo.GroupPermissions.IsPreferDisableVFX() != _ownPermissions.DisableGroupVFX
                || _groupJoinInfo.GroupPermissions.IsPreferDisableAnimations() != _ownPermissions.DisableGroupAnimations)
            {
                ImGuiHelpers.ScaledDummy(2f);
                UiSharedService.ColorText("Your current preferred default Syncshell permissions deviate from the suggested permissions:", ImGuiColors.DalamudYellow);
                if (_groupJoinInfo.GroupPermissions.IsPreferDisableSounds() != _ownPermissions.DisableGroupSounds)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("- Sounds");
                    _uiSharedService.BooleanToColoredIcon(!_ownPermissions.DisableGroupSounds);
                    ImGui.SameLine(200);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("Suggested");
                    _uiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableSounds());
                    ImGui.SameLine();
                    using var id = ImRaii.PushId("suggestedSounds");
                    if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.ArrowRight, "Apply suggested"))
                    {
                        _ownPermissions.DisableGroupSounds = _groupJoinInfo.GroupPermissions.IsPreferDisableSounds();
                    }
                }
                if (_groupJoinInfo.GroupPermissions.IsPreferDisableAnimations() != _ownPermissions.DisableGroupAnimations)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("- Animations");
                    _uiSharedService.BooleanToColoredIcon(!_ownPermissions.DisableGroupAnimations);
                    ImGui.SameLine(200);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("Suggested");
                    _uiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableAnimations());
                    ImGui.SameLine();
                    using var id = ImRaii.PushId("suggestedAnims");
                    if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.ArrowRight, "Apply suggested"))
                    {
                        _ownPermissions.DisableGroupAnimations = _groupJoinInfo.GroupPermissions.IsPreferDisableAnimations();
                    }
                }
                if (_groupJoinInfo.GroupPermissions.IsPreferDisableVFX() != _ownPermissions.DisableGroupVFX)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("- VFX");
                    _uiSharedService.BooleanToColoredIcon(!_ownPermissions.DisableGroupVFX);
                    ImGui.SameLine(200);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("Suggested");
                    _uiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableVFX());
                    ImGui.SameLine();
                    using var id = ImRaii.PushId("suggestedVfx");
                    if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.ArrowRight, "Apply suggested"))
                    {
                        _ownPermissions.DisableGroupVFX = _groupJoinInfo.GroupPermissions.IsPreferDisableVFX();
                    }
                }
                UiSharedService.TextWrapped("Note: you do not need to apply the suggested Syncshell permissions, they are solely suggestions by the staff of the Syncshell.");
            }
            else
            {
                UiSharedService.TextWrapped("Your default syncshell permissions on joining are in line with the suggested Syncshell permissions through the owner.");
            }
            ImGuiHelpers.ScaledDummy(2f);
            if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.Plus, "Finalize and join " + _groupJoinInfo.GroupAliasOrGID))
            {
                GroupUserPreferredPermissions joinPermissions = GroupUserPreferredPermissions.NoneSet;
                joinPermissions.SetDisableSounds(_ownPermissions.DisableGroupSounds);
                joinPermissions.SetDisableAnimations(_ownPermissions.DisableGroupAnimations);
                joinPermissions.SetDisableVFX(_ownPermissions.DisableGroupVFX);
                _ = _apiController.GroupJoinFinalize(new GroupJoinDto(_groupJoinInfo.Group, _previousPassword, joinPermissions));
                IsOpen = false;
            }
        }
    }
}