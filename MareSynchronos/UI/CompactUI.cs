using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.API;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI;

public class CompactUi : Window, IDisposable
{
    private readonly ApiController _apiController;
    private readonly Configuration _configuration;
    private readonly Dictionary<string, bool> _showUidForEntry = new();
    private readonly UiShared _uiShared;
    private readonly WindowSystem _windowSystem;
    private string _characterOrCommentFilter = string.Empty;

    private string _editCharComment = string.Empty;
    private string _editNickEntry = string.Empty;
    private string _pairToAdd = string.Empty;
    private string _syncShellPassword = string.Empty;
    private string _syncShellToJoin = string.Empty;
    private readonly Stopwatch _timeout = new();
    private bool _buttonState;

    private float _transferPartHeight = 0;

    private float _windowContentWidth = 0;

    private bool _showModalEnterPassword;
    private bool _showModalCreateGroup;
    private bool _showModalChangePassword;
    private string _newSyncShellPassword = string.Empty;
    private bool _isPasswordValid;
    private bool _errorGroupJoin;
    private bool _errorGroupCreate = false;
    private GroupCreatedDto? _lastCreatedGroup = null;

    public CompactUi(WindowSystem windowSystem,
        UiShared uiShared, Configuration configuration, ApiController apiController) : base("###MareSynchronosMainUI")
    {

#if DEBUG
        string dateTime = "DEV VERSION";
        try
        {
            dateTime = VariousExtensions.GetLinkerTime(Assembly.GetCallingAssembly()).ToString("yyyyMMddHHmmss");
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not get assembly name");
            Logger.Warn(ex.Message);
            Logger.Warn(ex.StackTrace);
        }
        this.WindowName = "Mare Synchronos " + dateTime + "###MareSynchronosMainUI";
        Toggle();
#else
        this.WindowName = "Mare Synchronos " + Assembly.GetExecutingAssembly().GetName().Version;
#endif
        Logger.Verbose("Creating " + nameof(CompactUi));

        _windowSystem = windowSystem;
        _uiShared = uiShared;
        _configuration = configuration;
        _apiController = apiController;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(300, 400),
            MaximumSize = new Vector2(300, 2000),
        };

