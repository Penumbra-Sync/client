using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.WebAPI;
using System.Numerics;
using System.Globalization;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.API.Data.Enum;

namespace MareSynchronos.UI
{
    internal class GroupPanel
    {
        private readonly CompactUi _mainUi;
        private UiShared _uiShared;
        private Configuration _configuration;
        private ApiController _apiController;

        private readonly Dictionary<string, bool> _showGidForEntry = new(StringComparer.Ordinal);
        private string _editGroupEntry = string.Empty;
        private string _editGroupComment = string.Empty;
        private string _syncShellPassword = string.Empty;
        private string _syncShellToJoin = string.Empty;

        private bool _showModalEnterPassword;
        private bool _showModalCreateGroup;
        private bool _showModalChangePassword;
        private bool _showModalBanUser;
        private bool _showModalBanList = false;
        private bool _showModalBulkOneTimeInvites = false;
        private string _newSyncShellPassword = string.Empty;
        private string _banReason = string.Empty;
        private bool _isPasswordValid;
        private bool _errorGroupJoin;
        private bool _errorGroupCreate = false;
        private GroupPasswordDto? _lastCreatedGroup = null;
        private readonly Dictionary<string, bool> ExpandedGroupState = new(StringComparer.Ordinal);
        private List<BannedGroupUserDto> _bannedUsers = new();
        private List<string> _bulkOneTimeInvites = new();
        private bool _modalBanListOpened;
        private bool _modalBulkOneTimeInvitesOpened;
        private bool _banUserPopupOpen;
        private bool _modalChangePwOpened;
        private int _bulkInviteCount = 10;

        public GroupPanel(CompactUi mainUi, UiShared uiShared, Configuration configuration, ApiController apiController)
        {
            _mainUi = mainUi;
            _uiShared = uiShared;
            _configuration = configuration;
            _apiController = apiController;
        }

        public void DrawSyncshells()
        {
            UiShared.DrawWithID("addsyncshell", DrawAddSyncshell);
            UiShared.DrawWithID("syncshelllist", DrawSyncshellList);
            _mainUi.TransferPartHeight = ImGui.GetCursorPosY();
        }

        private void DrawAddSyncshell()
        {
            var buttonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Plus);
            ImGui.SetNextItemWidth(UiShared.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonSize.X);
            ImGui.InputTextWithHint("##syncshellid", "Syncshell GID/Alias (leave empty to create)", ref _syncShellToJoin, 20);
            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X);

            bool userCanJoinMoreGroups = _apiController.Groups.Count < _apiController.ServerInfo.MaxGroupsJoinedByUser;
            bool userCanCreateMoreGroups = _apiController.Groups.Count(u => string.Equals(u.Value.Owner.UID, _apiController.UID, StringComparison.Ordinal)) < _apiController.ServerInfo.MaxGroupsCreatedByUser;

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                if (_apiController.Groups.All(w => !string.Equals(w.Value.Group.GID, _syncShellToJoin, StringComparison.Ordinal) && !string.Equals(w.Value.Group.Alias, _syncShellToJoin, StringComparison.Ordinal))
                    && !string.IsNullOrEmpty(_syncShellToJoin))
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
            UiShared.AttachToolTip(_syncShellToJoin.IsNullOrEmpty()
                ? (userCanCreateMoreGroups ? "Create Syncshell" : $"You cannot create more than {_apiController.ServerInfo.MaxGroupsCreatedByUser} Syncshells")
                : (userCanJoinMoreGroups ? "Join Syncshell" + _syncShellToJoin : $"You cannot join more than {_apiController.ServerInfo.MaxGroupsJoinedByUser} Syncshells"));

