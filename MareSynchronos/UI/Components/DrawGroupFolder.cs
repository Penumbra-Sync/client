using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components;

public class DrawGroupFolder : DrawFolderBase
{
    private readonly ApiController _apiController;
    private readonly IdDisplayHandler _idDisplayHandler;
    private readonly MareMediator _mareMediator;
    private readonly GroupFullInfoDto _groupFullInfoDto;
    private bool IsOwner => string.Equals(_groupFullInfoDto.OwnerUID, _apiController.UID, StringComparison.Ordinal);
    private bool IsModerator => IsOwner || _groupFullInfoDto.GroupPairUserInfos.TryGetValue(_apiController.UID, out var info) && info.IsModerator();
    private bool IsPinned => _groupFullInfoDto.GroupPairUserInfos.TryGetValue(_apiController.UID, out var info) && info.IsPinned();
    protected override bool RenderIfEmpty => true;
    protected override bool RenderMenu => true;

    public DrawGroupFolder(string id, GroupFullInfoDto groupFullInfoDto, ApiController apiController,
        IEnumerable<DrawGroupPair> drawPairs, TagHandler tagHandler, IdDisplayHandler idDisplayHandler,
        MareMediator mareMediator) :
        base(id, drawPairs, tagHandler)
    {
        _groupFullInfoDto = groupFullInfoDto;
        _apiController = apiController;
        _idDisplayHandler = idDisplayHandler;
        _mareMediator = mareMediator;
    }

