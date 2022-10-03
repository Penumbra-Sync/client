using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.API;
using MareSynchronos.WebAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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
        private string _newSyncShellPassword = string.Empty;
        private bool _isPasswordValid;
        private bool _errorGroupJoin;
        private bool _errorGroupCreate = false;
        private GroupCreatedDto? _lastCreatedGroup = null;
        private readonly Dictionary<string, bool> ExpandedGroupState = new(StringComparer.Ordinal);

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
            ImGui.InputTextWithHint("##syncshellid", "Syncshell GID/Alias", ref _syncShellToJoin, 20);
            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X);

            bool userCanJoinMoreGroups = _apiController.Groups.Count < _apiController.ServerInfo.MaxGroupsJoinedByUser;
            bool userCanCreateMoreGroups = _apiController.Groups.Count(u => string.Equals(u.OwnedBy, _apiController.UID, StringComparison.Ordinal)) < _apiController.ServerInfo.MaxGroupsCreatedByUser;

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                if (_apiController.Groups.All(w => !string.Equals(w.GID, _syncShellToJoin, StringComparison.Ordinal)) && !string.IsNullOrEmpty(_syncShellToJoin))
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

            if (ImGui.BeginPopupModal("Enter Syncshell Password", ref _showModalEnterPassword, ImGuiWindowFlags.AlwaysAutoResize))
            {
                UiShared.TextWrapped("Before joining any Syncshells please be aware that you will be automatically paired with everyone in the Syncshell.");
                ImGui.Separator();
                UiShared.TextWrapped("Enter the password for Syncshell " + _syncShellToJoin + ":");
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
                    _errorGroupJoin = !_apiController.SendGroupJoin(shell, pw).Result;
                    if (!_errorGroupJoin)
                    {
                        _syncShellToJoin = string.Empty;
                        _showModalEnterPassword = false;
                    }
                    _syncShellPassword = string.Empty;
                }
                ImGui.EndPopup();
            }

            if (ImGui.BeginPopupModal("Create Syncshell", ref _showModalCreateGroup))
            {
                ImGui.SetWindowSize(new(400, 200));
                UiShared.TextWrapped("Press the button below to create a new Syncshell.");
                ImGui.SetNextItemWidth(200);
                if (ImGui.Button("Create Syncshell"))
                {
                    try
                    {
                        _lastCreatedGroup = _apiController.CreateGroup().Result;
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
                    ImGui.TextUnformatted("Syncshell ID: " + _lastCreatedGroup.GID);
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

                ImGui.EndPopup();
            }

            ImGuiHelpers.ScaledDummy(2);
        }

        private void DrawSyncshellList()
        {
            var ySize = _mainUi.TransferPartHeight == 0
                ? 1
                : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y) - _mainUi.TransferPartHeight - ImGui.GetCursorPosY();
            ImGui.BeginChild("list", new Vector2(_mainUi._windowContentWidth, ySize), false);
            foreach (var entry in _apiController.Groups.OrderBy(g => g.Alias ?? g.GID).ToList())
            {
                UiShared.DrawWithID(entry.GID, () => DrawSyncshell(entry));
            }
            ImGui.EndChild();
        }

        private void DrawSyncshell(GroupDto group)
        {
            var name = group.Alias ?? group.GID;
            var pairsInGroup = _apiController.GroupPairedClients.Where(p => string.Equals(p.GroupGID, group.GID, StringComparison.Ordinal)).ToList();
            if (!ExpandedGroupState.TryGetValue(group.GID, out bool isExpanded))
            {
                isExpanded = false;
                ExpandedGroupState.Add(group.GID, isExpanded);
            }
            var icon = isExpanded ? FontAwesomeIcon.CaretSquareDown : FontAwesomeIcon.CaretSquareRight;
            var collapseButton = UiShared.GetIconButtonSize(icon);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0, 0, 0));
            if (ImGuiComponents.IconButton(icon))
            {
                ExpandedGroupState[group.GID] = !ExpandedGroupState[group.GID];
            }
            ImGui.PopStyleColor(2);
            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + collapseButton.X);
            var pauseIcon = (group.IsPaused ?? false) ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
            if (ImGuiComponents.IconButton(pauseIcon))
            {
                _ = _apiController.SendPauseGroup(group.GID, !group.IsPaused ?? false);
            }
            UiShared.AttachToolTip(((group.IsPaused ?? false) ? "Resume" : "Pause") + " pairing with all users in this Syncshell");
            ImGui.SameLine();

            var groupName = group.Alias ?? group.GID;
            var textIsGid = true;

            if (string.Equals(group.OwnedBy, _apiController.UID, StringComparison.Ordinal))
            {
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text(FontAwesomeIcon.Crown.ToIconString());
                ImGui.PopFont();
                UiShared.AttachToolTip("You are the owner of Syncshell " + groupName);
                ImGui.SameLine();
            }

            _showGidForEntry.TryGetValue(group.GID, out var showGidInsteadOfName);
            if (!showGidInsteadOfName && _configuration.GetCurrentServerGidComments().TryGetValue(group.GID, out var groupComment))
            {
                if (!string.IsNullOrEmpty(groupComment))
                {
                    groupName = groupComment;
                    textIsGid = false;
                }
            }

            if (!string.Equals(_editGroupEntry, group.GID, StringComparison.Ordinal))
            {
                if (textIsGid) ImGui.PushFont(UiBuilder.MonoFont);
                ImGui.TextUnformatted(groupName);
                if (textIsGid) ImGui.PopFont();
                UiShared.AttachToolTip("Left click to switch between GID display and comment" + Environment.NewLine +
                              "Right click to change comment for " + groupName + Environment.NewLine + Environment.NewLine
                              + "Users: " + (pairsInGroup.Count + 1) + ", Owner: " + group.OwnedBy);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    var prevState = textIsGid;
                    if (_showGidForEntry.ContainsKey(group.GID))
                    {
                        prevState = _showGidForEntry[group.GID];
                    }

                    _showGidForEntry[group.GID] = !prevState;
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _configuration.SetCurrentServerGidComment(_editGroupEntry, _editGroupComment);
                    _configuration.Save();
                    _editGroupComment = _configuration.GetCurrentServerGidComments().ContainsKey(group.GID)
                        ? _configuration.GetCurrentServerGidComments()[group.GID]
                        : string.Empty;
                    _editGroupEntry = group.GID;
                }
            }
            else
            {
                ImGui.SetNextItemWidth(UiShared.GetWindowContentRegionWidth() - ImGui.GetCursorPosX());
                if (ImGui.InputTextWithHint("", "Comment/Notes", ref _editGroupComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    _configuration.SetCurrentServerGidComment(group.GID, _editGroupComment);
                    _configuration.Save();
                    _editGroupEntry = string.Empty;
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _editGroupEntry = string.Empty;
                }
                UiShared.AttachToolTip("Hit ENTER to save\nRight click to cancel");
            }

            UiShared.DrawWithID(group.GID + "settings", () => DrawSyncShellButtons(group, name));

            ImGui.Indent(collapseButton.X);
            if (ExpandedGroupState[group.GID])
            {
                pairsInGroup = pairsInGroup.OrderBy(p => string.Equals(p.UserUID, group.OwnedBy, StringComparison.Ordinal) ? 0 : 1).ThenBy(p => p.IsPinned ?? false).ThenBy(p => p.UserAlias ?? p.UserUID).ToList();
                ImGui.Indent(ImGui.GetStyle().ItemSpacing.X / 2);
                ImGui.Separator();
                foreach (var pair in pairsInGroup)
                {
                    UiShared.DrawWithID(group.GID + pair.UserUID, () => DrawSyncshellPairedClient(pair, string.Equals(group.OwnedBy, _apiController.UID, StringComparison.Ordinal), group?.IsPaused ?? false));
                }

                ImGui.Separator();
                ImGui.Unindent(ImGui.GetStyle().ItemSpacing.X / 2);
            }
            ImGui.Unindent(collapseButton.X);
        }

        private void DrawSyncShellButtons(GroupDto entry, string name)
        {
            bool invitesEnabled = entry.InvitesEnabled ?? true;
            var lockedIcon = invitesEnabled ? FontAwesomeIcon.LockOpen : FontAwesomeIcon.Lock;
            var iconSize = UiShared.GetIconSize(lockedIcon);
            var barbuttonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Bars);

            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - barbuttonSize.X - iconSize.X - ImGui.GetStyle().ItemSpacing.X);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text(lockedIcon.ToIconString());
            ImGui.PopFont();
            UiShared.AttachToolTip(invitesEnabled ? "Syncshell is open for new joiners" : "Syncshell is closed for new joiners");
            ImGui.SameLine();
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
                        _ = _apiController.SendLeaveGroup(entry.GID);
                    }
                }
                UiShared.AttachToolTip("Hold CTRL and click to leave this Syncshell" + (!string.Equals(entry.OwnedBy, _apiController.UID, StringComparison.Ordinal) ? string.Empty : Environment.NewLine
                    + "WARNING: This action is irreverisble" + Environment.NewLine + "Leaving an owned Syncshell will transfer the ownership to a random person in the Syncshell."));

                if (UiShared.IconTextButton(FontAwesomeIcon.Copy, "Copy ID"))
                {
                    ImGui.SetClipboardText(entry.Alias ?? entry.GID);
                }
                UiShared.AttachToolTip("Copy Syncshell ID to Clipboard");

                if (string.Equals(entry.OwnedBy, _apiController.UID, StringComparison.Ordinal))
                {
                    ImGui.Separator();

                    if (UiShared.IconTextButton(lockedIcon, invitesEnabled ? "Lock Syncshell" : "Unlock Syncshell"))
                    {
                        _ = _apiController.SendGroupChangeInviteState(entry.GID, !entry.InvitesEnabled ?? true);
                    }
                    UiShared.AttachToolTip("Change Syncshell joining permissions" + Environment.NewLine + "Syncshell is currently " + (invitesEnabled ? "open" : "closed") + " for people to join");

                    if (UiShared.IconTextButton(FontAwesomeIcon.Passport, "Change Password"))
                    {
                        ImGui.OpenPopup("Change Syncshell Password");
                        _isPasswordValid = true;
                        _showModalChangePassword = true;
                    }
                    UiShared.AttachToolTip("Change Syncshell Password");

                    if (ImGui.BeginPopupModal("Change Syncshell Password", ref _showModalChangePassword, ImGuiWindowFlags.AlwaysAutoResize))
                    {
                        UiShared.TextWrapped("Enter the new Syncshell password for Syncshell " + name + " here.");
                        UiShared.TextWrapped("This action is irreversible");
                        ImGui.InputTextWithHint("##changepw", "New password for " + name, ref _newSyncShellPassword, 255);
                        if (ImGui.Button("Change password"))
                        {
                            var pw = _newSyncShellPassword;
                            _isPasswordValid = _apiController.ChangeGroupPassword(entry.GID, pw).Result;
                            _newSyncShellPassword = string.Empty;
                            if (_isPasswordValid) _showModalChangePassword = false;
                        }

                        if (!_isPasswordValid)
                        {
                            UiShared.ColorTextWrapped("The selected password is too short. It must be at least 10 characters.", new Vector4(1, 0, 0, 1));
                        }

                        ImGui.EndPopup();
                    }

                    if (UiShared.IconTextButton(FontAwesomeIcon.Broom, "Clear Syncshell"))
                    {
                        if (UiShared.CtrlPressed())
                        {
                            _ = _apiController.SendClearGroup(entry.GID);
                        }
                    }
                    UiShared.AttachToolTip("Hold CTRL and click to clear this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible." + Environment.NewLine
                        + "Clearing the Syncshell will remove all not pinned users from it.");

                    if (UiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete Syncshell"))
                    {
                        if (UiShared.CtrlPressed() && UiShared.ShiftPressed())
                        {
                            _ = _apiController.SendDeleteGroup(entry.GID);
                        }
                    }
                    UiShared.AttachToolTip("Hold CTRL and Shift and click to delete this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible.");
                }

                ImGui.EndPopup();
            }
        }

        private void DrawSyncshellPairedClient(GroupPairDto entry, bool isOwner, bool isPausedByYou)
        {
            var plusButtonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Plus);
            var barButtonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Bars);
            var entryUID = string.IsNullOrEmpty(entry.UserAlias) ? entry.UserUID : entry.UserAlias;
            var textSize = ImGui.CalcTextSize(entryUID);
            var originalY = ImGui.GetCursorPosY();
            var buttonSizes = plusButtonSize.Y;

            var textPos = originalY + plusButtonSize.Y / 2 - textSize.Y / 2;
            ImGui.SetCursorPosY(textPos);
            if (isPausedByYou || (entry.IsPaused ?? false))
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

            if (entry.IsPinned ?? false)
            {
                ImGui.SameLine();
                ImGui.SetCursorPosY(textPos);
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextUnformatted(FontAwesomeIcon.Thumbtack.ToIconString());
                ImGui.PopFont();
                UiShared.AttachToolTip("User is pinned in this Syncshell");
            }

            var textIsUid = true;
            _mainUi.ShowUidForEntry.TryGetValue(entry.UserUID, out var showUidInsteadOfName);
            if (!showUidInsteadOfName && _configuration.GetCurrentServerUidComments().TryGetValue(entry.UserUID, out var playerText))
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

            ImGui.SameLine();
            if (!string.Equals(_mainUi.EditNickEntry, entry.UserUID, StringComparison.Ordinal))
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
                    if (_mainUi.ShowUidForEntry.ContainsKey(entry.UserUID))
                    {
                        prevState = _mainUi.ShowUidForEntry[entry.UserUID];
                    }

                    _mainUi.ShowUidForEntry[entry.UserUID] = !prevState;
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _configuration.SetCurrentServerUidComment(_mainUi.EditNickEntry, _mainUi.EditUserComment);
                    _configuration.Save();
                    _mainUi.EditUserComment = _configuration.GetCurrentServerUidComments().ContainsKey(entry.UserUID)
                        ? _configuration.GetCurrentServerUidComments()[entry.UserUID]
                        : string.Empty;
                    _mainUi.EditNickEntry = entry.UserUID;
                }
            }
            else
            {
                ImGui.SetCursorPosY(originalY);

                ImGui.SetNextItemWidth(UiShared.GetWindowContentRegionWidth() - ImGui.GetCursorPosX() - buttonSizes - ImGui.GetStyle().ItemSpacing.X * 2);
                if (ImGui.InputTextWithHint("", "Nick/Notes", ref _mainUi.EditUserComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    _configuration.SetCurrentServerUidComment(entry.UserUID, _mainUi.EditUserComment);
                    _configuration.Save();
                    _mainUi.EditNickEntry = string.Empty;
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _mainUi.EditNickEntry = string.Empty;
                }
                UiShared.AttachToolTip("Hit ENTER to save\nRight click to cancel");
            }

            bool plusButtonShown = !_apiController.PairedClients.Any(p => string.Equals(p.OtherUID, entry.UserUID, StringComparison.Ordinal));

            if (plusButtonShown)
            {
                ImGui.SetCursorPosY(originalY);
                ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - plusButtonSize.X - (isOwner ? barButtonSize.X + ImGui.GetStyle().ItemSpacing.X : 0));

                if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
                {
                    _ = _apiController.SendPairedClientAddition(entry.UserUID);
                }
                UiShared.AttachToolTip("Pair with " + entryUID + " individually");
            }

            if (isOwner)
            {
                ImGui.SetCursorPosY(originalY);
                var subtractedWidth = plusButtonShown ? (plusButtonSize.X) : 0;
                ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - barButtonSize.X);

                if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
                {
                    ImGui.OpenPopup("Popup");
                }
            }

            if (ImGui.BeginPopup("Popup"))
            {
                if (UiShared.IconTextButton(FontAwesomeIcon.Thumbtack, "Pin user"))
                {
                    _ = _apiController.SendChangeUserPinned(entry.GroupGID, entry.UserUID, !entry.IsPinned ?? false);
                }
                UiShared.AttachToolTip("Pin this user to the Syncshell. Pinned users will not be deleted in case of a manually initiated Syncshell clean");
                if (UiShared.IconTextButton(FontAwesomeIcon.Crown, "Transfer Ownership"))
                {
                    if (UiShared.CtrlPressed() && UiShared.ShiftPressed())
                    {
                        _ = _apiController.ChangeOwnerOfGroup(entry.GroupGID, entry.UserUID);
                    }
                }
                UiShared.AttachToolTip("Hold CTRL and SHIFT and click to transfer ownership of this Syncshell to " + (entry.UserAlias ?? entry.UserUID) + Environment.NewLine + "WARNING: This action is irreversible.");
                if (UiShared.IconTextButton(FontAwesomeIcon.Trash, "Remove user"))
                {
                    if (UiShared.CtrlPressed())
                    {
                        _ = _apiController.SendRemoveUserFromGroup(entry.GroupGID, entry.UserUID);
                    }
                }
                UiShared.AttachToolTip("Hold CTRL and click to remove user " + (entry.UserAlias ?? entry.UserUID) + " from Syncshell");
                ImGui.EndPopup();
            }
        }
    }
}
