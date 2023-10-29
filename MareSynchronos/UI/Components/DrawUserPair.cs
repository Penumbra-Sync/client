using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components;

public class DrawUserPair
{
    protected readonly ApiController _apiController;
    protected readonly IdDisplayHandler _displayHandler;
    protected readonly MareMediator _mediator;
    protected readonly List<GroupFullInfoDto> _syncedGroups;
    private readonly GroupFullInfoDto? _currentGroup;
    protected Pair _pair;
    private readonly string _id;
    private readonly SelectTagForPairUi _selectTagForPairUi;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private float _menuRenderWidth = -1;

    public DrawUserPair(string id, Pair entry, List<GroupFullInfoDto> syncedGroups,
        GroupFullInfoDto? currentGroup,
        ApiController apiController, IdDisplayHandler uIDDisplayHandler,
        MareMediator mareMediator, SelectTagForPairUi selectTagForPairUi,
        ServerConfigurationManager serverConfigurationManager)
    {
        _id = id;
        _pair = entry;
        _syncedGroups = syncedGroups;
        _currentGroup = currentGroup;
        _apiController = apiController;
        _displayHandler = uIDDisplayHandler;
        _mediator = mareMediator;
        _selectTagForPairUi = selectTagForPairUi;
        _serverConfigurationManager = serverConfigurationManager;
    }

    public Pair Pair => _pair;
    public UserFullPairDto UserPair => _pair.UserPair!;

    public void DrawPairedClient()
    {
        using var id = ImRaii.PushId(GetType() + _id);

        DrawLeftSide();
        ImGui.SameLine();
        var posX = ImGui.GetCursorPosX();
        var rightSide = DrawRightSide();
        DrawName(posX, rightSide);
    }