        windowSystem.AddWindow(this);
    }

    public event SwitchUi? OpenSettingsUi;
    public void Dispose()
    {
        Logger.Verbose("Disposing " + nameof(CompactUi));
        _windowSystem.RemoveWindow(this);
    }

    public override void Draw()
    {
        _windowContentWidth = UiShared.GetWindowContentRegionWidth();
        UiShared.DrawWithID("header", DrawUIDHeader);
        ImGui.Separator();
        UiShared.DrawWithID("serverstatus", DrawServerStatus);

        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.Separator();
            if (ImGui.BeginTabBar("maintabs"))
            {
                if (ImGui.BeginTabItem("Individuals"))
                {
                    UiShared.DrawWithID("pairlist", DrawPairList);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Syncshells"))
                {
                    UiShared.DrawWithID("synchshells", DrawSyncshells);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            ImGui.Separator();
            UiShared.DrawWithID("transfers", DrawTransfers);
            _transferPartHeight = ImGui.GetCursorPosY() - _transferPartHeight;
        }
    }

    public override void OnClose()
    {
        _editNickEntry = string.Empty;
        _editCharComment = string.Empty;
        base.OnClose();
    }
    private void DrawAddPair()
    {
        var buttonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Plus);
        ImGui.SetNextItemWidth(UiShared.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonSize.X);
        ImGui.InputTextWithHint("##otheruid", "Other players UID", ref _pairToAdd, 10);
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
        {
            if (_apiController.PairedClients.All(w => w.OtherUID != _pairToAdd))
            {
                _ = _apiController.SendPairedClientAddition(_pairToAdd);
                _pairToAdd = string.Empty;
            }
        }
        UiShared.AttachToolTip("Pair with " + (_pairToAdd.IsNullOrEmpty() ? "other user" : _pairToAdd));

        ImGuiHelpers.ScaledDummy(2);
    }

    private void DrawFilter()
    {
        var buttonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.ArrowUp);
        var playButtonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Play);
        if (!_configuration.ReverseUserSort)
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowDown))
            {
                _configuration.ReverseUserSort = true;
                _configuration.Save();
            }
            UiShared.AttachToolTip("Sort by newest additions first");
        }
        else
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowUp))
            {
                _configuration.ReverseUserSort = false;
                _configuration.Save();
            }
            UiShared.AttachToolTip("Sort by oldest additions first");
        }
        ImGui.SameLine();

        var users = GetFilteredUsers().ToList();
        var userCount = users.Count;

        var spacing = userCount > 0
            ? playButtonSize.X + ImGui.GetStyle().ItemSpacing.X * 2
            : ImGui.GetStyle().ItemSpacing.X;

        ImGui.SetNextItemWidth(_windowContentWidth - buttonSize.X - spacing);
        ImGui.InputTextWithHint("##filter", "Filter for UID/notes", ref _characterOrCommentFilter, 255);

        if (userCount == 0) return;
        ImGui.SameLine();

        var pausedUsers = users.Where(u => u.IsPaused).ToList();
        var resumedUsers = users.Where(u => !u.IsPaused).ToList();

        switch (_buttonState)
        {
            case true when !pausedUsers.Any():
                _buttonState = false;
                break;
            case false when !resumedUsers.Any():
                _buttonState = true;
                break;
            case true:
                users = pausedUsers;
                break;
            case false:
                users = resumedUsers;
                break;
        }

        var button = _buttonState ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;

        if (!_timeout.IsRunning || _timeout.ElapsedMilliseconds > 15000)
        {
            _timeout.Reset();

            if (ImGuiComponents.IconButton(button))
            {
                if (UiShared.CtrlPressed())
                {
                    Logger.Debug(users.Count.ToString());
                    foreach (var entry in users)
                    {
                        _ = _apiController.SendPairedClientPauseChange(entry.OtherUID, !entry.IsPaused);
                    }

                    _timeout.Start();
                    _buttonState = !_buttonState;
                }
            }
            UiShared.AttachToolTip($"Hold Control to {(button == FontAwesomeIcon.Play ? "resume" : "pause")} pairing with {users.Count} out of {userCount} displayed users.");
        }
        else
        {
            var availableAt = (15000 - _timeout.ElapsedMilliseconds) / 1000;
            ImGuiComponents.DisabledButton(button);
            UiShared.AttachToolTip($"Next execution is available at {availableAt} seconds");
        }
    }

    private void DrawPairedClient(ClientPairDto entry)
    {
        var pauseIcon = entry.IsPaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;

        var buttonSize = UiShared.GetIconButtonSize(pauseIcon);
        var trashButtonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Trash);
        var entryUID = string.IsNullOrEmpty(entry.VanityUID) ? entry.OtherUID : entry.VanityUID;
        var textSize = ImGui.CalcTextSize(entryUID);
        var originalY = ImGui.GetCursorPosY();
        var buttonSizes = buttonSize.Y + trashButtonSize.Y;

        var textPos = originalY + buttonSize.Y / 2 - textSize.Y / 2;
        ImGui.SetCursorPosY(textPos);
        if (!entry.IsSynced)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            UiShared.ColorText(FontAwesomeIcon.ArrowUp.ToIconString(), ImGuiColors.DalamudRed);
            ImGui.PopFont();

            UiShared.AttachToolTip(entryUID + " has not added you back");
        }
        else if (entry.IsPaused || entry.IsPausedFromOthers)
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

        var textIsUid = true;
        _showUidForEntry.TryGetValue(entry.OtherUID, out var showUidInsteadOfName);
        if (!showUidInsteadOfName && _configuration.GetCurrentServerUidComments().TryGetValue(entry.OtherUID, out var playerText))
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
        if (_editNickEntry != entry.OtherUID)
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
                if (_showUidForEntry.ContainsKey(entry.OtherUID))
                {
                    prevState = _showUidForEntry[entry.OtherUID];
                }

                _showUidForEntry[entry.OtherUID] = !prevState;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _configuration.SetCurrentServerUidComment(_editNickEntry, _editCharComment);
                _configuration.Save();
                _editCharComment = _configuration.GetCurrentServerUidComments().ContainsKey(entry.OtherUID)
                    ? _configuration.GetCurrentServerUidComments()[entry.OtherUID]
                    : string.Empty;
                _editNickEntry = entry.OtherUID;
            }
        }
        else
        {
            ImGui.SetCursorPosY(originalY);

            ImGui.SetNextItemWidth(UiShared.GetWindowContentRegionWidth() - ImGui.GetCursorPosX() - buttonSizes - ImGui.GetStyle().ItemSpacing.X * 2);
            if (ImGui.InputTextWithHint("", "Nick/Notes", ref _editCharComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _configuration.SetCurrentServerUidComment(entry.OtherUID, _editCharComment);
                _configuration.Save();
                _editNickEntry = string.Empty;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _editNickEntry = string.Empty;
            }
            UiShared.AttachToolTip("Hit ENTER to save\nRight click to cancel");
        }

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X);
        ImGui.SetCursorPosY(originalY);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
        {
            if (UiShared.CtrlPressed())
            {
                _ = _apiController.SendPairedClientRemoval(entry.OtherUID);
            }
        }
        UiShared.AttachToolTip("Hold CTRL and click to unpair permanently from " + entryUID);

        if (entry.IsSynced)
        {
            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X - ImGui.GetStyle().ItemSpacing.X - trashButtonSize.X);
            ImGui.SetCursorPosY(originalY);
            if (ImGuiComponents.IconButton(pauseIcon))
            {
                _ = _apiController.SendPairedClientPauseChange(entry.OtherUID, !entry.IsPaused);
            }
            UiShared.AttachToolTip(!entry.IsPaused
                ? "Pause pairing with " + entryUID
                : "Resume pairing with " + entryUID);
        }
    }

    private void DrawSyncshells()
    {
        UiShared.DrawWithID("addsyncshell", DrawAddSyncshell);
        UiShared.DrawWithID("syncshells", DrawSyncshellList);
        _transferPartHeight = ImGui.GetCursorPosY();
    }

    private void DrawPairList()
    {
        UiShared.DrawWithID("addpair", DrawAddPair);
        UiShared.DrawWithID("pairs", DrawPairs);
        _transferPartHeight = ImGui.GetCursorPosY();
        UiShared.DrawWithID("filter", DrawFilter);
    }

    private void DrawPairs()
    {
        var ySize = _transferPartHeight == 0
            ? 1
            : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y) - _transferPartHeight - ImGui.GetCursorPosY();
        var users = GetFilteredUsers();

        if (_configuration.ReverseUserSort) users = users.Reverse();

        ImGui.BeginChild("list", new Vector2(_windowContentWidth, ySize), false);
        foreach (var entry in users.ToList())
        {
            UiShared.DrawWithID(entry.OtherUID, () => DrawPairedClient(entry));
        }
        ImGui.EndChild();
    }

    private void DrawAddSyncshell()
    {
        var buttonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Plus);
        ImGui.SetNextItemWidth(UiShared.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonSize.X);
        ImGui.InputTextWithHint("##otheruid", "Syncshell ID", ref _syncShellToJoin, 20);
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
        {
            if (_apiController.Groups.All(w => w.GID != _syncShellToJoin) && !string.IsNullOrEmpty(_syncShellToJoin))
            {
                _errorGroupJoin = false;
                _showModalEnterPassword = true;
                ImGui.OpenPopup("Enter Syncshell Password");
            }
            else
            {
                _lastCreatedGroup = null;
                _errorGroupCreate = false;
                _showModalCreateGroup = true;
                ImGui.OpenPopup("Create Syncshell");
            }
        }
        UiShared.AttachToolTip(_syncShellToJoin.IsNullOrEmpty() ? "Create Syncshell" : "Join Syncshell" + _syncShellToJoin);

        if (ImGui.BeginPopupModal("Enter Syncshell Password", ref _showModalEnterPassword, ImGuiWindowFlags.AlwaysAutoResize))
        {
            UiShared.TextWrapped("Before joining any Syncshells please be aware that you will be automatically paired with everyone in the Syncshell.");
            ImGui.Separator();
            UiShared.TextWrapped("Enter the password for Syncshell " + _syncShellToJoin + ":");
            ImGui.InputTextWithHint("##password", _syncShellToJoin + " Password", ref _syncShellPassword, 255, ImGuiInputTextFlags.Password);
            if (_errorGroupJoin)
            {
                UiShared.ColorTextWrapped("An error occured during joining of this Syncshell: you either have joined the maximum amount of Syncshells (6), it does not exist, the password you entered is wrong, you already joined the Syncshell, the Syncshell is full (100 users) or the Syncshell has closed invites.",
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
        var ySize = _transferPartHeight == 0
            ? 1
            : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y) - _transferPartHeight - ImGui.GetCursorPosY();
        ImGui.BeginChild("list", new Vector2(_windowContentWidth, ySize), false);
        foreach (var entry in _apiController.Groups.ToList())
        {
            UiShared.DrawWithID(entry.GID, () => DrawSyncshell(entry));
        }
        ImGui.EndChild();
    }

    private void DrawSyncshell(GroupDto entry)
    {
        var name = entry.Alias ?? entry.GID;
        var pairsInGroup = _apiController.GroupPairedClients.Where(p => p.GroupGID == entry.GID).ToList();
        if (ImGui.CollapsingHeader(name + " (" + (pairsInGroup.Count + 1) + " users)", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            UiShared.DrawWithID(entry.GID + "settings", () => DrawSyncshellHeader(entry, name));
            pairsInGroup = pairsInGroup.OrderBy(p => p.UserUID == entry.OwnedBy ? 0 : 1).ThenBy(p => p.UserAlias ?? p.UserUID).ToList();
            foreach (var pair in pairsInGroup)
            {
                ImGui.Indent(20);
                UiShared.DrawWithID(entry.GID + pair.UserUID, () => DrawSyncshellPairedClient(pair));
                ImGui.Unindent(20);
            }
        }
    }

    private void DrawSyncshellHeader(GroupDto entry, string name)
    {
        bool invitesEnabled = entry.InvitesEnabled ?? true;
        var lockedIcon = invitesEnabled ? FontAwesomeIcon.LockOpen : FontAwesomeIcon.Lock;

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text(FontAwesomeIcon.Crown.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        UiShared.TextWrapped("Syncshell owner: " + entry.OwnedBy ?? string.Empty);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowCircleLeft))
        {
            if (UiShared.CtrlPressed())
            {
                _ = _apiController.SendLeaveGroup(entry.GID);
            }
        }
        UiShared.AttachToolTip("Hold CTRL and click to leave this Syncshell");
        ImGui.SameLine();
        var pauseIcon = (entry.IsPaused ?? false) ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        if (ImGuiComponents.IconButton(pauseIcon))
        {
            _ = _apiController.SendPauseGroup(entry.GID, !entry.IsPaused ?? false);
        }
        UiShared.AttachToolTip(((entry.IsPaused ?? false) ? "Resume" : "Pause") + " pairing with all users in this Syncshell");
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
        {
            ImGui.SetClipboardText(entry.Alias ?? entry.GID);
        }
        UiShared.AttachToolTip("Copy Syncshell ID to Clipboard");
        if (entry.OwnedBy == _apiController.UID)
        {
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(lockedIcon))
            {
                _ = _apiController.SendGroupChangeInviteState(entry.GID, !entry.InvitesEnabled ?? true);
            }
            UiShared.AttachToolTip("Change Syncshell invite state, invites currently " + (invitesEnabled ? "open" : "closed"));
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Passport))
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

            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
            {
                if (UiShared.CtrlPressed())
                {
                    _ = _apiController.SendDeleteGroup(entry.GID);
                }
            }
            UiShared.AttachToolTip("Hold CTRL and click to delete this Syncshell. WARNING: this action is irreversible.");
        }
        else
        {
            ImGui.SameLine();
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text(lockedIcon.ToIconString());
            ImGui.PopFont();
            UiShared.AttachToolTip(invitesEnabled ? "Group is open for new joiners" : "Group is closed for new joiners");
        }
    }

    private void DrawSyncshellPairedClient(GroupPairDto entry)
    {
        var plusButtonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Plus);
        var entryUID = string.IsNullOrEmpty(entry.UserAlias) ? entry.UserUID : entry.UserAlias;
        var textSize = ImGui.CalcTextSize(entryUID);
        var originalY = ImGui.GetCursorPosY();
        var buttonSizes = plusButtonSize.Y;

        var textPos = originalY + plusButtonSize.Y / 2 - textSize.Y / 2;
        ImGui.SetCursorPosY(textPos);
        if (entry.IsPaused ?? false)
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

        var textIsUid = true;
        _showUidForEntry.TryGetValue(entry.UserUID, out var showUidInsteadOfName);
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
        if (_editNickEntry != entry.UserUID)
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
                if (_showUidForEntry.ContainsKey(entry.UserUID))
                {
                    prevState = _showUidForEntry[entry.UserUID];
                }

                _showUidForEntry[entry.UserUID] = !prevState;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _configuration.SetCurrentServerUidComment(_editNickEntry, _editCharComment);
                _configuration.Save();
                _editCharComment = _configuration.GetCurrentServerUidComments().ContainsKey(entry.UserUID)
                    ? _configuration.GetCurrentServerUidComments()[entry.UserUID]
                    : string.Empty;
                _editNickEntry = entry.UserUID;
            }
        }
        else
        {
            ImGui.SetCursorPosY(originalY);

            ImGui.SetNextItemWidth(UiShared.GetWindowContentRegionWidth() - ImGui.GetCursorPosX() - buttonSizes - ImGui.GetStyle().ItemSpacing.X * 2);
            if (ImGui.InputTextWithHint("", "Nick/Notes", ref _editCharComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _configuration.SetCurrentServerUidComment(entry.UserUID, _editCharComment);
                _configuration.Save();
                _editNickEntry = string.Empty;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _editNickEntry = string.Empty;
            }
            UiShared.AttachToolTip("Hit ENTER to save\nRight click to cancel");
        }

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - plusButtonSize.X);
        ImGui.SetCursorPosY(originalY);
        if (!_apiController.PairedClients.Any(p => p.OtherUID == entry.UserUID))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                _ = _apiController.SendPairedClientAddition(entry.UserUID);
            }
            UiShared.AttachToolTip("Pair with " + entryUID + " individually");
        }
    }

    private IEnumerable<ClientPairDto> GetFilteredUsers()
    {
        return _apiController.PairedClients.Where(p =>
        {
            if (_characterOrCommentFilter.IsNullOrEmpty()) return true;
            _configuration.GetCurrentServerUidComments().TryGetValue(p.OtherUID, out var comment);
            var uid = p.VanityUID.IsNullOrEmpty() ? p.OtherUID : p.VanityUID;
            return uid.ToLowerInvariant().Contains(_characterOrCommentFilter.ToLowerInvariant()) ||
                   (comment?.ToLowerInvariant().Contains(_characterOrCommentFilter.ToLowerInvariant()) ?? false);
        });
    }

    private void DrawServerStatus()
    {
        var buttonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Link);
        var userCount = _apiController.OnlineUsers.ToString();
        var userSize = ImGui.CalcTextSize(userCount);
        var textSize = ImGui.CalcTextSize("Users Online");

        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X) / 2 - (userSize.X + textSize.X) / 2);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiColors.ParsedGreen, userCount);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Users Online");
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiColors.DalamudRed, "Not connected to any server");
        }

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X);
        var color = UiShared.GetBoolColor(!_configuration.FullPause);
        var connectedIcon = !_configuration.FullPause ? FontAwesomeIcon.Link : FontAwesomeIcon.Unlink;

        ImGui.PushStyleColor(ImGuiCol.Text, color);
        if (ImGuiComponents.IconButton(connectedIcon))
        {
            _configuration.FullPause = !_configuration.FullPause;
            _configuration.Save();
            _ = _apiController.CreateConnections();
        }
        ImGui.PopStyleColor();
        UiShared.AttachToolTip(!_configuration.FullPause ? "Disconnect from " + _apiController.ServerDictionary[_configuration.ApiUri] : "Connect to " + _apiController.ServerDictionary[_configuration.ApiUri]);
    }

    private void DrawTransfers()
    {
        var currentUploads = _apiController.CurrentUploads.ToList();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text(FontAwesomeIcon.Upload.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

        if (currentUploads.Any())
        {
            var totalUploads = currentUploads.Count;

            var doneUploads = currentUploads.Count(c => c.IsTransferred);
            var totalUploaded = currentUploads.Sum(c => c.Transferred);
            var totalToUpload = currentUploads.Sum(c => c.Total);

            ImGui.Text($"{doneUploads}/{totalUploads}");
            var uploadText = $"({UiShared.ByteToString(totalUploaded)}/{UiShared.ByteToString(totalToUpload)})";
            var textSize = ImGui.CalcTextSize(uploadText);
            ImGui.SameLine(_windowContentWidth - textSize.X);
            ImGui.Text(uploadText);
        }
        else
        {
            ImGui.Text("No uploads in progress");
        }

        var currentDownloads = _apiController.CurrentDownloads.SelectMany(k => k.Value).ToList();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text(FontAwesomeIcon.Download.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

        if (currentDownloads.Any())
        {
            var totalDownloads = currentDownloads.Count();
            var doneDownloads = currentDownloads.Count(c => c.IsTransferred);
            var totalDownloaded = currentDownloads.Sum(c => c.Transferred);
            var totalToDownload = currentDownloads.Sum(c => c.Total);

            ImGui.Text($"{doneDownloads}/{totalDownloads}");
            var downloadText =
                $"({UiShared.ByteToString(totalDownloaded)}/{UiShared.ByteToString(totalToDownload)})";
            var textSize = ImGui.CalcTextSize(downloadText);
            ImGui.SameLine(_windowContentWidth - textSize.X);
            ImGui.Text(downloadText);
        }
        else
        {
            ImGui.Text("No downloads in progress");
        }

        ImGui.SameLine();
    }

    private void DrawUIDHeader()
    {
        var uidText = GetUidText();
        var buttonSizeX = 0f;

        if (_uiShared.UidFontBuilt) ImGui.PushFont(_uiShared.UidFont);
        var uidTextSize = ImGui.CalcTextSize(uidText);
        if (_uiShared.UidFontBuilt) ImGui.PopFont();

        var originalPos = ImGui.GetCursorPos();
        ImGui.SetWindowFontScale(1.5f);
        var buttonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Cog);
        buttonSizeX -= buttonSize.X - ImGui.GetStyle().ItemSpacing.X * 2;
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X);
        ImGui.SetCursorPosY(originalPos.Y + uidTextSize.Y / 2 - buttonSize.Y / 2);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
        {
            OpenSettingsUi?.Invoke();
        }
        UiShared.AttachToolTip("Open the Mare Synchronos Settings");

        ImGui.SameLine(); //Important to draw the uidText consistently
        ImGui.SetCursorPos(originalPos);

        if (_apiController.ServerState is ServerState.Connected)
        {
            buttonSizeX += UiShared.GetIconButtonSize(FontAwesomeIcon.Copy).X - ImGui.GetStyle().ItemSpacing.X * 2;
            ImGui.SetCursorPosY(originalPos.Y + uidTextSize.Y / 2 - buttonSize.Y / 2);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
            {
                ImGui.SetClipboardText(_apiController.UID);
            }
            UiShared.AttachToolTip("Copy your UID to clipboard");
            ImGui.SameLine();
        }
        ImGui.SetWindowFontScale(1f);

        ImGui.SetCursorPosY(originalPos.Y + buttonSize.Y / 2 - uidTextSize.Y / 2 - ImGui.GetStyle().ItemSpacing.Y / 2);
        ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 + buttonSizeX - uidTextSize.X / 2);
        if (_uiShared.UidFontBuilt) ImGui.PushFont(_uiShared.UidFont);
        ImGui.TextColored(GetUidColor(), uidText);
        if (_uiShared.UidFontBuilt) ImGui.PopFont();

        if (_apiController.ServerState is not ServerState.Connected)
        {
            UiShared.ColorTextWrapped(GetServerError(), GetUidColor());
        }
    }


    private string GetServerError()
    {
        return _apiController.ServerState switch
        {
            ServerState.Disconnected => "You are currently disconnected from the Mare Synchronos server.",
            ServerState.Unauthorized => "Your account is not present on the server anymore or you are banned.",
            ServerState.Offline => "Your selected Mare Synchronos server is currently offline.",
            ServerState.VersionMisMatch =>
                "Your plugin or the server you are connecting to is out of date. Please update your plugin now. If you already did so, contact the server provider to update their server to the latest version.",
            ServerState.RateLimited => "You are rate limited for (re)connecting too often. Wait and try again later.",
            ServerState.Connected => string.Empty,
            _ => string.Empty
        };
    }

    private Vector4 GetUidColor()
    {
        return _apiController.ServerState switch
        {
            ServerState.Connected => ImGuiColors.ParsedGreen,
            ServerState.Disconnected => ImGuiColors.DalamudYellow,
            ServerState.Unauthorized => ImGuiColors.DalamudRed,
            ServerState.VersionMisMatch => ImGuiColors.DalamudRed,
            ServerState.Offline => ImGuiColors.DalamudRed,
            ServerState.RateLimited => ImGuiColors.DalamudYellow,
            _ => ImGuiColors.DalamudRed
        };
    }

    private string GetUidText()
    {
        return _apiController.ServerState switch
        {
            ServerState.Disconnected => "Disconnected",
            ServerState.Unauthorized => "Unauthorized",
            ServerState.VersionMisMatch => "Version mismatch",
            ServerState.Offline => "Unavailable",
            ServerState.RateLimited => "Rate Limited",
            ServerState.Connected => _apiController.UID,
            _ => string.Empty
        };
    }
}
