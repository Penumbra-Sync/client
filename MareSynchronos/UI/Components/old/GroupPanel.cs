using Dalamud.Interface.Components;
using Dalamud.Interface;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.WebAPI;
using System.Numerics;
using System.Globalization;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Handlers;
using Dalamud.Interface.Utility;

namespace MareSynchronos.UI.Components.old;
/*
internal sealed class GroupPanel
{
    private readonly Dictionary<string, bool> _expandedGroupState = new(StringComparer.Ordinal);
    private readonly CompactUi _mainUi;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly Dictionary<string, bool> _showGidForEntry = new(StringComparer.Ordinal);
    private readonly UidDisplayHandler _uidDisplayHandler;
    private readonly UiSharedService _uiShared;
    private List<BannedGroupUserDto> _bannedUsers = new();
    private int _bulkInviteCount = 10;
    private List<string> _bulkOneTimeInvites = new();
    private string _editGroupComment = string.Empty;
    private string _editGroupEntry = string.Empty;
    private bool _errorGroupCreate = false;
    private bool _errorGroupJoin;
    private bool _isPasswordValid;
    private GroupPasswordDto? _lastCreatedGroup = null;
    private bool _modalBanListOpened;
    private bool _modalBulkOneTimeInvitesOpened;
    private bool _modalChangePwOpened;
    private string _newSyncShellPassword = string.Empty;
    private bool _showModalBanList = false;
    private bool _showModalBulkOneTimeInvites = false;
    private bool _showModalChangePassword;
    private bool _showModalCreateGroup;
    private bool _showModalEnterPassword;
    private string _syncShellPassword = string.Empty;
    private string _syncShellToJoin = string.Empty;

    public GroupPanel(CompactUi mainUi, UiSharedService uiShared, PairManager pairManager, UidDisplayHandler uidDisplayHandler, ServerConfigurationManager serverConfigurationManager)
    {
        _mainUi = mainUi;
        _uiShared = uiShared;
        _pairManager = pairManager;
        _uidDisplayHandler = uidDisplayHandler;
        _serverConfigurationManager = serverConfigurationManager;
    }

    private ApiController ApiController => _uiShared.ApiController;

    public void DrawSyncshells()
    {
        UiSharedService.DrawWithID("addsyncshell", DrawAddSyncshell);
        UiSharedService.DrawWithID("syncshelllist", DrawSyncshellList);
        _mainUi.TransferPartHeight = ImGui.GetCursorPosY();
    }

    private void DrawAddSyncshell()
    {
        var buttonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus);
        ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonSize.X);
        ImGui.InputTextWithHint("##syncshellid", "Syncshell GID/Alias (leave empty to create)", ref _syncShellToJoin, 20);
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);

        bool userCanJoinMoreGroups = _pairManager.GroupPairs.Count < ApiController.ServerInfo.MaxGroupsJoinedByUser;
        bool userCanCreateMoreGroups = _pairManager.GroupPairs.Count(u => string.Equals(u.Key.Owner.UID, ApiController.UID, StringComparison.Ordinal)) < ApiController.ServerInfo.MaxGroupsCreatedByUser;
        bool alreadyInGroup = _pairManager.GroupPairs.Select(p => p.Key).Any(p => string.Equals(p.Group.Alias, _syncShellToJoin, StringComparison.Ordinal)
            || string.Equals(p.Group.GID, _syncShellToJoin, StringComparison.Ordinal));

        if (alreadyInGroup) ImGui.BeginDisabled();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
        {
            if (!string.IsNullOrEmpty(_syncShellToJoin))
            {
                if (userCanJoinMoreGroups)
                {
                    _errorGroupJoin = false;
                    _showModalEnterPassword = true;
                    ImGui.OpenPopup("Enter Syncshell Password");
                }
            }
            else
            {
                if (userCanCreateMoreGroups)
                {
                    _lastCreatedGroup = null;
                    _errorGroupCreate = false;
                    _showModalCreateGroup = true;
                    ImGui.OpenPopup("Create Syncshell");
                }
            }
        }
        UiSharedService.AttachToolTip(_syncShellToJoin.IsNullOrEmpty()
            ? (userCanCreateMoreGroups ? "Create Syncshell" : $"You cannot create more than {ApiController.ServerInfo.MaxGroupsCreatedByUser} Syncshells")
            : (userCanJoinMoreGroups ? "Join Syncshell" + _syncShellToJoin : $"You cannot join more than {ApiController.ServerInfo.MaxGroupsJoinedByUser} Syncshells"));

        if (alreadyInGroup) ImGui.EndDisabled();

        if (ImGui.BeginPopupModal("Enter Syncshell Password", ref _showModalEnterPassword, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped("Before joining any Syncshells please be aware that you will be automatically paired with everyone in the Syncshell.");
            ImGui.Separator();
            UiSharedService.TextWrapped("Enter the password for Syncshell " + _syncShellToJoin + ":");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##password", _syncShellToJoin + " Password", ref _syncShellPassword, 255, ImGuiInputTextFlags.Password);
            if (_errorGroupJoin)
            {
                UiSharedService.ColorTextWrapped($"An error occured during joining of this Syncshell: you either have joined the maximum amount of Syncshells ({ApiController.ServerInfo.MaxGroupsJoinedByUser}), " +
                    $"it does not exist, the password you entered is wrong, you already joined the Syncshell, the Syncshell is full ({ApiController.ServerInfo.MaxGroupUserCount} users) or the Syncshell has closed invites.",
                    new Vector4(1, 0, 0, 1));
            }
            if (ImGui.Button("Join " + _syncShellToJoin))
            {
                var shell = _syncShellToJoin;
                var pw = _syncShellPassword;
                // todo: handle group join in two stages
                //_errorGroupJoin = !ApiController.GroupJoin(new(new GroupData(shell), pw)).Result;
                if (!_errorGroupJoin)
                {
                    _syncShellToJoin = string.Empty;
                    _showModalEnterPassword = false;
                }
                _syncShellPassword = string.Empty;
            }
            UiSharedService.SetScaledWindowSize(290);
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopupModal("Create Syncshell", ref _showModalCreateGroup, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped("Press the button below to create a new Syncshell.");
            ImGui.SetNextItemWidth(200);
            if (ImGui.Button("Create Syncshell"))
            {
                try
                {
                    _lastCreatedGroup = ApiController.GroupCreate().Result;
                }
                catch
                {
                    _lastCreatedGroup = null;
                    _errorGroupCreate = true;
                }
            }

            if (_lastCreatedGroup != null)
            {
                ImGui.Separator();
                _errorGroupCreate = false;
                ImGui.TextUnformatted("Syncshell ID: " + _lastCreatedGroup.Group.GID);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Syncshell Password: " + _lastCreatedGroup.Password);
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
                {
                    ImGui.SetClipboardText(_lastCreatedGroup.Password);
                }
                UiSharedService.TextWrapped("You can change the Syncshell password later at any time.");
            }

            if (_errorGroupCreate)
            {
                UiSharedService.ColorTextWrapped("You are already owner of the maximum amount of Syncshells (3) or joined the maximum amount of Syncshells (6). Relinquish ownership of your own Syncshells to someone else or leave existing Syncshells.",
                    new Vector4(1, 0, 0, 1));
            }

            UiSharedService.SetScaledWindowSize(350);
            ImGui.EndPopup();
        }

        ImGuiHelpers.ScaledDummy(2);
    }

    private void DrawSyncshell(GroupFullInfoDto groupDto, List<Pair> pairsInGroup)
    {
        var name = groupDto.Group.Alias ?? groupDto.GID;
        if (!_expandedGroupState.TryGetValue(groupDto.GID, out bool isExpanded))
        {
            isExpanded = false;
            _expandedGroupState.Add(groupDto.GID, isExpanded);
        }
        var icon = isExpanded ? FontAwesomeIcon.CaretSquareDown : FontAwesomeIcon.CaretSquareRight;
        var collapseButton = UiSharedService.GetIconButtonSize(icon);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0, 0, 0));
        if (ImGuiComponents.IconButton(icon))
        {
            _expandedGroupState[groupDto.GID] = !_expandedGroupState[groupDto.GID];
        }
        ImGui.PopStyleColor(2);
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + collapseButton.X);
        var pauseIcon = groupDto.GroupUserPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        if (ImGuiComponents.IconButton(pauseIcon))
        {
            // todo: pause/unpause
            //var userPerm = groupDto.GroupUserPermissions ^ GroupUserPermissions.Paused;
            //_ = ApiController.GroupChangeIndividualPermissionState(new GroupPairUserPermissionDto(groupDto.Group, new UserData(ApiController.UID), userPerm));
        }
        UiSharedService.AttachToolTip((groupDto.GroupUserPermissions.IsPaused() ? "Resume" : "Pause") + " pairing with all users in this Syncshell");
        ImGui.SameLine();

        var textIsGid = true;
        string groupName = groupDto.GroupAliasOrGID;

        if (string.Equals(groupDto.OwnerUID, ApiController.UID, StringComparison.Ordinal))
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Crown.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("You are the owner of Syncshell " + groupName);
            ImGui.SameLine();
        }
        else if (groupDto.GroupUserInfo.IsModerator())
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.UserShield.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("You are a moderator of Syncshell " + groupName);
            ImGui.SameLine();
        }

        _showGidForEntry.TryGetValue(groupDto.GID, out var showGidInsteadOfName);
        var groupComment = _serverConfigurationManager.GetNoteForGid(groupDto.GID);
        if (!showGidInsteadOfName && !string.IsNullOrEmpty(groupComment))
        {
            groupName = groupComment;
            textIsGid = false;
        }

        if (!string.Equals(_editGroupEntry, groupDto.GID, StringComparison.Ordinal))
        {
            if (textIsGid) ImGui.PushFont(UiBuilder.MonoFont);
            ImGui.TextUnformatted(groupName);
            if (textIsGid) ImGui.PopFont();
            UiSharedService.AttachToolTip("Left click to switch between GID display and comment" + Environment.NewLine +
                          "Right click to change comment for " + groupName + Environment.NewLine + Environment.NewLine
                          + "Users: " + (pairsInGroup.Count + 1) + ", Owner: " + groupDto.OwnerAliasOrUID);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                var prevState = textIsGid;
                if (_showGidForEntry.ContainsKey(groupDto.GID))
                {
                    prevState = _showGidForEntry[groupDto.GID];
                }

                _showGidForEntry[groupDto.GID] = !prevState;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _serverConfigurationManager.SetNoteForGid(_editGroupEntry, _editGroupComment);
                _editGroupComment = _serverConfigurationManager.GetNoteForGid(groupDto.GID) ?? string.Empty;
                _editGroupEntry = groupDto.GID;
            }
        }
        else
        {
            var buttonSizes = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars).X + UiSharedService.GetIconSize(FontAwesomeIcon.LockOpen).X;
            ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX() - buttonSizes - ImGui.GetStyle().ItemSpacing.X * 2);
            if (ImGui.InputTextWithHint("", "Comment/Notes", ref _editGroupComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _serverConfigurationManager.SetNoteForGid(groupDto.GID, _editGroupComment);
                _editGroupEntry = string.Empty;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _editGroupEntry = string.Empty;
            }
            UiSharedService.AttachToolTip("Hit ENTER to save\nRight click to cancel");
        }

        UiSharedService.DrawWithID(groupDto.GID + "settings", () => DrawSyncShellButtons(groupDto, pairsInGroup));

        if (_showModalBanList && !_modalBanListOpened)
        {
            _modalBanListOpened = true;
            ImGui.OpenPopup("Manage Banlist for " + groupDto.GID);
        }

        if (!_showModalBanList) _modalBanListOpened = false;

        if (ImGui.BeginPopupModal("Manage Banlist for " + groupDto.GID, ref _showModalBanList, UiSharedService.PopupWindowFlags))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Retweet, "Refresh Banlist from Server"))
            {
                _bannedUsers = ApiController.GroupGetBannedUsers(groupDto).Result;
            }

            if (ImGui.BeginTable("bannedusertable" + groupDto.GID, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.None, 1);
                ImGui.TableSetupColumn("Alias", ImGuiTableColumnFlags.None, 1);
                ImGui.TableSetupColumn("By", ImGuiTableColumnFlags.None, 1);
                ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.None, 2);
                ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.None, 3);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 1);

                ImGui.TableHeadersRow();

                foreach (var bannedUser in _bannedUsers.ToList())
                {
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(bannedUser.UID);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(bannedUser.UserAlias ?? string.Empty);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(bannedUser.BannedBy);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(bannedUser.BannedOn.ToLocalTime().ToString(CultureInfo.CurrentCulture));
                    ImGui.TableNextColumn();
                    UiSharedService.TextWrapped(bannedUser.Reason);
                    ImGui.TableNextColumn();
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Check, "Unban#" + bannedUser.UID))
                    {
                        _ = ApiController.GroupUnbanUser(bannedUser);
                        _bannedUsers.RemoveAll(b => string.Equals(b.UID, bannedUser.UID, StringComparison.Ordinal));
                    }
                }

                ImGui.EndTable();
            }
            UiSharedService.SetScaledWindowSize(700, 300);
            ImGui.EndPopup();
        }

        if (_showModalChangePassword && !_modalChangePwOpened)
        {
            _modalChangePwOpened = true;
            ImGui.OpenPopup("Change Syncshell Password");
        }

        if (!_showModalChangePassword) _modalChangePwOpened = false;

        if (ImGui.BeginPopupModal("Change Syncshell Password", ref _showModalChangePassword, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped("Enter the new Syncshell password for Syncshell " + name + " here.");
            UiSharedService.TextWrapped("This action is irreversible");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##changepw", "New password for " + name, ref _newSyncShellPassword, 255);
            if (ImGui.Button("Change password"))
            {
                var pw = _newSyncShellPassword;
                _isPasswordValid = ApiController.GroupChangePassword(new(groupDto.Group, pw)).Result;
                _newSyncShellPassword = string.Empty;
                if (_isPasswordValid) _showModalChangePassword = false;
            }

            if (!_isPasswordValid)
            {
                UiSharedService.ColorTextWrapped("The selected password is too short. It must be at least 10 characters.", new Vector4(1, 0, 0, 1));
            }

            UiSharedService.SetScaledWindowSize(290);
            ImGui.EndPopup();
        }

        if (_showModalBulkOneTimeInvites && !_modalBulkOneTimeInvitesOpened)
        {
            _modalBulkOneTimeInvitesOpened = true;
            ImGui.OpenPopup("Create Bulk One-Time Invites");
        }

        if (!_showModalBulkOneTimeInvites) _modalBulkOneTimeInvitesOpened = false;

        if (ImGui.BeginPopupModal("Create Bulk One-Time Invites", ref _showModalBulkOneTimeInvites, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped("This allows you to create up to 100 one-time invites at once for the Syncshell " + name + "." + Environment.NewLine
                + "The invites are valid for 24h after creation and will automatically expire.");
            ImGui.Separator();
            if (_bulkOneTimeInvites.Count == 0)
            {
                ImGui.SetNextItemWidth(-1);
                ImGui.SliderInt("Amount##bulkinvites", ref _bulkInviteCount, 1, 100);
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.MailBulk, "Create invites"))
                {
                    _bulkOneTimeInvites = ApiController.GroupCreateTempInvite(groupDto, _bulkInviteCount).Result;
                }
            }
            else
            {
                UiSharedService.TextWrapped("A total of " + _bulkOneTimeInvites.Count + " invites have been created.");
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, "Copy invites to clipboard"))
                {
                    ImGui.SetClipboardText(string.Join(Environment.NewLine, _bulkOneTimeInvites));
                }
            }

            UiSharedService.SetScaledWindowSize(290);
            ImGui.EndPopup();
        }

        ImGui.Indent(collapseButton.X);
        if (_expandedGroupState[groupDto.GID])
        {
            var visibleUsers = pairsInGroup.Where(u => u.IsVisible)
                .OrderByDescending(u => string.Equals(u.UserData.UID, groupDto.OwnerUID, StringComparison.Ordinal))
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsModerator())
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsPinned())
                .ThenBy(u => u.GetNote() ?? u.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase)
                .Select(c => new DrawGroupPair(groupDto.GID + c.UserData.UID, c, ApiController, groupDto, c.GroupPair.Single(g => GroupDataComparer.Instance.Equals(g.Key.Group, groupDto.Group)).Value,
                    _uidDisplayHandler))
                .ToList();
            var onlineUsers = pairsInGroup.Where(u => u.IsOnline && !u.IsVisible)
                .OrderByDescending(u => string.Equals(u.UserData.UID, groupDto.OwnerUID, StringComparison.Ordinal))
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsModerator())
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsPinned())
                .ThenBy(u => u.GetNote() ?? u.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase)
                .Select(c => new DrawGroupPair(groupDto.GID + c.UserData.UID, c, ApiController, groupDto, c.GroupPair.Single(g => GroupDataComparer.Instance.Equals(g.Key.Group, groupDto.Group)).Value,
                    _uidDisplayHandler))
                .ToList();
            var offlineUsers = pairsInGroup.Where(u => !u.IsOnline && !u.IsVisible)
                .OrderByDescending(u => string.Equals(u.UserData.UID, groupDto.OwnerUID, StringComparison.Ordinal))
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsModerator())
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsPinned())
                .ThenBy(u => u.GetNote() ?? u.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase)
                .Select(c => new DrawGroupPair(groupDto.GID + c.UserData.UID, c, ApiController, groupDto, c.GroupPair.Single(g => GroupDataComparer.Instance.Equals(g.Key.Group, groupDto.Group)).Value,
                    _uidDisplayHandler))
                .ToList();

            if (visibleUsers.Any())
            {
                ImGui.TextUnformatted("Visible");
                ImGui.Separator();
                foreach (var entry in visibleUsers)
                {
                    UiSharedService.DrawWithID(groupDto.GID + entry.UID, () => entry.DrawPairedClient());
                }
            }

            if (onlineUsers.Any())
            {
                ImGui.TextUnformatted("Online");
                ImGui.Separator();
                foreach (var entry in onlineUsers)
                {
                    UiSharedService.DrawWithID(groupDto.GID + entry.UID, () => entry.DrawPairedClient());
                }
            }

            if (offlineUsers.Any())
            {
                ImGui.TextUnformatted("Offline/Unknown");
                ImGui.Separator();
                foreach (var entry in offlineUsers)
                {
                    UiSharedService.DrawWithID(groupDto.GID + entry.UID, () => entry.DrawPairedClient());
                }
            }

            ImGui.Separator();
            ImGui.Unindent(ImGui.GetStyle().ItemSpacing.X / 2);
        }
        ImGui.Unindent(collapseButton.X);
    }

    private void DrawSyncShellButtons(GroupFullInfoDto groupDto, List<Pair> groupPairs)
    {
        var infoIcon = FontAwesomeIcon.InfoCircle;

        bool invitesEnabled = !groupDto.GroupPermissions.IsDisableInvites();
        var soundsDisabled = groupDto.GroupPermissions.IsPreferDisableSounds();
        var animDisabled = groupDto.GroupPermissions.IsPreferDisableAnimations();
        var vfxDisabled = groupDto.GroupPermissions.IsPreferDisableVFX();

        var userSoundsDisabled = groupDto.GroupUserPermissions.IsDisableSounds();
        var userAnimDisabled = groupDto.GroupUserPermissions.IsDisableAnimations();
        var userVFXDisabled = groupDto.GroupUserPermissions.IsDisableVFX();

        bool showInfoIcon = !invitesEnabled || soundsDisabled || animDisabled || vfxDisabled || userSoundsDisabled || userAnimDisabled || userVFXDisabled;

        var lockedIcon = invitesEnabled ? FontAwesomeIcon.LockOpen : FontAwesomeIcon.Lock;
        var animIcon = animDisabled ? FontAwesomeIcon.Stop : FontAwesomeIcon.Running;
        var soundsIcon = soundsDisabled ? FontAwesomeIcon.VolumeOff : FontAwesomeIcon.VolumeUp;
        var vfxIcon = vfxDisabled ? FontAwesomeIcon.Circle : FontAwesomeIcon.Sun;
        var userAnimIcon = userAnimDisabled ? FontAwesomeIcon.Stop : FontAwesomeIcon.Running;
        var userSoundsIcon = userSoundsDisabled ? FontAwesomeIcon.VolumeOff : FontAwesomeIcon.VolumeUp;
        var userVFXIcon = userVFXDisabled ? FontAwesomeIcon.Circle : FontAwesomeIcon.Sun;

        var iconSize = UiSharedService.GetIconSize(infoIcon);
        var diffLockUnlockIcons = showInfoIcon ? (UiSharedService.GetIconSize(infoIcon).X - iconSize.X) / 2 : 0;
        var barbuttonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars);
        var isOwner = string.Equals(groupDto.OwnerUID, ApiController.UID, StringComparison.Ordinal);

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - barbuttonSize.X - (showInfoIcon ? iconSize.X : 0) - diffLockUnlockIcons - (showInfoIcon ? ImGui.GetStyle().ItemSpacing.X : 0));
        if (showInfoIcon)
        {
            UiSharedService.FontText(infoIcon.ToIconString(), UiBuilder.IconFont);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                if (!invitesEnabled || soundsDisabled || animDisabled || vfxDisabled)
                {
                    ImGui.TextUnformatted("Syncshell permissions");

                    if (!invitesEnabled)
                    {
                        var lockedText = "Syncshell is closed for joining";
                        UiSharedService.FontText(lockedIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(lockedText);
                    }

                    if (soundsDisabled)
                    {
                        var soundsText = "Sound sync disabled through owner";
                        UiSharedService.FontText(soundsIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(soundsText);
                    }

                    if (animDisabled)
                    {
                        var animText = "Animation sync disabled through owner";
                        UiSharedService.FontText(animIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(animText);
                    }

                    if (vfxDisabled)
                    {
                        var vfxText = "VFX sync disabled through owner";
                        UiSharedService.FontText(vfxIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(vfxText);
                    }
                }

                if (userSoundsDisabled || userAnimDisabled || userVFXDisabled)
                {
                    if (!invitesEnabled || soundsDisabled || animDisabled || vfxDisabled)
                        ImGui.Separator();

                    ImGui.TextUnformatted("Your permissions");

                    if (userSoundsDisabled)
                    {
                        var userSoundsText = "Sound sync disabled through you";
                        UiSharedService.FontText(userSoundsIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userSoundsText);
                    }

                    if (userAnimDisabled)
                    {
                        var userAnimText = "Animation sync disabled through you";
                        UiSharedService.FontText(userAnimIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userAnimText);
                    }

                    if (userVFXDisabled)
                    {
                        var userVFXText = "VFX sync disabled through you";
                        UiSharedService.FontText(userVFXIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userVFXText);
                    }

                    if (!invitesEnabled || soundsDisabled || animDisabled || vfxDisabled)
                        UiSharedService.TextWrapped("Note that syncshell permissions for disabling take precedence over your own set permissions");
                }
                ImGui.EndTooltip();
            }
            ImGui.SameLine();
        }

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + diffLockUnlockIcons);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup("ShellPopup");
        }

        if (ImGui.BeginPopup("ShellPopup"))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ArrowCircleLeft, "Leave Syncshell") && UiSharedService.CtrlPressed())
            {
                _ = ApiController.GroupLeave(groupDto);
            }
            UiSharedService.AttachToolTip("Hold CTRL and click to leave this Syncshell" + (!string.Equals(groupDto.OwnerUID, ApiController.UID, StringComparison.Ordinal) ? string.Empty : Environment.NewLine
                + "WARNING: This action is irreversible" + Environment.NewLine + "Leaving an owned Syncshell will transfer the ownership to a random person in the Syncshell."));

            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, "Copy ID"))
            {
                ImGui.CloseCurrentPopup();
                ImGui.SetClipboardText(groupDto.GroupAliasOrGID);
            }
            UiSharedService.AttachToolTip("Copy Syncshell ID to Clipboard");

            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.StickyNote, "Copy Notes"))
            {
                ImGui.CloseCurrentPopup();
                ImGui.SetClipboardText(UiSharedService.GetNotes(groupPairs));
            }
            UiSharedService.AttachToolTip("Copies all your notes for all users in this Syncshell to the clipboard." + Environment.NewLine + "They can be imported via Settings -> Privacy -> Import Notes from Clipboard");

            var soundsText = userSoundsDisabled ? "Enable sound sync" : "Disable sound sync";
            if (ImGuiComponents.IconButtonWithText(userSoundsIcon, soundsText))
            {
                ImGui.CloseCurrentPopup();
                var perm = groupDto.GroupUserPermissions;
                perm.SetDisableSounds(!perm.IsDisableSounds());
                _ = ApiController.GroupChangeIndividualPermissionState(new(groupDto.Group, new UserData(ApiController.UID), perm));
            }
            UiSharedService.AttachToolTip("Sets your allowance for sound synchronization for users of this syncshell."
                + Environment.NewLine + "Disabling the synchronization will stop applying sound modifications for users of this syncshell."
                + Environment.NewLine + "Note: this setting can be forcefully overridden to 'disabled' through the syncshell owner."
                + Environment.NewLine + "Note: this setting does not apply to individual pairs that are also in the syncshell.");

            var animText = userAnimDisabled ? "Enable animations sync" : "Disable animations sync";
            if (ImGuiComponents.IconButtonWithText(userAnimIcon, animText))
            {
                ImGui.CloseCurrentPopup();
                var perm = groupDto.GroupUserPermissions;
                perm.SetDisableAnimations(!perm.IsDisableAnimations());
                _ = ApiController.GroupChangeIndividualPermissionState(new(groupDto.Group, new UserData(ApiController.UID), perm));
            }
            UiSharedService.AttachToolTip("Sets your allowance for animations synchronization for users of this syncshell."
                + Environment.NewLine + "Disabling the synchronization will stop applying animations modifications for users of this syncshell."
                + Environment.NewLine + "Note: this setting might also affect sound synchronization"
                + Environment.NewLine + "Note: this setting can be forcefully overridden to 'disabled' through the syncshell owner."
                + Environment.NewLine + "Note: this setting does not apply to individual pairs that are also in the syncshell.");

            var vfxText = userVFXDisabled ? "Enable VFX sync" : "Disable VFX sync";
            if (ImGuiComponents.IconButtonWithText(userVFXIcon, vfxText))
            {
                ImGui.CloseCurrentPopup();
                var perm = groupDto.GroupUserPermissions;
                perm.SetDisableVFX(!perm.IsDisableVFX());
                _ = ApiController.GroupChangeIndividualPermissionState(new(groupDto.Group, new UserData(ApiController.UID), perm));
            }
            UiSharedService.AttachToolTip("Sets your allowance for VFX synchronization for users of this syncshell."
                                          + Environment.NewLine + "Disabling the synchronization will stop applying VFX modifications for users of this syncshell."
                                          + Environment.NewLine + "Note: this setting might also affect animation synchronization to some degree"
                                          + Environment.NewLine + "Note: this setting can be forcefully overridden to 'disabled' through the syncshell owner."
                                          + Environment.NewLine + "Note: this setting does not apply to individual pairs that are also in the syncshell.");

            if (isOwner || groupDto.GroupUserInfo.IsModerator())
            {
                ImGui.Separator();

                var changedToIcon = invitesEnabled ? FontAwesomeIcon.LockOpen : FontAwesomeIcon.Lock;
                if (ImGuiComponents.IconButtonWithText(changedToIcon, invitesEnabled ? "Lock Syncshell" : "Unlock Syncshell"))
                {
                    ImGui.CloseCurrentPopup();
                    var groupPerm = groupDto.GroupPermissions;
                    groupPerm.SetDisableInvites(invitesEnabled);
                    _ = ApiController.GroupChangeGroupPermissionState(new GroupPermissionDto(groupDto.Group, groupPerm));
                }
                UiSharedService.AttachToolTip("Change Syncshell joining permissions" + Environment.NewLine + "Syncshell is currently " + (invitesEnabled ? "open" : "closed") + " for people to join");

                if (isOwner)
                {
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Passport, "Change Password"))
                    {
                        ImGui.CloseCurrentPopup();
                        _isPasswordValid = true;
                        _showModalChangePassword = true;
                    }
                    UiSharedService.AttachToolTip("Change Syncshell Password");
                }

                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Broom, "Clear Syncshell") && UiSharedService.CtrlPressed())
                {
                    ImGui.CloseCurrentPopup();
                    _ = ApiController.GroupClear(groupDto);
                }
                UiSharedService.AttachToolTip("Hold CTRL and click to clear this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible." + Environment.NewLine
                    + "Clearing the Syncshell will remove all not pinned users from it.");

                var groupSoundsText = soundsDisabled ? "Enable syncshell sound sync" : "Disable syncshell sound sync";
                if (ImGuiComponents.IconButtonWithText(soundsIcon, groupSoundsText))
                {
                    ImGui.CloseCurrentPopup();
                    var perm = groupDto.GroupPermissions;
                    perm.SetPreferDisableSounds(!perm.IsPreferDisableSounds());
                    _ = ApiController.GroupChangeGroupPermissionState(new(groupDto.Group, perm));
                }
                UiSharedService.AttachToolTip("Sets syncshell-wide allowance for sound synchronization for all users." + Environment.NewLine
                    + "Note: users that are individually paired with others in the syncshell will ignore this setting." + Environment.NewLine
                    + "Note: if the synchronization is enabled, users can individually override this setting to disabled.");

                var groupAnimText = animDisabled ? "Enable syncshell animations sync" : "Disable syncshell animations sync";
                if (ImGuiComponents.IconButtonWithText(animIcon, groupAnimText))
                {
                    ImGui.CloseCurrentPopup();
                    var perm = groupDto.GroupPermissions;
                    perm.SetPreferDisableAnimations(!perm.IsPreferDisableAnimations());
                    _ = ApiController.GroupChangeGroupPermissionState(new(groupDto.Group, perm));
                }
                UiSharedService.AttachToolTip("Sets syncshell-wide allowance for animations synchronization for all users." + Environment.NewLine
                    + "Note: users that are individually paired with others in the syncshell will ignore this setting." + Environment.NewLine
                    + "Note: if the synchronization is enabled, users can individually override this setting to disabled.");

                var groupVFXText = vfxDisabled ? "Enable syncshell VFX sync" : "Disable syncshell VFX sync";
                if (ImGuiComponents.IconButtonWithText(vfxIcon, groupVFXText))
                {
                    ImGui.CloseCurrentPopup();
                    var perm = groupDto.GroupPermissions;
                    perm.SetPreferDisableVFX(!perm.IsPreferDisableVFX());
                    _ = ApiController.GroupChangeGroupPermissionState(new(groupDto.Group, perm));
                }
                UiSharedService.AttachToolTip("Sets syncshell-wide allowance for VFX synchronization for all users." + Environment.NewLine
                    + "Note: users that are individually paired with others in the syncshell will ignore this setting." + Environment.NewLine
                    + "Note: if the synchronization is enabled, users can individually override this setting to disabled.");

                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Envelope, "Single one-time invite"))
                {
                    ImGui.CloseCurrentPopup();
                    ImGui.SetClipboardText(ApiController.GroupCreateTempInvite(groupDto, 1).Result.FirstOrDefault() ?? string.Empty);
                }
                UiSharedService.AttachToolTip("Creates a single-use password for joining the syncshell which is valid for 24h and copies it to the clipboard.");

                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.MailBulk, "Bulk one-time invites"))
                {
                    ImGui.CloseCurrentPopup();
                    _showModalBulkOneTimeInvites = true;
                    _bulkOneTimeInvites.Clear();
                }
                UiSharedService.AttachToolTip("Opens a dialog to create up to 100 single-use passwords for joining the syncshell.");

                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Ban, "Manage Banlist"))
                {
                    ImGui.CloseCurrentPopup();
                    _showModalBanList = true;
                    _bannedUsers = ApiController.GroupGetBannedUsers(groupDto).Result;
                }

                if (isOwner)
                {
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, "Delete Syncshell") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
                    {
                        ImGui.CloseCurrentPopup();
                        _ = ApiController.GroupDelete(groupDto);
                    }
                    UiSharedService.AttachToolTip("Hold CTRL and Shift and click to delete this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible.");
                }
            }

            ImGui.EndPopup();
        }
    }

    private void DrawSyncshellList()
    {
        var ySize = _mainUi.TransferPartHeight == 0
            ? 1
            : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y) - _mainUi.TransferPartHeight - ImGui.GetCursorPosY();
        ImGui.BeginChild("list", new Vector2(_mainUi.WindowContentWidth, ySize), border: false);
        foreach (var entry in _pairManager.GroupPairs.OrderBy(g => g.Key.Group.AliasOrGID, StringComparer.OrdinalIgnoreCase).ToList())
        {
            UiSharedService.DrawWithID(entry.Key.Group.GID, () => DrawSyncshell(entry.Key, entry.Value));
        }
        ImGui.EndChild();
    }
}*/