            if (ImGui.BeginPopupModal("Enter Syncshell Password", ref _showModalEnterPassword, UiShared.PopupWindowFlags))
            {
                UiShared.TextWrapped("Before joining any Syncshells please be aware that you will be automatically paired with everyone in the Syncshell.");
                ImGui.Separator();
                UiShared.TextWrapped("Enter the password for Syncshell " + _syncShellToJoin + ":");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##password", _syncShellToJoin + " Password", ref _syncShellPassword, 255, ImGuiInputTextFlags.Password);
                if (_errorGroupJoin)
                {
                    UiShared.ColorTextWrapped($"An error occured during joining of this Syncshell: you either have joined the maximum amount of Syncshells ({_apiController.ServerInfo.MaxGroupsJoinedByUser}), " +
                        $"it does not exist, the password you entered is wrong, you already joined the Syncshell, the Syncshell is full ({_apiController.ServerInfo.MaxGroupUserCount} users) or the Syncshell has closed invites.",
                        new Vector4(1, 0, 0, 1));
                }
                if (ImGui.Button("Join " + _syncShellToJoin))
                {
                    var shell = _syncShellToJoin;
                    var pw = _syncShellPassword;
                    _errorGroupJoin = !_apiController.GroupJoin(new(new GroupData(shell), pw)).Result;
                    if (!_errorGroupJoin)
                    {
                        _syncShellToJoin = string.Empty;
                        _showModalEnterPassword = false;
                    }
                    _syncShellPassword = string.Empty;
                }
                UiShared.SetScaledWindowSize(290);
                ImGui.EndPopup();
            }

            if (ImGui.BeginPopupModal("Create Syncshell", ref _showModalCreateGroup, UiShared.PopupWindowFlags))
            {
                UiShared.TextWrapped("Press the button below to create a new Syncshell.");
                ImGui.SetNextItemWidth(200);
                if (ImGui.Button("Create Syncshell"))
                {
                    try
                    {
                        _lastCreatedGroup = _apiController.GroupCreate().Result;
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
                    UiShared.TextWrapped("You can change the Syncshell password later at any time.");
                }

                if (_errorGroupCreate)
                {
                    UiShared.ColorTextWrapped("You are already owner of the maximum amount of Syncshells (3) or joined the maximum amount of Syncshells (6). Relinquish ownership of your own Syncshells to someone else or leave existing Syncshells.",
                        new Vector4(1, 0, 0, 1));
                }

                UiShared.SetScaledWindowSize(350);
                ImGui.EndPopup();
            }

            ImGuiHelpers.ScaledDummy(2);
        }

