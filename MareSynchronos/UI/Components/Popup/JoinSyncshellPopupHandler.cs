using ImGuiNET;
using MareSynchronos.WebAPI;
using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Dto.Group;
using Dalamud.Interface.Components;
using Dalamud.Interface.Colors;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.Utils;
using MareSynchronos.API.Dto;
using MareSynchronos.API.Data.Enum;

namespace MareSynchronos.UI.Components.Popup;

internal class JoinSyncshellPopupHandler : IPopupHandler
{
    private readonly UiSharedService _uiSharedService;
    private readonly ApiController _apiController;
    private string _desiredSyncshellToJoin = string.Empty;
    private string _syncshellPassword = string.Empty;
    private string _previousPassword = string.Empty;
    private GroupJoinInfoDto? _groupJoinInfo = null;
    private DefaultPermissionsDto _ownPermissions = null!;

    public Vector2 PopupSize => new(700, 400);

    public JoinSyncshellPopupHandler(UiSharedService uiSharedService, ApiController apiController)
    {
        _uiSharedService = uiSharedService;
        _apiController = apiController;
    }

    public void Open()
    {
        _desiredSyncshellToJoin = string.Empty;
        _syncshellPassword = string.Empty;
        _previousPassword = string.Empty;
        _groupJoinInfo = null;
        _ownPermissions = _apiController.DefaultPermissions.DeepClone()!;
    }

    public void DrawContent()
    {
        using (ImRaii.PushFont(_uiSharedService.UidFont))
            ImGui.TextUnformatted((_groupJoinInfo == null || !_groupJoinInfo.Success) ? "Join Syncshell" : ("Finalize join Syncshell " + _groupJoinInfo.GroupAliasOrGID));
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
            ImGui.InputTextWithHint("##syncshellpw", "Password", ref _syncshellPassword, 20, ImGuiInputTextFlags.Password);
            using (ImRaii.Disabled(string.IsNullOrEmpty(_desiredSyncshellToJoin) || string.IsNullOrEmpty(_syncshellPassword)))
            {
                if (ImGuiComponents.IconButtonWithText(Dalamud.Interface.FontAwesomeIcon.Plus, "Join Syncshell"))
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
            ImGui.Dummy(new(2));
            ImGui.TextUnformatted("This Syncshell staff has set the following suggested Syncshell permissions:");
            ImGui.TextUnformatted("- Sounds ");
            UiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableSounds());
            ImGui.TextUnformatted("- Animations");
            UiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableAnimations());
            ImGui.TextUnformatted("- VFX");
            UiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableVFX());

            if (_groupJoinInfo.GroupPermissions.IsPreferDisableSounds() != _ownPermissions.DisableGroupSounds
                || _groupJoinInfo.GroupPermissions.IsPreferDisableVFX() != _ownPermissions.DisableGroupVFX
                || _groupJoinInfo.GroupPermissions.IsPreferDisableAnimations() != _ownPermissions.DisableGroupAnimations)
            {
                ImGui.Dummy(new(2));
                UiSharedService.ColorText("Your current preferred default Syncshell permissions deviate from the suggested permissions:", ImGuiColors.DalamudYellow);
                if (_groupJoinInfo.GroupPermissions.IsPreferDisableSounds() != _ownPermissions.DisableGroupSounds)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("- Sounds");
                    UiSharedService.BooleanToColoredIcon(!_ownPermissions.DisableGroupSounds);
                    ImGui.SameLine(200);
                    ImGui.TextUnformatted("Suggested");
                    UiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableSounds());
                    ImGui.SameLine();
                    using var id = ImRaii.PushId("suggestedSounds");
                    if (ImGuiComponents.IconButtonWithText(Dalamud.Interface.FontAwesomeIcon.ArrowRight, "Apply suggested"))
                    {
                        _ownPermissions.DisableGroupSounds = _groupJoinInfo.GroupPermissions.IsPreferDisableSounds();
                    }
                }
                if (_groupJoinInfo.GroupPermissions.IsPreferDisableAnimations() != _ownPermissions.DisableGroupAnimations)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("- Animations");
                    UiSharedService.BooleanToColoredIcon(!_ownPermissions.DisableGroupAnimations);
                    ImGui.SameLine(200);
                    ImGui.TextUnformatted("Suggested");
                    UiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableAnimations());
                    ImGui.SameLine();
                    using var id = ImRaii.PushId("suggestedAnims");
                    if (ImGuiComponents.IconButtonWithText(Dalamud.Interface.FontAwesomeIcon.ArrowRight, "Apply suggested"))
                    {
                        _ownPermissions.DisableGroupAnimations = _groupJoinInfo.GroupPermissions.IsPreferDisableAnimations();
                    }
                }
                if (_groupJoinInfo.GroupPermissions.IsPreferDisableVFX() != _ownPermissions.DisableGroupVFX)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("- VFX");
                    UiSharedService.BooleanToColoredIcon(!_ownPermissions.DisableGroupVFX);
                    ImGui.SameLine(200);
                    ImGui.TextUnformatted("Suggested");
                    UiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableVFX());
                    ImGui.SameLine();
                    using var id = ImRaii.PushId("suggestedVfx");
                    if (ImGuiComponents.IconButtonWithText(Dalamud.Interface.FontAwesomeIcon.ArrowRight, "Apply suggested"))
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
            ImGui.Dummy(new(2));
            if (ImGuiComponents.IconButtonWithText(Dalamud.Interface.FontAwesomeIcon.Plus, "Finalize and join " + _groupJoinInfo.GroupAliasOrGID))
            {
                GroupUserPreferredPermissions joinPermissions = GroupUserPreferredPermissions.NoneSet;
                joinPermissions.SetDisableSounds(_ownPermissions.DisableGroupSounds);
                joinPermissions.SetDisableAnimations(_ownPermissions.DisableGroupVFX);
                joinPermissions.SetDisableVFX(_ownPermissions.DisableGroupVFX);
                _ = _apiController.GroupJoinFinalize(new GroupJoinDto(_groupJoinInfo.Group, _previousPassword, joinPermissions));
                ImGui.CloseCurrentPopup();
            }
        }
    }
}
