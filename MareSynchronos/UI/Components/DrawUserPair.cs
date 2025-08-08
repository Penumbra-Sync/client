using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
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
    private readonly UiSharedService _uiSharedService;
    private readonly PlayerPerformanceConfigService _performanceConfigService;
    private readonly CharaDataManager _charaDataManager;
    private float _menuWidth = -1;
    private bool _wasHovered = false;

    public DrawUserPair(string id, Pair entry, List<GroupFullInfoDto> syncedGroups,
        GroupFullInfoDto? currentGroup,
        ApiController apiController, IdDisplayHandler uIDDisplayHandler,
        MareMediator mareMediator, SelectTagForPairUi selectTagForPairUi,
        ServerConfigurationManager serverConfigurationManager,
        UiSharedService uiSharedService, PlayerPerformanceConfigService performanceConfigService,
        CharaDataManager charaDataManager)
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
        _uiSharedService = uiSharedService;
        _performanceConfigService = performanceConfigService;
        _charaDataManager = charaDataManager;
    }

    public Pair Pair => _pair;
    public UserFullPairDto UserPair => _pair.UserPair!;

    public void DrawPairedClient()
    {
        using var id = ImRaii.PushId(GetType() + _id);
        var color = ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), _wasHovered);
        using (ImRaii.Child(GetType() + _id, new System.Numerics.Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight())))
        {
            DrawLeftSide();
            ImGui.SameLine();
            var posX = ImGui.GetCursorPosX();
            var rightSide = DrawRightSide();
            DrawName(posX, rightSide);
        }
        _wasHovered = ImGui.IsItemHovered();
        color.Dispose();
    }

    private void DrawCommonClientMenu()
    {
        if (!_pair.IsPaused)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.User, "Open Profile", _menuWidth, true))
            {
                _displayHandler.OpenProfile(_pair);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("Opens the profile for this user in a new window");
        }
        if (_pair.IsVisible)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Sync, "Reload last data", _menuWidth, true))
            {
                _pair.ApplyLastReceivedData(forced: true);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("This reapplies the last received character data to this character");
        }

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.PlayCircle, "Cycle pause state", _menuWidth, true))
        {
            _ = _apiController.CyclePauseAsync(_pair.UserData);
            ImGui.CloseCurrentPopup();
        }
        ImGui.Separator();

        ImGui.TextUnformatted("Pair Permission Functions");
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.WindowMaximize, "Open Permissions Window", _menuWidth, true))
        {
            _mediator.Publish(new OpenPermissionWindow(_pair));
            ImGui.CloseCurrentPopup();
        }
        UiSharedService.AttachToolTip("Opens the Permissions Window which allows you to manage multiple permissions at once.");

        var isSticky = _pair.UserPair!.OwnPermissions.IsSticky();
        string stickyText = isSticky ? "Disable Preferred Permissions" : "Enable Preferred Permissions";
        var stickyIcon = isSticky ? FontAwesomeIcon.ArrowCircleDown : FontAwesomeIcon.ArrowCircleUp;
        if (_uiSharedService.IconTextButton(stickyIcon, stickyText, _menuWidth, true))
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
        if (_uiSharedService.IconTextButton(disableSoundsIcon, disableSoundsText, _menuWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableSounds(!isDisableSounds);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("Changes sound sync permissions with this user." + (individual ? individualText : string.Empty));

        var isDisableAnims = _pair.UserPair!.OwnPermissions.IsDisableAnimations();
        string disableAnimsText = isDisableAnims ? "Enable animation sync" : "Disable animation sync";
        var disableAnimsIcon = isDisableAnims ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop;
        if (_uiSharedService.IconTextButton(disableAnimsIcon, disableAnimsText, _menuWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableAnimations(!isDisableAnims);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("Changes animation sync permissions with this user." + (individual ? individualText : string.Empty));

        var isDisableVFX = _pair.UserPair!.OwnPermissions.IsDisableVFX();
        string disableVFXText = isDisableVFX ? "Enable VFX sync" : "Disable VFX sync";
        var disableVFXIcon = isDisableVFX ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle;
        if (_uiSharedService.IconTextButton(disableVFXIcon, disableVFXText, _menuWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableVFX(!isDisableVFX);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("Changes VFX sync permissions with this user." + (individual ? individualText : string.Empty));
    }

    private void DrawIndividualMenu()
    {
        ImGui.TextUnformatted("Individual Pair Functions");
        var entryUID = _pair.UserData.AliasOrUID;

        if (_pair.IndividualPairStatus != API.Data.Enum.IndividualPairStatus.None)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Folder, "Pair Groups", _menuWidth, true))
            {
                _selectTagForPairUi.Open(_pair);
            }
            UiSharedService.AttachToolTip("Choose pair groups for " + entryUID);
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Unpair Permanently", _menuWidth, true) && UiSharedService.CtrlPressed())
            {
                _ = _apiController.UserRemovePair(new(_pair.UserData));
            }
            UiSharedService.AttachToolTip("Hold CTRL and click to unpair permanently from " + entryUID);
        }
        else
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Pair individually", _menuWidth, true))
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
            _uiSharedService.IconText(FontAwesomeIcon.PauseCircle);
            userPairText = _pair.UserData.AliasOrUID + " is paused";
        }
        else if (!_pair.IsOnline)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            _uiSharedService.IconText(_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.OneSided
                ? FontAwesomeIcon.ArrowsLeftRight
                : (_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional
                    ? FontAwesomeIcon.User : FontAwesomeIcon.Users));
            userPairText = _pair.UserData.AliasOrUID + " is offline";
        }
        else if (_pair.IsVisible)
        {
            _uiSharedService.IconText(FontAwesomeIcon.Eye, ImGuiColors.ParsedGreen);
            userPairText = _pair.UserData.AliasOrUID + " is visible: " + _pair.PlayerName + Environment.NewLine + "Click to target this player";
            if (ImGui.IsItemClicked())
            {
                _mediator.Publish(new TargetPairMessage(_pair));
            }
        }
        else
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
            _uiSharedService.IconText(_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional
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

        if (_pair.LastAppliedDataBytes >= 0)
        {
            userPairText += UiSharedService.TooltipSeparator;
            userPairText += ((!_pair.IsPaired) ? "(Last) " : string.Empty) + "Mods Info" + Environment.NewLine;
            userPairText += "Files Size: " + UiSharedService.ByteToString(_pair.LastAppliedDataBytes, true);
            if (_pair.LastAppliedApproximateVRAMBytes >= 0)
            {
                userPairText += Environment.NewLine + "Approx. VRAM Usage: " + UiSharedService.ByteToString(_pair.LastAppliedApproximateVRAMBytes, true);
            }
            if (_pair.LastAppliedDataTris >= 0)
            {
                userPairText += Environment.NewLine + "Approx. Triangle Count (excl. Vanilla): "
                    + (_pair.LastAppliedDataTris > 1000 ? (_pair.LastAppliedDataTris / 1000d).ToString("0.0'k'") : _pair.LastAppliedDataTris);
            }
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

        if (_performanceConfigService.Current.ShowPerformanceIndicator
            && !_performanceConfigService.Current.UIDsToIgnore
                .Exists(uid => string.Equals(uid, UserPair.User.Alias, StringComparison.Ordinal) || string.Equals(uid, UserPair.User.UID, StringComparison.Ordinal))
            && ((_performanceConfigService.Current.VRAMSizeWarningThresholdMiB > 0 && _performanceConfigService.Current.VRAMSizeWarningThresholdMiB * 1024 * 1024 < _pair.LastAppliedApproximateVRAMBytes)
                || (_performanceConfigService.Current.TrisWarningThresholdThousands > 0 && _performanceConfigService.Current.TrisWarningThresholdThousands * 1000 < _pair.LastAppliedDataTris))
            && (!_pair.UserPair.OwnPermissions.IsSticky()
                || _performanceConfigService.Current.WarnOnPreferredPermissionsExceedingThresholds))
        {
            ImGui.SameLine();

            _uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudYellow);

            string userWarningText = "WARNING: This user exceeds one or more of your defined thresholds:" + UiSharedService.TooltipSeparator;
            bool shownVram = false;
            if (_performanceConfigService.Current.VRAMSizeWarningThresholdMiB > 0
                && _performanceConfigService.Current.VRAMSizeWarningThresholdMiB * 1024 * 1024 < _pair.LastAppliedApproximateVRAMBytes)
            {
                shownVram = true;
                userWarningText += $"Approx. VRAM Usage: Used: {UiSharedService.ByteToString(_pair.LastAppliedApproximateVRAMBytes)}, Threshold: {_performanceConfigService.Current.VRAMSizeWarningThresholdMiB} MiB";
            }
            if (_performanceConfigService.Current.TrisWarningThresholdThousands > 0
                && _performanceConfigService.Current.TrisWarningThresholdThousands * 1024 < _pair.LastAppliedDataTris)
            {
                if (shownVram) userWarningText += Environment.NewLine;
                userWarningText += $"Approx. Triangle count: Used: {_pair.LastAppliedDataTris}, Threshold: {_performanceConfigService.Current.TrisWarningThresholdThousands * 1000}";
            }

            UiSharedService.AttachToolTip(userWarningText);
        }

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
        var pauseButtonSize = _uiSharedService.GetIconButtonSize(pauseIcon);
        var barButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.EllipsisV);
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
        float currentRightSide = windowEndX - barButtonSize.X;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        if (_uiSharedService.IconButton(FontAwesomeIcon.EllipsisV))
        {
            ImGui.OpenPopup("User Flyout Menu");
        }

        currentRightSide -= (pauseButtonSize.X + spacingX);
        ImGui.SameLine(currentRightSide);
        if (_uiSharedService.IconButton(pauseIcon))
        {
            var perm = _pair.UserPair!.OwnPermissions;

            if (UiSharedService.CtrlPressed() && !perm.IsPaused())
            {
                perm.SetSticky(true);
            }
            perm.SetPaused(!perm.IsPaused());
            _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
        }
        UiSharedService.AttachToolTip(!_pair.UserPair!.OwnPermissions.IsPaused()
            ? ("Pause pairing with " + _pair.UserData.AliasOrUID
                + (_pair.UserPair!.OwnPermissions.IsSticky()
                    ? string.Empty
                    : UiSharedService.TooltipSeparator + "Hold CTRL to enable preferred permissions while pausing." + Environment.NewLine + "This will leave this pair paused even if unpausing syncshells including this pair."))
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
                currentRightSide -= (_uiSharedService.GetIconSize(individualIcon).X + spacingX);

                ImGui.SameLine(currentRightSide);
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow, individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled))
                    _uiSharedService.IconText(individualIcon);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();

                    ImGui.TextUnformatted("Individual User permissions");
                    ImGui.Separator();

                    if (individualIsSticky)
                    {
                        _uiSharedService.IconText(individualIcon);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("Preferred permissions enabled");
                        if (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled)
                            ImGui.Separator();
                    }

                    if (individualSoundsDisabled)
                    {
                        var userSoundsText = "Sound sync";
                        _uiSharedService.IconText(FontAwesomeIcon.VolumeOff);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(userSoundsText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("You");
                        _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OwnPermissions.IsDisableSounds());
                        ImGui.SameLine();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("They");
                        _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OtherPermissions.IsDisableSounds());
                    }

                    if (individualAnimDisabled)
                    {
                        var userAnimText = "Animation sync";
                        _uiSharedService.IconText(FontAwesomeIcon.Stop);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(userAnimText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("You");
                        _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OwnPermissions.IsDisableAnimations());
                        ImGui.SameLine();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("They");
                        _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OtherPermissions.IsDisableAnimations());
                    }

                    if (individualVFXDisabled)
                    {
                        var userVFXText = "VFX sync";
                        _uiSharedService.IconText(FontAwesomeIcon.Circle);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(userVFXText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("You");
                        _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OwnPermissions.IsDisableVFX());
                        ImGui.SameLine();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("They");
                        _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OtherPermissions.IsDisableVFX());
                    }

                    ImGui.EndTooltip();
                }
            }
        }

        if (_charaDataManager.SharedWithYouData.TryGetValue(_pair.UserData, out var sharedData))
        {
            currentRightSide -= (_uiSharedService.GetIconSize(FontAwesomeIcon.Running).X + (spacingX / 2f));
            ImGui.SameLine(currentRightSide);
            _uiSharedService.IconText(FontAwesomeIcon.Running);
            UiSharedService.AttachToolTip($"This user has shared {sharedData.Count} Character Data Sets with you." + UiSharedService.TooltipSeparator
                + "Click to open the Character Data Hub and show the entries.");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                _mediator.Publish(new OpenCharaDataHubWithFilterMessage(_pair.UserData));
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
                currentRightSide -= (_uiSharedService.GetIconSize(icon).X + spacingX);
                ImGui.SameLine(currentRightSide);
                _uiSharedService.IconText(icon);
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
                if (_menuWidth <= 0)
                {
                    _menuWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
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
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Thumbtack, pinText, _menuWidth, true))
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

            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Remove user", _menuWidth, true) && UiSharedService.CtrlPressed())
            {
                ImGui.CloseCurrentPopup();
                _ = _apiController.GroupRemoveUser(new(group.Group, _pair.UserData));
            }
            UiSharedService.AttachToolTip("Hold CTRL and click to remove user " + (_pair.UserData.AliasOrUID) + " from Syncshell");

            if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserSlash, "Ban User", _menuWidth, true))
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
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserShield, modText, _menuWidth, true) && UiSharedService.CtrlPressed())
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

            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Crown, "Transfer Ownership", _menuWidth, true) && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
            {
                ImGui.CloseCurrentPopup();
                _ = _apiController.GroupChangeOwnership(new(group.Group, _pair.UserData));
            }
            UiSharedService.AttachToolTip("Hold CTRL and SHIFT and click to transfer ownership of this Syncshell to "
                + (_pair.UserData.AliasOrUID) + Environment.NewLine + "WARNING: This action is irreversible.");
        }
    }
}