        private void DrawSyncshellList()
        {
            var ySize = _mainUi.TransferPartHeight == 0
                ? 1
                : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y) - _mainUi.TransferPartHeight - ImGui.GetCursorPosY();
            ImGui.BeginChild("list", new Vector2(_mainUi.WindowContentWidth, ySize), false);
            foreach (var entry in _apiController.Groups.OrderBy(g => string.IsNullOrEmpty(g.Value.Group.Alias) ? g.Value.Group.GID : g.Value.Group.Alias).ToList())
            {
                UiShared.DrawWithID(entry.Value.Group.GID, () => DrawSyncshell(entry.Value));
            }
            ImGui.EndChild();
        }

        private void DrawSyncshell(GroupFullInfoDto groupDto)
        {
            var name = groupDto.Group.Alias ?? groupDto.GID;
            var pairsInGroup = _apiController.GroupPairedClients.Where(p => string.Equals(p.Value.GID, groupDto.GID, StringComparison.Ordinal)).ToList();
            if (!ExpandedGroupState.TryGetValue(groupDto.GID, out bool isExpanded))
            {
                isExpanded = false;
                ExpandedGroupState.Add(groupDto.GID, isExpanded);
            }
            var icon = isExpanded ? FontAwesomeIcon.CaretSquareDown : FontAwesomeIcon.CaretSquareRight;
            var collapseButton = UiShared.GetIconButtonSize(icon);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0, 0, 0));
            if (ImGuiComponents.IconButton(icon))
            {
                ExpandedGroupState[groupDto.GID] = !ExpandedGroupState[groupDto.GID];
            }
            ImGui.PopStyleColor(2);
            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + collapseButton.X);
            var pauseIcon = groupDto.GroupUserPermissions.HasFlag(GroupUserPermissions.Paused) ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
            if (ImGuiComponents.IconButton(pauseIcon))
            {
                var userPerm = groupDto.GroupUserPermissions ^ GroupUserPermissions.Paused;
                _ = _apiController.GroupChangeIndividualPermissionState(new GroupPairUserPermissionDto(groupDto.Group, new UserData(_apiController.UID), userPerm));
            }
            UiShared.AttachToolTip((groupDto.GroupUserPermissions.HasFlag(GroupUserPermissions.Paused) ? "Resume" : "Pause") + " pairing with all users in this Syncshell");
            ImGui.SameLine();

            var textIsGid = true;
            string groupName = groupDto.GroupAliasOrGID;

            if (string.Equals(groupDto.OwnerUID, _apiController.UID, StringComparison.Ordinal))
            {
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text(FontAwesomeIcon.Crown.ToIconString());
                ImGui.PopFont();
                UiShared.AttachToolTip("You are the owner of Syncshell " + groupName);
                ImGui.SameLine();
            }
            else if (groupDto.GroupUserInfo.HasFlag(GroupUserInfo.IsModerator))
            {
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text(FontAwesomeIcon.UserShield.ToIconString());
                ImGui.PopFont();
                UiShared.AttachToolTip("You are a moderator of Syncshell " + groupName);
                ImGui.SameLine();
            }

            _showGidForEntry.TryGetValue(groupDto.GID, out var showGidInsteadOfName);
            if (!showGidInsteadOfName && _configuration.GetCurrentServerGidComments().TryGetValue(groupDto.GID, out var groupComment))
            {
                if (!string.IsNullOrEmpty(groupComment))
                {
                    groupName = groupComment;
                    textIsGid = false;
                }
            }

            if (!string.Equals(_editGroupEntry, groupDto.GID, StringComparison.Ordinal))
            {
                if (textIsGid) ImGui.PushFont(UiBuilder.MonoFont);
                ImGui.TextUnformatted(groupName);
                if (textIsGid) ImGui.PopFont();
                UiShared.AttachToolTip("Left click to switch between GID display and comment" + Environment.NewLine +
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
                    _configuration.SetCurrentServerGidComment(_editGroupEntry, _editGroupComment);
                    _configuration.Save();
                    _editGroupComment = _configuration.GetCurrentServerGidComments().ContainsKey(groupDto.GID)
                        ? _configuration.GetCurrentServerGidComments()[groupDto.GID]
                        : string.Empty;
                    _editGroupEntry = groupDto.GID;
                }
            }
            else
            {
                var buttonSizes = UiShared.GetIconButtonSize(FontAwesomeIcon.Bars).X + UiShared.GetIconSize(FontAwesomeIcon.LockOpen).X;
                ImGui.SetNextItemWidth(UiShared.GetWindowContentRegionWidth() - ImGui.GetCursorPosX() - buttonSizes - ImGui.GetStyle().ItemSpacing.X * 2);
                if (ImGui.InputTextWithHint("", "Comment/Notes", ref _editGroupComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    _configuration.SetCurrentServerGidComment(groupDto.GID, _editGroupComment);
                    _configuration.Save();
                    _editGroupEntry = string.Empty;
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _editGroupEntry = string.Empty;
                }
                UiShared.AttachToolTip("Hit ENTER to save\nRight click to cancel");
            }

            UiShared.DrawWithID(groupDto.GID + "settings", () => DrawSyncShellButtons(groupDto, name));

            if (_showModalBanList && !_modalBanListOpened)
            {
                _modalBanListOpened = true;
                ImGui.OpenPopup("Manage Banlist for " + groupDto.GID);
            }

            if (!_showModalBanList) _modalBanListOpened = false;

            if (ImGui.BeginPopupModal("Manage Banlist for " + groupDto.GID, ref _showModalBanList, UiShared.PopupWindowFlags))
            {
                if (UiShared.IconTextButton(FontAwesomeIcon.Retweet, "Refresh Banlist from Server"))
                {
                    _bannedUsers = _apiController.GroupGetBannedUsers(groupDto).Result;
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
                        UiShared.TextWrapped(bannedUser.Reason);
                        ImGui.TableNextColumn();
                        if (UiShared.IconTextButton(FontAwesomeIcon.Check, "Unban"))
                        {
                            _ = _apiController.GroupUnbanUser(bannedUser);
                            _bannedUsers.RemoveAll(b => string.Equals(b.UID, bannedUser.UID, StringComparison.Ordinal));
                        }
                    }

                    ImGui.EndTable();
                }
                UiShared.SetScaledWindowSize(700, 300);
                ImGui.EndPopup();
            }

            if (_showModalChangePassword && !_modalChangePwOpened)
            {
                _modalChangePwOpened = true;
                ImGui.OpenPopup("Change Syncshell Password");
            }

            if (!_showModalChangePassword) _modalChangePwOpened = false;


            if (ImGui.BeginPopupModal("Change Syncshell Password", ref _showModalChangePassword, UiShared.PopupWindowFlags))
            {
                UiShared.TextWrapped("Enter the new Syncshell password for Syncshell " + name + " here.");
                UiShared.TextWrapped("This action is irreversible");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##changepw", "New password for " + name, ref _newSyncShellPassword, 255);
                if (ImGui.Button("Change password"))
                {
                    var pw = _newSyncShellPassword;
                    _isPasswordValid = _apiController.GroupChangePassword(new(groupDto.Group, pw)).Result;
                    _newSyncShellPassword = string.Empty;
                    if (_isPasswordValid) _showModalChangePassword = false;
                }

                if (!_isPasswordValid)
                {
                    UiShared.ColorTextWrapped("The selected password is too short. It must be at least 10 characters.", new Vector4(1, 0, 0, 1));
                }

                UiShared.SetScaledWindowSize(290);
                ImGui.EndPopup();
            }

            if (_showModalBulkOneTimeInvites && !_modalBulkOneTimeInvitesOpened)
            {
                _modalBulkOneTimeInvitesOpened = true;
                ImGui.OpenPopup("Create Bulk One-Time Invites");
            }

            if (!_showModalBulkOneTimeInvites) _modalBulkOneTimeInvitesOpened = false;

            if (ImGui.BeginPopupModal("Create Bulk One-Time Invites", ref _showModalBulkOneTimeInvites, UiShared.PopupWindowFlags))
            {
                UiShared.TextWrapped("This allows you to create up to 100 one-time invites at once for the Syncshell " + name + "." + Environment.NewLine
                    + "The invites are valid for 24h after creation and will automatically expire.");
                ImGui.Separator();
                if (_bulkOneTimeInvites.Count == 0)
                {
                    ImGui.SetNextItemWidth(-1);
                    ImGui.SliderInt("Amount##bulkinvites", ref _bulkInviteCount, 1, 100);
                    if (UiShared.IconTextButton(FontAwesomeIcon.MailBulk, "Create invites"))
                    {
                        _bulkOneTimeInvites = _apiController.GroupCreateTempInvite(groupDto, _bulkInviteCount).Result;
                    }
                }
                else
                {
                    UiShared.TextWrapped("A total of " + _bulkOneTimeInvites.Count + " invites have been created.");
                    if (UiShared.IconTextButton(FontAwesomeIcon.Copy, "Copy invites to clipboard"))
                    {
                        ImGui.SetClipboardText(string.Join(Environment.NewLine, _bulkOneTimeInvites));
                    }
                }

                UiShared.SetScaledWindowSize(290);
                ImGui.EndPopup();
            }

            ImGui.Indent(collapseButton.X);
            if (ExpandedGroupState[groupDto.GID])
            {
                pairsInGroup = pairsInGroup.OrderBy(p => string.Equals(p.Value.User.UID, groupDto.OwnerUID, StringComparison.Ordinal) ? 0 : 1)
                    .ThenBy(p => p.Value.GroupPairStatusInfo.HasFlag(GroupUserInfo.IsModerator) ? 0 : 1)
                    .ThenBy(p => p.Value.GroupPairStatusInfo.HasFlag(GroupUserInfo.IsPinned) ? 0 : 1)
                    .ThenBy(p => p.Value.UserAliasOrUID, StringComparer.OrdinalIgnoreCase).ToList();
                ImGui.Indent(ImGui.GetStyle().ItemSpacing.X / 2);
                ImGui.Separator();
                foreach (var pair in pairsInGroup)
                {
                    UiShared.DrawWithID(groupDto.GID + pair.Value.User.UID, () => DrawSyncshellPairedClient(pair.Value,
                        groupDto.OwnerUID,
                        string.Equals(groupDto.OwnerUID, _apiController.UID, StringComparison.Ordinal),
                        groupDto.GroupUserInfo.HasFlag(GroupUserInfo.IsModerator),
                        groupDto.GroupUserInfo.HasFlag(GroupUserInfo.IsPinned)));

                }

                ImGui.Separator();
                ImGui.Unindent(ImGui.GetStyle().ItemSpacing.X / 2);
            }
            ImGui.Unindent(collapseButton.X);
        }

        private void DrawSyncShellButtons(GroupFullInfoDto groupDto, string name)
        {
            bool invitesEnabled = !groupDto.GroupPermissions.HasFlag(GroupPermissions.DisableInvites);
            var lockedIcon = invitesEnabled ? FontAwesomeIcon.LockOpen : FontAwesomeIcon.Lock;
            var iconSize = UiShared.GetIconSize(lockedIcon);
            var diffLockUnlockIcons = invitesEnabled ? 0 : (UiShared.GetIconSize(FontAwesomeIcon.LockOpen).X - iconSize.X) / 2;
            var barbuttonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Bars);
            var isOwner = string.Equals(groupDto.OwnerUID, _apiController.UID, StringComparison.Ordinal);

            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - barbuttonSize.X - iconSize.X - diffLockUnlockIcons - ImGui.GetStyle().ItemSpacing.X);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text(lockedIcon.ToIconString());
            ImGui.PopFont();
            UiShared.AttachToolTip(invitesEnabled ? "Syncshell is open for new joiners" : "Syncshell is closed for new joiners");
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + diffLockUnlockIcons);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
            {
                ImGui.OpenPopup("ShellPopup");
            }

            if (ImGui.BeginPopup("ShellPopup"))
            {
                if (UiShared.IconTextButton(FontAwesomeIcon.ArrowCircleLeft, "Leave Syncshell"))
                {
                    if (UiShared.CtrlPressed())
                    {
                        _ = _apiController.GroupLeave(groupDto);
                    }
                }
                UiShared.AttachToolTip("Hold CTRL and click to leave this Syncshell" + (!string.Equals(groupDto.OwnerUID, _apiController.UID, StringComparison.Ordinal) ? string.Empty : Environment.NewLine
                    + "WARNING: This action is irreverisble" + Environment.NewLine + "Leaving an owned Syncshell will transfer the ownership to a random person in the Syncshell."));

                if (UiShared.IconTextButton(FontAwesomeIcon.Copy, "Copy ID"))
                {
                    ImGui.CloseCurrentPopup();
                    ImGui.SetClipboardText(groupDto.GroupAliasOrGID);
                }
                UiShared.AttachToolTip("Copy Syncshell ID to Clipboard");

                if (UiShared.IconTextButton(FontAwesomeIcon.StickyNote, "Copy Notes"))
                {
                    ImGui.CloseCurrentPopup();
                    ImGui.SetClipboardText(_uiShared.GetNotes(groupDto.GID));
                }
                UiShared.AttachToolTip("Copies all your notes for all users in this Syncshell to the clipboard." + Environment.NewLine + "They can be imported via Settings -> Privacy -> Import Notes from Clipboard");

                if (isOwner || groupDto.GroupUserInfo.HasFlag(GroupUserInfo.IsModerator))
                {
                    ImGui.Separator();

                    var changedToIcon = !invitesEnabled ? FontAwesomeIcon.LockOpen : FontAwesomeIcon.Lock;
                    if (UiShared.IconTextButton(changedToIcon, invitesEnabled ? "Lock Syncshell" : "Unlock Syncshell"))
                    {
                        ImGui.CloseCurrentPopup();
                        var groupPerm = groupDto.GroupPermissions ^ GroupPermissions.DisableInvites;
                        _apiController.GroupChangeGroupPermissionState(new GroupPermissionDto(groupDto.Group, groupPerm));
                    }
                    UiShared.AttachToolTip("Change Syncshell joining permissions" + Environment.NewLine + "Syncshell is currently " + (invitesEnabled ? "open" : "closed") + " for people to join");

                    if (isOwner)
                    {
                        if (UiShared.IconTextButton(FontAwesomeIcon.Passport, "Change Password"))
                        {
                            ImGui.CloseCurrentPopup();
                            _isPasswordValid = true;
                            _showModalChangePassword = true;
                        }
                        UiShared.AttachToolTip("Change Syncshell Password");
                    }

                    if (UiShared.IconTextButton(FontAwesomeIcon.Broom, "Clear Syncshell"))
                    {
                        if (UiShared.CtrlPressed())
                        {
                            ImGui.CloseCurrentPopup();
                            _ = _apiController.GroupClear(groupDto);
                        }
                    }
                    UiShared.AttachToolTip("Hold CTRL and click to clear this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible." + Environment.NewLine
                        + "Clearing the Syncshell will remove all not pinned users from it.");

                    if (UiShared.IconTextButton(FontAwesomeIcon.Envelope, "Single one-time invite"))
                    {
                        ImGui.CloseCurrentPopup();
                        ImGui.SetClipboardText(_apiController.GroupCreateTempInvite(groupDto, 1).Result.FirstOrDefault() ?? string.Empty);
                    }
                    UiShared.AttachToolTip("Creates a single-use password for joining the syncshell which is valid for 24h and copies it to the clipboard.");

                    if (UiShared.IconTextButton(FontAwesomeIcon.MailBulk, "Bulk one-time invites"))
                    {
                        ImGui.CloseCurrentPopup();
                        _showModalBulkOneTimeInvites = true;
                        _bulkOneTimeInvites.Clear();
                    }
                    UiShared.AttachToolTip("Opens a dialog to create up to 100 single-use passwords for joining the syncshell.");

                    if (UiShared.IconTextButton(FontAwesomeIcon.Ban, "Manage Banlist"))
                    {
                        ImGui.CloseCurrentPopup();
                        _showModalBanList = true;
                        _bannedUsers = _apiController.GroupGetBannedUsers(groupDto).Result;
                    }

                    if (isOwner)
                    {
                        if (UiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete Syncshell"))
                        {
                            if (UiShared.CtrlPressed() && UiShared.ShiftPressed())
                            {
                                ImGui.CloseCurrentPopup();
                                _ = _apiController.GroupDelete(groupDto);
                            }
                        }
                        UiShared.AttachToolTip("Hold CTRL and Shift and click to delete this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible.");
                    }
                }

                ImGui.EndPopup();
            }
        }

        private void DrawSyncshellPairedClient(GroupPairFullInfoDto entry, string ownerUid, bool isOwner, bool isModerator, bool isPausedByYou)
        {
            var plusButtonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Plus);
            var barButtonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Bars);
            var entryUID = entry.UserAliasOrUID;
            var textSize = ImGui.CalcTextSize(entryUID);
            var originalY = ImGui.GetCursorPosY();
            var userIsMod = entry.GroupPairStatusInfo.HasFlag(GroupUserInfo.IsModerator);
            var userIsOwner = string.Equals(entryUID, ownerUid, StringComparison.Ordinal);
            var isPinned = entry.GroupPairStatusInfo.HasFlag(GroupUserInfo.IsPinned);
            var isPaused = entry.GroupUserPermissions.HasFlag(GroupUserPermissions.Paused);

            var textPos = originalY + barButtonSize.Y / 2 - textSize.Y / 2;
            ImGui.SetCursorPosY(textPos);
            if (isPausedByYou || isPaused)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                UiShared.ColorText(FontAwesomeIcon.PauseCircle.ToIconString(), ImGuiColors.DalamudYellow);
                ImGui.PopFont();

                UiShared.AttachToolTip("Pairing status with " + entryUID + " is paused");
            }
            else
            {
                ImGui.PushFont(UiBuilder.IconFont);
                UiShared.ColorText(FontAwesomeIcon.Check.ToIconString(), ImGuiColors.ParsedGreen);
                ImGui.PopFont();

                UiShared.AttachToolTip("You are paired with " + entryUID);
            }

            if (userIsOwner)
            {
                ImGui.SameLine();
                ImGui.SetCursorPosY(textPos);
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextUnformatted(FontAwesomeIcon.Crown.ToIconString());
                ImGui.PopFont();
                UiShared.AttachToolTip("User is owner of this Syncshell");
            }
            else if (userIsMod)
            {
                ImGui.SameLine();
                ImGui.SetCursorPosY(textPos);
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextUnformatted(FontAwesomeIcon.UserShield.ToIconString());
                ImGui.PopFont();
                UiShared.AttachToolTip("User is moderator of this Syncshell");
            }
            else if (isPinned)
            {
                ImGui.SameLine();
                ImGui.SetCursorPosY(textPos);
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextUnformatted(FontAwesomeIcon.Thumbtack.ToIconString());
                ImGui.PopFont();
                UiShared.AttachToolTip("User is pinned in this Syncshell");
            }

            var textIsUid = true;
            _mainUi.ShowUidForEntry.TryGetValue(entry.UID, out var showUidInsteadOfName);
            if (!showUidInsteadOfName && _configuration.GetCurrentServerUidComments().TryGetValue(entry.UID, out var playerText))
            {
                if (string.IsNullOrEmpty(playerText))
                {
                    playerText = entryUID;
                }
                else
                {
                    textIsUid = false;
                }
            }
            else
            {
                playerText = entryUID;
            }

            bool plusButtonShown = !_apiController.PairedClients.Any(p => string.Equals(p.Key.User.UID, entry.UID, StringComparison.Ordinal));

            ImGui.SameLine();
            if (!string.Equals(_mainUi.EditNickEntry, entry.UID, StringComparison.Ordinal))
            {
                ImGui.SetCursorPosY(textPos);
                if (textIsUid) ImGui.PushFont(UiBuilder.MonoFont);
                ImGui.TextUnformatted(playerText);
                if (textIsUid) ImGui.PopFont();
                UiShared.AttachToolTip("Left click to switch between UID display and nick" + Environment.NewLine +
                              "Right click to change nick for " + entryUID);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    var prevState = textIsUid;
                    if (_mainUi.ShowUidForEntry.ContainsKey(entry.UID))
                    {
                        prevState = _mainUi.ShowUidForEntry[entry.UID];
                    }

                    _mainUi.ShowUidForEntry[entry.UID] = !prevState;
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _configuration.SetCurrentServerUidComment(_mainUi.EditNickEntry, _mainUi.EditUserComment);
                    _configuration.Save();
                    _mainUi.EditUserComment = _configuration.GetCurrentServerUidComments().ContainsKey(entry.UID)
                        ? _configuration.GetCurrentServerUidComments()[entry.UID]
                        : string.Empty;
                    _mainUi.EditNickEntry = entry.UID;
                }
            }
            else
            {
                ImGui.SetCursorPosY(originalY);
                var buttonSizes = (plusButtonShown ? plusButtonSize.X : 0) + barButtonSize.X;
                var buttons = plusButtonShown ? 2 : 1;

                ImGui.SetNextItemWidth(UiShared.GetWindowContentRegionWidth() - ImGui.GetCursorPosX() - buttonSizes - ImGui.GetStyle().ItemSpacing.X * buttons);
                if (ImGui.InputTextWithHint("", "Nick/Notes", ref _mainUi.EditUserComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    _configuration.SetCurrentServerUidComment(entry.UID, _mainUi.EditUserComment);
                    _configuration.Save();
                    _mainUi.EditNickEntry = string.Empty;
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _mainUi.EditNickEntry = string.Empty;
                }
                UiShared.AttachToolTip("Hit ENTER to save\nRight click to cancel");
            }

            if (plusButtonShown)
            {
                var barWidth = isOwner || (isModerator && !userIsMod && !userIsOwner)
                    ? barButtonSize.X + ImGui.GetStyle().ItemSpacing.X
                    : 0;
                ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - plusButtonSize.X - barWidth);
                ImGui.SetCursorPosY(originalY);

                if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
                {
                    _ = _apiController.UserAddPair(new UserDto(entry.User));
                }
                UiShared.AttachToolTip("Pair with " + entryUID + " individually");
            }

            if (isOwner || (isModerator && !userIsMod && !userIsOwner))
            {
                ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - barButtonSize.X);
                ImGui.SetCursorPosY(originalY);

                if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
                {
                    ImGui.OpenPopup("Popup");
                }
            }

            if (ImGui.BeginPopup("Popup"))
            {
                if ((!isModerator) && !(userIsMod || userIsOwner))
                {
                    var pinText = isPinned ? "Unpin user" : "Pin user";
                    if (UiShared.IconTextButton(FontAwesomeIcon.Thumbtack, pinText))
                    {
                        ImGui.CloseCurrentPopup();
                        var userInfo = entry.GroupPairStatusInfo ^ GroupUserInfo.IsPinned;
                        _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(entry.Group, entry.User, userInfo));
                    }
                    UiShared.AttachToolTip("Pin this user to the Syncshell. Pinned users will not be deleted in case of a manually initiated Syncshell clean");

                    if (UiShared.IconTextButton(FontAwesomeIcon.Trash, "Remove user"))
                    {
                        if (UiShared.CtrlPressed())
                        {
                            ImGui.CloseCurrentPopup();
                            _ = _apiController.GroupRemoveUser(entry);
                        }
                    }

                    UiShared.AttachToolTip("Hold CTRL and click to remove user " + (entry.UserAliasOrUID) + " from Syncshell");
                    if (UiShared.IconTextButton(FontAwesomeIcon.UserSlash, "Ban User"))
                    {
                        _showModalBanUser = true;
                        ImGui.CloseCurrentPopup();
                    }
                    UiShared.AttachToolTip("Ban user from this Syncshell");
                }

                if (isOwner)
                {
                    string modText = userIsMod ? "Demod user" : "Mod user";
                    if (UiShared.IconTextButton(FontAwesomeIcon.UserShield, modText))
                    {
                        if (UiShared.CtrlPressed())
                        {
                            ImGui.CloseCurrentPopup();
                            var userInfo = entry.GroupPairStatusInfo ^ GroupUserInfo.IsModerator;
                            _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(entry.Group, entry.User, userInfo));
                        }
                    }
                    UiShared.AttachToolTip("Hold CTRL to change the moderator status for " + (entry.UserAliasOrUID) + Environment.NewLine +
                        "Moderators can kick, ban/unban, pin/unpin users and clear the Syncshell.");
                    if (UiShared.IconTextButton(FontAwesomeIcon.Crown, "Transfer Ownership"))
                    {
                        if (UiShared.CtrlPressed() && UiShared.ShiftPressed())
                        {
                            ImGui.CloseCurrentPopup();
                            _ = _apiController.GroupChangeOwnership(entry);
                        }
                    }
                    UiShared.AttachToolTip("Hold CTRL and SHIFT and click to transfer ownership of this Syncshell to " + (entry.UserAliasOrUID) + Environment.NewLine + "WARNING: This action is irreversible.");
                }
                ImGui.EndPopup();
            }

            if (!plusButtonShown && !(isOwner || (isModerator && !userIsMod && !userIsOwner)))
            {
                ImGui.SameLine();
                ImGui.Dummy(barButtonSize with { X = 0 });
            }

            if (_showModalBanUser && !_banUserPopupOpen)
            {
                ImGui.OpenPopup("Ban User");
                _banUserPopupOpen = true;
            }

            if (!_showModalBanUser) _banUserPopupOpen = false;

            if (ImGui.BeginPopupModal("Ban User", ref _showModalBanUser, UiShared.PopupWindowFlags))
            {
                UiShared.TextWrapped("User " + (entry.UserAliasOrUID) + " will be banned and removed from this Syncshell.");
                ImGui.InputTextWithHint("##banreason", "Ban Reason", ref _banReason, 255);
                if (ImGui.Button("Ban User"))
                {
                    ImGui.CloseCurrentPopup();
                    var reason = _banReason;
                    _ = _apiController.GroupBanUser(entry, reason);
                    _banReason = string.Empty;
                }
                UiShared.TextWrapped("The reason will be displayed in the banlist. The current server-side alias if present (Vanity ID) will automatically be attached to the reason.");
                UiShared.SetScaledWindowSize(300);
                ImGui.EndPopup();
            }
        }
    }
}