    private void DrawCommonClientMenu()
    {
        if (!_pair.IsPaused)
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.User, "Open Profile", _menuRenderWidth, true))
            {
                _displayHandler.OpenProfile(_pair);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("Opens the profile for this user in a new window");
        }
        if (_pair.IsVisible)
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Sync, "Reload last data", _menuRenderWidth, true))
            {
                _pair.ApplyLastReceivedData(forced: true);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("This reapplies the last received character data to this character");
        }

        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.PlayCircle, "Cycle pause state", _menuRenderWidth, true))
        {
            _ = _apiController.CyclePause(_pair.UserData);
            ImGui.CloseCurrentPopup();
        }
        ImGui.Separator();

        ImGui.TextUnformatted("Pair Permission Functions");
        var isSticky = _pair.UserPair!.OwnPermissions.IsSticky();
        string stickyText = isSticky ? "Disable Preferred Permissions" : "Enable Preferred Permissions";
        var stickyIcon = isSticky ? FontAwesomeIcon.ArrowCircleDown : FontAwesomeIcon.ArrowCircleUp;
        if (UiSharedService.NormalizedIconTextButton(stickyIcon, stickyText, _menuRenderWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetSticky(!isSticky);
            _ = _apiController.UserSetPairPermissions(new(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("Preferred permissions means that this pair will not" + Environment.NewLine + " be affected by any syncshell permission changes through you.");

        string individualText = Environment.NewLine + Environment.NewLine + "Note: changing this permission will turn the permissions for this"
            + Environment.NewLine + "user to preferred permissions. You can change this behavior"
            + Environment.NewLine + "in the permission settings.";
        bool individual = !_pair.IsDirectlyPaired && _apiController.DefaultPermissions!.IndividualIsSticky;

        var isDisableSounds = _pair.UserPair!.OwnPermissions.IsDisableSounds();
        string disableSoundsText = isDisableSounds ? "Enable sound sync" : "Disable sound sync";
        var disableSoundsIcon = isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute;
        if (UiSharedService.NormalizedIconTextButton(disableSoundsIcon, disableSoundsText, _menuRenderWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableSounds(!isDisableSounds);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("Changes sound sync permissions with this user." + (individual ? individualText : string.Empty));

        var isDisableAnims = _pair.UserPair!.OwnPermissions.IsDisableAnimations();
        string disableAnimsText = isDisableAnims ? "Enable animation sync" : "Disable animation sync";
        var disableAnimsIcon = isDisableAnims ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop;
        if (UiSharedService.NormalizedIconTextButton(disableAnimsIcon, disableAnimsText, _menuRenderWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableAnimations(!isDisableAnims);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("Changes animation sync permissions with this user." + (individual ? individualText : string.Empty));

        var isDisableVFX = _pair.UserPair!.OwnPermissions.IsDisableVFX();
        string disableVFXText = isDisableVFX ? "Enable VFX sync" : "Disable VFX sync";
        var disableVFXIcon = isDisableVFX ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle;
        if (UiSharedService.NormalizedIconTextButton(disableVFXIcon, disableVFXText, _menuRenderWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableVFX(!isDisableVFX);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("Changes VFX sync permissions with this user." + (individual ? individualText : string.Empty));

        if (!_pair.IsPaused)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Pair reporting");
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.ExclamationTriangle, "Report Mare Profile", _menuRenderWidth, true))
            {
                ImGui.CloseCurrentPopup();
                _mediator.Publish(new OpenReportPopupMessage(_pair));
            }
            UiSharedService.AttachToolTip("Report this users Mare Profile to the administrative team.");
        }
    }

    private void DrawIndividualMenu()
    {
        ImGui.TextUnformatted("Individual Pair Functions");
        var entryUID = _pair.UserData.AliasOrUID;

        if (_pair.IndividualPairStatus != API.Data.Enum.IndividualPairStatus.None)
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Folder, "Pair Groups", _menuRenderWidth, true))
            {
                _selectTagForPairUi.Open(_pair);
            }
            UiSharedService.AttachToolTip("Choose pair groups for " + entryUID);
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Trash, "Unpair Permanently", _menuRenderWidth, true) && UiSharedService.CtrlPressed())
            {
                _ = _apiController.UserRemovePair(new(_pair.UserData));
            }
            UiSharedService.AttachToolTip("Hold CTRL and click to unpair permanently from " + entryUID);
        }
        else
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Plus, "Pair individually", _menuRenderWidth, true))
            {
                _ = _apiController.UserAddPair(new(_pair.UserData));
            }
            UiSharedService.AttachToolTip("Pair individually with " + entryUID);
        }
    }

    private void DrawLeftSide()
    {
        string userPairText = string.Empty;

        ImGui.AlignTextToFramePadding();

        if (_pair.IsPaused)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            UiSharedService.NormalizedIcon(FontAwesomeIcon.PauseCircle);
            userPairText = _pair.UserData.AliasOrUID + " is paused";
        }
        else if (!_pair.IsOnline)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            UiSharedService.NormalizedIcon(_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.OneSided
                ? FontAwesomeIcon.ArrowsLeftRight
                : (_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional
                    ? FontAwesomeIcon.User : FontAwesomeIcon.Users));
            userPairText = _pair.UserData.AliasOrUID + " is offline";
        }
        else if (_pair.IsVisible)
        {
            UiSharedService.NormalizedIcon(FontAwesomeIcon.Eye, ImGuiColors.ParsedGreen);
            userPairText = _pair.UserData.AliasOrUID + " is visible";
        }
        else
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
            UiSharedService.NormalizedIcon(_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional
                ? FontAwesomeIcon.User : FontAwesomeIcon.Users);
            userPairText = _pair.UserData.AliasOrUID + " is online";
        }

        if (_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.OneSided)
        {
            userPairText += UiSharedService.TooltipSeparator + "User has not added you back";
        }
        else if (_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional)
        {
            userPairText += UiSharedService.TooltipSeparator + "You are directly Paired";
        }

        if (_syncedGroups.Any())
        {
            userPairText += UiSharedService.TooltipSeparator + string.Join(Environment.NewLine,
                _syncedGroups.Select(g =>
                {
                    var groupNote = _serverConfigurationManager.GetNoteForGid(g.GID);
                    var groupString = string.IsNullOrEmpty(groupNote) ? g.GroupAliasOrGID : $"{groupNote} ({g.GroupAliasOrGID})";
                    return "Paired through " + groupString;
                }));
        }
        UiSharedService.AttachToolTip(userPairText);

        ImGui.SameLine();
    }

    private void DrawName(float leftSide, float rightSide)
    {
        _displayHandler.DrawPairText(_id, _pair, leftSide, () => rightSide - leftSide);
    }

    private void DrawPairedClientMenu()
    {
        DrawIndividualMenu();

        if (_syncedGroups.Any()) ImGui.Separator();
        foreach (var entry in _syncedGroups)
        {
            bool selfIsOwner = string.Equals(_apiController.UID, entry.Owner.UID, StringComparison.Ordinal);
            bool selfIsModerator = entry.GroupUserInfo.IsModerator();
            bool userIsModerator = entry.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var modinfo) && modinfo.IsModerator();
            bool userIsPinned = entry.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var info) && info.IsPinned();
            if (selfIsOwner || selfIsModerator)
            {
                var groupNote = _serverConfigurationManager.GetNoteForGid(entry.GID);
                var groupString = string.IsNullOrEmpty(groupNote) ? entry.GroupAliasOrGID : $"{groupNote} ({entry.GroupAliasOrGID})";

                if (ImGui.BeginMenu(groupString + " Moderation Functions"))
                {
                    DrawSyncshellMenu(entry, selfIsOwner, selfIsModerator, userIsPinned, userIsModerator);
                    ImGui.EndMenu();
                }
            }
        }
    }

    private float DrawRightSide()
    {
        var pauseIcon = _pair.UserPair!.OwnPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseIconSize = UiSharedService.NormalizedIconButtonSize(pauseIcon);
        var barButtonSize = UiSharedService.NormalizedIconButtonSize(FontAwesomeIcon.Bars);
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
        float currentRightSide = windowEndX - barButtonSize.X;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        if (UiSharedService.NormalizedIconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup("User Flyout Menu");
        }

        currentRightSide -= (pauseIconSize.X + spacingX);
        ImGui.SameLine(currentRightSide);
        if (UiSharedService.NormalizedIconButton(pauseIcon))
        {
            var perm = _pair.UserPair!.OwnPermissions;
            perm.SetPaused(!perm.IsPaused());
            _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
        }
        UiSharedService.AttachToolTip(!_pair.UserPair!.OwnPermissions.IsPaused()
            ? "Pause pairing with " + _pair.UserData.AliasOrUID
            : "Resume pairing with " + _pair.UserData.AliasOrUID);

        if (_pair.IsPaired)
        {
            var individualSoundsDisabled = (_pair.UserPair?.OwnPermissions.IsDisableSounds() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableSounds() ?? false);
            var individualAnimDisabled = (_pair.UserPair?.OwnPermissions.IsDisableAnimations() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableAnimations() ?? false);
            var individualVFXDisabled = (_pair.UserPair?.OwnPermissions.IsDisableVFX() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableVFX() ?? false);
            var individualIsSticky = _pair.UserPair!.OwnPermissions.IsSticky();
            var individualIcon = individualIsSticky ? FontAwesomeIcon.ArrowCircleUp : FontAwesomeIcon.InfoCircle;

            if (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled || individualIsSticky)
            {
                currentRightSide -= (UiSharedService.GetIconData(individualIcon).NormalizedIconScale.X + spacingX);

                ImGui.SameLine(currentRightSide);
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, individualIsSticky))
                    UiSharedService.NormalizedIcon(individualIcon);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();

                    ImGui.TextUnformatted("Individual User permissions");
                    ImGui.Separator();

                    if (individualIsSticky)
                    {
                        UiSharedService.NormalizedIcon(individualIcon);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("Preferred permissions enabled");
                        if (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled)
                            ImGui.Separator();
                    }

                    if (individualSoundsDisabled)
                    {
                        var userSoundsText = "Sound sync";
                        UiSharedService.NormalizedIcon(FontAwesomeIcon.VolumeOff);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(userSoundsText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("You");
                        UiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OwnPermissions.IsDisableSounds());
                        ImGui.SameLine();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("They");
                        UiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OtherPermissions.IsDisableSounds());
                    }

                    if (individualAnimDisabled)
                    {
                        var userAnimText = "Animation sync";
                        UiSharedService.NormalizedIcon(FontAwesomeIcon.Stop);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(userAnimText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("You");
                        UiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OwnPermissions.IsDisableAnimations());
                        ImGui.SameLine();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("They");
                        UiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OtherPermissions.IsDisableAnimations());
                    }

                    if (individualVFXDisabled)
                    {
                        var userVFXText = "VFX sync";
                        UiSharedService.NormalizedIcon(FontAwesomeIcon.Circle);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(userVFXText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("You");
                        UiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OwnPermissions.IsDisableVFX());
                        ImGui.SameLine();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("They");
                        UiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OtherPermissions.IsDisableVFX());
                    }

                    ImGui.EndTooltip();
                }
            }
        }

        if (_currentGroup != null)
        {
            var icon = FontAwesomeIcon.None;
            var text = string.Empty;
            if (string.Equals(_currentGroup.OwnerUID, _pair.UserData.UID, StringComparison.Ordinal))
            {
                icon = FontAwesomeIcon.Crown;
                text = "User is owner of this syncshell";
            }
            else if (_currentGroup.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo))
            {
                if (userinfo.IsModerator())
                {
                    icon = FontAwesomeIcon.UserShield;
                    text = "User is moderator in this syncshell";
                }
                else if (userinfo.IsPinned())
                {
                    icon = FontAwesomeIcon.Thumbtack;
                    text = "User is pinned in this syncshell";
                }
            }

            if (!string.IsNullOrEmpty(text))
            {
                currentRightSide -= (UiSharedService.GetIconData(icon).NormalizedIconScale.X + spacingX);
                ImGui.SameLine(currentRightSide);
                UiSharedService.NormalizedIcon(icon);
                UiSharedService.AttachToolTip(text);
            }
        }

        if (ImGui.BeginPopup("User Flyout Menu"))
        {
            using (ImRaii.PushId($"buttons-{_pair.UserData.UID}"))
            {
                ImGui.TextUnformatted("Common Pair Functions");
                DrawCommonClientMenu();
                ImGui.Separator();
                DrawPairedClientMenu();
                if (_menuRenderWidth <= 0)
                {
                    _menuRenderWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                }
            }

            ImGui.EndPopup();
        }

        return currentRightSide - spacingX;
    }

    private void DrawSyncshellMenu(GroupFullInfoDto group, bool selfIsOwner, bool selfIsModerator, bool userIsPinned, bool userIsModerator)
    {
        if (selfIsOwner || ((selfIsModerator) && (!userIsModerator)))
        {
            ImGui.TextUnformatted("Syncshell Moderator Functions");
            var pinText = userIsPinned ? "Unpin user" : "Pin user";
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Thumbtack, pinText, _menuRenderWidth, true))
            {
                ImGui.CloseCurrentPopup();
                if (!group.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo))
                {
                    userinfo = API.Data.Enum.GroupPairUserInfo.IsPinned;
                }
                else
                {
                    userinfo.SetPinned(!userinfo.IsPinned());
                }
                _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(group.Group, _pair.UserData, userinfo));
            }
            UiSharedService.AttachToolTip("Pin this user to the Syncshell. Pinned users will not be deleted in case of a manually initiated Syncshell clean");

            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Trash, "Remove user", _menuRenderWidth, true) && UiSharedService.CtrlPressed())
            {
                ImGui.CloseCurrentPopup();
                _ = _apiController.GroupRemoveUser(new(group.Group, _pair.UserData));
            }
            UiSharedService.AttachToolTip("Hold CTRL and click to remove user " + (_pair.UserData.AliasOrUID) + " from Syncshell");

            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.UserSlash, "Ban User", _menuRenderWidth, true))
            {
                _mediator.Publish(new OpenBanUserPopupMessage(_pair, group));
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("Ban user from this Syncshell");

            ImGui.Separator();
        }

        if (selfIsOwner)
        {
            ImGui.TextUnformatted("Syncshell Owner Functions");
            string modText = userIsModerator ? "Demod user" : "Mod user";
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.UserShield, modText, _menuRenderWidth, true) && UiSharedService.CtrlPressed())
            {
                ImGui.CloseCurrentPopup();
                if (!group.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo))
                {
                    userinfo = API.Data.Enum.GroupPairUserInfo.IsModerator;
                }
                else
                {
                    userinfo.SetModerator(!userinfo.IsModerator());
                }

                _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(group.Group, _pair.UserData, userinfo));
            }
            UiSharedService.AttachToolTip("Hold CTRL to change the moderator status for " + (_pair.UserData.AliasOrUID) + Environment.NewLine +
                "Moderators can kick, ban/unban, pin/unpin users and clear the Syncshell.");

            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Crown, "Transfer Ownership", _menuRenderWidth, true) && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
            {
                ImGui.CloseCurrentPopup();
                _ = _apiController.GroupChangeOwnership(new(group.Group, _pair.UserData));
            }
            UiSharedService.AttachToolTip("Hold CTRL and SHIFT and click to transfer ownership of this Syncshell to "
                + (_pair.UserData.AliasOrUID) + Environment.NewLine + "WARNING: This action is irreversible.");
        }
    }
}