    protected override float DrawIcon(float textPosY, float originalY)
    {
        ImGui.SetCursorPosY(textPosY);
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextUnformatted(_groupFullInfoDto.GroupPermissions.IsDisableInvites() ? FontAwesomeIcon.Lock.ToIconString() : FontAwesomeIcon.Users.ToIconString());
        if (_groupFullInfoDto.GroupPermissions.IsDisableInvites())
        {
            UiSharedService.AttachToolTip("Syncshell " + _groupFullInfoDto.GroupAliasOrGID + " is closed for invites");
        }
        if (IsOwner)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            using (ImRaii.PushFont(UiBuilder.IconFont))
                ImGui.TextUnformatted(FontAwesomeIcon.Crown.ToIconString());
            UiSharedService.AttachToolTip("You are the owner of " + _groupFullInfoDto.GroupAliasOrGID);

        }
        else if (IsModerator)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            using (ImRaii.PushFont(UiBuilder.IconFont))
                ImGui.TextUnformatted(FontAwesomeIcon.UserShield.ToIconString());
            UiSharedService.AttachToolTip("You are a moderator in " + _groupFullInfoDto.GroupAliasOrGID);
        }
        else if (IsPinned)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            using (ImRaii.PushFont(UiBuilder.IconFont))
                ImGui.TextUnformatted(FontAwesomeIcon.Thumbtack.ToIconString());
            UiSharedService.AttachToolTip("You are pinned in " + _groupFullInfoDto.GroupAliasOrGID);
        }
        ImGui.SameLine();
        return ImGui.GetCursorPosX();
    }

    protected override void DrawMenu()
    {
        ImGui.TextUnformatted("Syncshell Menu (" + _groupFullInfoDto.GroupAliasOrGID + ")");
        ImGui.Separator();

        ImGui.TextUnformatted("General Syncshell Actions");
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, "Copy ID"))
        {
            ImGui.CloseCurrentPopup();
            ImGui.SetClipboardText(_groupFullInfoDto.GroupAliasOrGID);
        }
        UiSharedService.AttachToolTip("Copy Syncshell ID to Clipboard");

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.StickyNote, "Copy Notes"))
        {
            ImGui.CloseCurrentPopup();
            ImGui.SetClipboardText(UiSharedService.GetNotes(_drawPairs.Select(k => k.Pair).ToList()));
        }
        UiSharedService.AttachToolTip("Copies all your notes for all users in this Syncshell to the clipboard." + Environment.NewLine + "They can be imported via Settings -> Privacy -> Import Notes from Clipboard");

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ArrowCircleLeft, "Leave Syncshell") && UiSharedService.CtrlPressed())
        {
            _ = _apiController.GroupLeave(_groupFullInfoDto);
        }
        UiSharedService.AttachToolTip("Hold CTRL and click to leave this Syncshell" + (!string.Equals(_groupFullInfoDto.OwnerUID, _apiController.UID, StringComparison.Ordinal)
            ? string.Empty : Environment.NewLine + "WARNING: This action is irreversible" + Environment.NewLine + "Leaving an owned Syncshell will transfer the ownership to a random person in the Syncshell."));

        ImGui.Separator();
        ImGui.TextUnformatted("Permission Settings");
        var perm = _groupFullInfoDto.GroupUserPermissions;
        bool disableSounds = perm.IsDisableSounds();
        bool disableAnims = perm.IsDisableAnimations();
        bool disableVfx = perm.IsDisableVFX();
        if (ImGuiComponents.IconButtonWithText(disableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeOff, disableSounds ? "Enable Sound Sync" : "Disable Sound Sync"))
        {
            perm.SetDisableSounds(!disableSounds);
            _ = _apiController.GroupChangeIndividualPermissionState(new(_groupFullInfoDto.Group, new(_apiController.UID), perm));
        }

        if (ImGuiComponents.IconButtonWithText(disableAnims ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop, disableAnims ? "Enable Animation Sync" : "Disable Animation Sync"))
        {
            perm.SetDisableAnimations(!disableAnims);
            _ = _apiController.GroupChangeIndividualPermissionState(new(_groupFullInfoDto.Group, new(_apiController.UID), perm));
        }

        if (ImGuiComponents.IconButtonWithText(disableVfx ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle, disableVfx ? "Enable VFX Sync" : "Disable VFX Sync"))
        {
            perm.SetDisableVFX(!disableVfx);
            _ = _apiController.GroupChangeIndividualPermissionState(new(_groupFullInfoDto.Group, new(_apiController.UID), perm));
        }

        if (IsModerator || IsOwner)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Syncshell Admin Functions");
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Cog, "Open Admin Panel"))
            {
                ImGui.CloseCurrentPopup();
                _mareMediator.Publish(new OpenSyncshellAdminPanelPopupMessage(_groupFullInfoDto));
            }
        }
    }

    protected override void DrawName(float originalY, float width)
    {
        _idDisplayHandler.DrawGroupText(_id, _groupFullInfoDto, ImGui.GetCursorPosX(), originalY, () => width);
    }

    protected override float DrawRightSide(float originalY, float currentRightSideX)
    {
        var spacingX = ImGui.GetStyle().ItemSpacing.X;

        FontAwesomeIcon pauseIcon = _groupFullInfoDto.GroupUserPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseButtonSize = UiSharedService.GetIconButtonSize(pauseIcon);

        var folderIcon = FontAwesomeIcon.UsersCog;
        var userCogButtonSize = UiSharedService.GetIconSize(folderIcon);

        var individualSoundsDisabled = _groupFullInfoDto.GroupUserPermissions.IsDisableSounds();
        var individualAnimDisabled = _groupFullInfoDto.GroupUserPermissions.IsDisableAnimations();
        var individualVFXDisabled = _groupFullInfoDto.GroupUserPermissions.IsDisableVFX();

        var infoIconPosDist = currentRightSideX - pauseButtonSize.X - spacingX;

        ImGui.SameLine(infoIconPosDist - userCogButtonSize.X);

        UiSharedService.FontText(folderIcon.ToIconString(), UiBuilder.IconFont);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();

            ImGui.TextUnformatted("Syncshell Permissions");
            ImGui.Separator();

            UiSharedService.BooleanToColoredIcon(!individualSoundsDisabled, false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.TextUnformatted("Sound Sync");

            UiSharedService.BooleanToColoredIcon(!individualAnimDisabled, false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.TextUnformatted("Animation Sync");

            UiSharedService.BooleanToColoredIcon(!individualVFXDisabled, false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.TextUnformatted("VFX Sync");

            ImGui.EndTooltip();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(pauseIcon))
        {
            var perm = _groupFullInfoDto.GroupUserPermissions;
            perm.SetPaused(!perm.IsPaused());
            _ = _apiController.GroupChangeIndividualPermissionState(new GroupPairUserPermissionDto(_groupFullInfoDto.Group, new(_apiController.UID), perm));
        }
        return currentRightSideX;
    }
}
