using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using System.Collections.Immutable;

namespace MareSynchronos.UI.Components;

public class DrawFolderGroup : DrawFolderBase
{
    private readonly ApiController _apiController;
    private readonly GroupFullInfoDto _groupFullInfoDto;
    private readonly IdDisplayHandler _idDisplayHandler;
    private readonly MareMediator _mareMediator;

    public DrawFolderGroup(string id, GroupFullInfoDto groupFullInfoDto, ApiController apiController,
        IImmutableList<DrawUserPair> drawPairs, IImmutableList<Pair> allPairs, TagHandler tagHandler, IdDisplayHandler idDisplayHandler,
        MareMediator mareMediator, UiSharedService uiSharedService) :
        base(id, drawPairs, allPairs, tagHandler, uiSharedService)
    {
        _groupFullInfoDto = groupFullInfoDto;
        _apiController = apiController;
        _idDisplayHandler = idDisplayHandler;
        _mareMediator = mareMediator;
    }

    protected override bool RenderIfEmpty => true;
    protected override bool RenderMenu => true;
    private bool IsModerator => IsOwner || _groupFullInfoDto.GroupUserInfo.IsModerator();
    private bool IsOwner => string.Equals(_groupFullInfoDto.OwnerUID, _apiController.UID, StringComparison.Ordinal);
    private bool IsPinned => _groupFullInfoDto.GroupUserInfo.IsPinned();

    protected override float DrawIcon()
    {
        ImGui.AlignTextToFramePadding();

        _uiSharedService.IconText(_groupFullInfoDto.GroupPermissions.IsDisableInvites() ? FontAwesomeIcon.Lock : FontAwesomeIcon.Users);
        if (_groupFullInfoDto.GroupPermissions.IsDisableInvites())
        {
            UiSharedService.AttachToolTip("Syncshell " + _groupFullInfoDto.GroupAliasOrGID + " is closed for invites");
        }

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing with { X = ImGui.GetStyle().ItemSpacing.X / 2f }))
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();

            ImGui.TextUnformatted("[" + OnlinePairs.ToString() + "]");
        }
        UiSharedService.AttachToolTip(OnlinePairs + " online" + Environment.NewLine + TotalPairs + " total");

        ImGui.SameLine();
        if (IsOwner)
        {
            ImGui.AlignTextToFramePadding();
            _uiSharedService.IconText(FontAwesomeIcon.Crown);
            UiSharedService.AttachToolTip("You are the owner of " + _groupFullInfoDto.GroupAliasOrGID);
        }
        else if (IsModerator)
        {
            ImGui.AlignTextToFramePadding();
            _uiSharedService.IconText(FontAwesomeIcon.UserShield);
            UiSharedService.AttachToolTip("You are a moderator in " + _groupFullInfoDto.GroupAliasOrGID);
        }
        else if (IsPinned)
        {
            ImGui.AlignTextToFramePadding();
            _uiSharedService.IconText(FontAwesomeIcon.Thumbtack);
            UiSharedService.AttachToolTip("You are pinned in " + _groupFullInfoDto.GroupAliasOrGID);
        }
        ImGui.SameLine();
        return ImGui.GetCursorPosX();
    }

    protected override void DrawMenu(float menuWidth)
    {
        ImGui.TextUnformatted("Syncshell Menu (" + _groupFullInfoDto.GroupAliasOrGID + ")");
        ImGui.Separator();

        ImGui.TextUnformatted("General Syncshell Actions");
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Copy, "Copy ID", menuWidth, true))
        {
            ImGui.CloseCurrentPopup();
            ImGui.SetClipboardText(_groupFullInfoDto.GroupAliasOrGID);
        }
        UiSharedService.AttachToolTip("Copy Syncshell ID to Clipboard");

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.StickyNote, "Copy Notes", menuWidth, true))
        {
            ImGui.CloseCurrentPopup();
            ImGui.SetClipboardText(UiSharedService.GetNotes(DrawPairs.Select(k => k.Pair).ToList()));
        }
        UiSharedService.AttachToolTip("Copies all your notes for all users in this Syncshell to the clipboard." + Environment.NewLine + "They can be imported via Settings -> General -> Notes -> Import notes from clipboard");

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleLeft, "Leave Syncshell", menuWidth, true) && UiSharedService.CtrlPressed())
        {
            _ = _apiController.GroupLeave(_groupFullInfoDto);
            ImGui.CloseCurrentPopup();
        }
        UiSharedService.AttachToolTip("Hold CTRL and click to leave this Syncshell" + (!string.Equals(_groupFullInfoDto.OwnerUID, _apiController.UID, StringComparison.Ordinal)
            ? string.Empty : Environment.NewLine + "WARNING: This action is irreversible" + Environment.NewLine + "Leaving an owned Syncshell will transfer the ownership to a random person in the Syncshell."));

        ImGui.Separator();
        ImGui.TextUnformatted("Permission Settings");
        var perm = _groupFullInfoDto.GroupUserPermissions;
        bool disableSounds = perm.IsDisableSounds();
        bool disableAnims = perm.IsDisableAnimations();
        bool disableVfx = perm.IsDisableVFX();

        if ((_groupFullInfoDto.GroupPermissions.IsPreferDisableAnimations() != disableAnims
            || _groupFullInfoDto.GroupPermissions.IsPreferDisableSounds() != disableSounds
            || _groupFullInfoDto.GroupPermissions.IsPreferDisableVFX() != disableVfx)
            && _uiSharedService.IconTextButton(FontAwesomeIcon.Check, "Align with suggested permissions", menuWidth, true))
        {
            perm.SetDisableVFX(_groupFullInfoDto.GroupPermissions.IsPreferDisableVFX());
            perm.SetDisableSounds(_groupFullInfoDto.GroupPermissions.IsPreferDisableSounds());
            perm.SetDisableAnimations(_groupFullInfoDto.GroupPermissions.IsPreferDisableAnimations());
            _ = _apiController.GroupChangeIndividualPermissionState(new(_groupFullInfoDto.Group, new(_apiController.UID), perm));
            ImGui.CloseCurrentPopup();
        }

        if (_uiSharedService.IconTextButton(disableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeOff, disableSounds ? "Enable Sound Sync" : "Disable Sound Sync", menuWidth, true))
        {
            perm.SetDisableSounds(!disableSounds);
            _ = _apiController.GroupChangeIndividualPermissionState(new(_groupFullInfoDto.Group, new(_apiController.UID), perm));
            ImGui.CloseCurrentPopup();
        }

        if (_uiSharedService.IconTextButton(disableAnims ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop, disableAnims ? "Enable Animation Sync" : "Disable Animation Sync", menuWidth, true))
        {
            perm.SetDisableAnimations(!disableAnims);
            _ = _apiController.GroupChangeIndividualPermissionState(new(_groupFullInfoDto.Group, new(_apiController.UID), perm));
            ImGui.CloseCurrentPopup();
        }

        if (_uiSharedService.IconTextButton(disableVfx ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle, disableVfx ? "Enable VFX Sync" : "Disable VFX Sync", menuWidth, true))
        {
            perm.SetDisableVFX(!disableVfx);
            _ = _apiController.GroupChangeIndividualPermissionState(new(_groupFullInfoDto.Group, new(_apiController.UID), perm));
            ImGui.CloseCurrentPopup();
        }

        if (IsModerator || IsOwner)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Syncshell Admin Functions");
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Cog, "Open Admin Panel", menuWidth, true))
            {
                ImGui.CloseCurrentPopup();
                _mareMediator.Publish(new OpenSyncshellAdminPanel(_groupFullInfoDto));
            }
        }
    }

    protected override void DrawName(float width)
    {
        _idDisplayHandler.DrawGroupText(_id, _groupFullInfoDto, ImGui.GetCursorPosX(), () => width);
    }

    protected override float DrawRightSide(float currentRightSideX)
    {
        var spacingX = ImGui.GetStyle().ItemSpacing.X;

        FontAwesomeIcon pauseIcon = _groupFullInfoDto.GroupUserPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseButtonSize = _uiSharedService.GetIconButtonSize(pauseIcon);

        var userCogButtonSize = _uiSharedService.GetIconSize(FontAwesomeIcon.UsersCog);

        var individualSoundsDisabled = _groupFullInfoDto.GroupUserPermissions.IsDisableSounds();
        var individualAnimDisabled = _groupFullInfoDto.GroupUserPermissions.IsDisableAnimations();
        var individualVFXDisabled = _groupFullInfoDto.GroupUserPermissions.IsDisableVFX();

        var infoIconPosDist = currentRightSideX - pauseButtonSize.X - spacingX;

        ImGui.SameLine(infoIconPosDist - userCogButtonSize.X);

        ImGui.AlignTextToFramePadding();

        _uiSharedService.IconText(FontAwesomeIcon.UsersCog, (_groupFullInfoDto.GroupPermissions.IsPreferDisableAnimations() != individualAnimDisabled
            || _groupFullInfoDto.GroupPermissions.IsPreferDisableSounds() != individualSoundsDisabled
            || _groupFullInfoDto.GroupPermissions.IsPreferDisableVFX() != individualVFXDisabled) ? ImGuiColors.DalamudYellow : null);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();

            ImGui.TextUnformatted("Syncshell Permissions");
            ImGuiHelpers.ScaledDummy(2f);

            _uiSharedService.BooleanToColoredIcon(!individualSoundsDisabled, inline: false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Sound Sync");

            _uiSharedService.BooleanToColoredIcon(!individualAnimDisabled, inline: false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Animation Sync");

            _uiSharedService.BooleanToColoredIcon(!individualVFXDisabled, inline: false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("VFX Sync");

            ImGui.Separator();

            ImGuiHelpers.ScaledDummy(2f);
            ImGui.TextUnformatted("Suggested Permissions");
            ImGuiHelpers.ScaledDummy(2f);

            _uiSharedService.BooleanToColoredIcon(!_groupFullInfoDto.GroupPermissions.IsPreferDisableSounds(), inline: false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Sound Sync");

            _uiSharedService.BooleanToColoredIcon(!_groupFullInfoDto.GroupPermissions.IsPreferDisableAnimations(), inline: false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Animation Sync");

            _uiSharedService.BooleanToColoredIcon(!_groupFullInfoDto.GroupPermissions.IsPreferDisableVFX(), inline: false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("VFX Sync");

            ImGui.EndTooltip();
        }

        ImGui.SameLine();
        if (_uiSharedService.IconButton(pauseIcon))
        {
            var perm = _groupFullInfoDto.GroupUserPermissions;
            perm.SetPaused(!perm.IsPaused());
            _ = _apiController.GroupChangeIndividualPermissionState(new GroupPairUserPermissionDto(_groupFullInfoDto.Group, new(_apiController.UID), perm));
        }
        return currentRightSideX;
    }
}
