using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Delegates;
using MareSynchronos.Managers;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Models;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Handlers;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI;

public class CompactUi : Window, IDisposable
{
    private readonly ApiController _apiController;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly ConfigurationService _configService;
    private readonly TagHandler _tagHandler;
    public readonly Dictionary<string, bool> ShowUidForEntry = new(StringComparer.Ordinal);
    private readonly UiShared _uiShared;
    private readonly WindowSystem _windowSystem;
    private string _characterOrCommentFilter = string.Empty;

    public string EditUserComment = string.Empty;
    public string EditNickEntry = string.Empty;

    private string _pairToAdd = string.Empty;

    private readonly Stopwatch _timeout = new();
    private bool _buttonState;

    public float TransferPartHeight;
    public float WindowContentWidth;
    private bool _showModalForUserAddition;
    private bool _wasOpen;

    private bool _showSyncShells;
    private readonly GroupPanel _groupPanel;
    private UserPairDto? _lastAddedUser;
    private string _lastAddedUserComment = string.Empty;

    private readonly SelectGroupForPairUi _selectGroupForPairUi;
    private readonly SelectPairForGroupUi _selectPairsForGroupUi;
    private readonly PairGroupsUi _pairGroupsUi;

    public CompactUi(WindowSystem windowSystem,
        UiShared uiShared, ConfigurationService configService, ApiController apiController, PairManager pairManager,
        ServerConfigurationManager serverManager) : base("###MareSynchronosMainUI")
    {

#if DEBUG
        string dateTime = "DEV VERSION";
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        this.WindowName = "Mare Synchronos " + ver.Major + "." + ver.Minor + "." + ver.Build + "###MareSynchronosMainUI";
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
        this.WindowName = $"Mare Synchronos {dateTime} ({ver.Major}.{ver.Minor}.{ver.Build})###MareSynchronosMainUI";
        Toggle();
#else
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        this.WindowName = "Mare Synchronos " + ver.Major + "." + ver.Minor + "." + ver.Build + "###MareSynchronosMainUI";
#endif
        Logger.Verbose("Creating " + nameof(CompactUi));

        _windowSystem = windowSystem;
        _uiShared = uiShared;
        _configService = configService;
        _apiController = apiController;
        _pairManager = pairManager;
        _serverManager = serverManager;
        _tagHandler = new(_serverManager);

        _groupPanel = new(this, uiShared, _pairManager, _serverManager);
        _selectGroupForPairUi = new(_tagHandler);
        _selectPairsForGroupUi = new(_tagHandler);
        _pairGroupsUi = new(_tagHandler, DrawPairedClient, apiController, _selectPairsForGroupUi);

        _uiShared.GposeStart += UiShared_GposeStart;
        _uiShared.GposeEnd += UiShared_GposeEnd;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(350, 400),
            MaximumSize = new Vector2(350, 2000),
        };

        windowSystem.AddWindow(this);
    }

    private void UiShared_GposeEnd()
    {
        IsOpen = _wasOpen;
    }

    private void UiShared_GposeStart()
    {
        _wasOpen = IsOpen;
        IsOpen = false;
    }

    public event VoidDelegate? OpenSettingsUi;
    public void Dispose()
    {
        Logger.Verbose("Disposing " + nameof(CompactUi));
        _uiShared.GposeStart -= UiShared_GposeStart;
        _uiShared.GposeEnd -= UiShared_GposeEnd;
        _windowSystem.RemoveWindow(this);
    }

    public override void Draw()
    {
        WindowContentWidth = UiShared.GetWindowContentRegionWidth();
        UiShared.DrawWithID("header", DrawUIDHeader);
        ImGui.Separator();
        UiShared.DrawWithID("serverstatus", DrawServerStatus);

        if (_apiController.ServerState is ServerState.Connected)
        {
            var hasShownSyncShells = _showSyncShells;

            ImGui.PushFont(UiBuilder.IconFont);
            if (!hasShownSyncShells)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered]);
            }
            if (ImGui.Button(FontAwesomeIcon.User.ToIconString(), new Vector2((UiShared.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30 * ImGuiHelpers.GlobalScale)))
            {
                _showSyncShells = false;
            }
            if (!hasShownSyncShells)
            {
                ImGui.PopStyleColor();
            }
            ImGui.PopFont();
            UiShared.AttachToolTip("Individual pairs");

            ImGui.SameLine();

            ImGui.PushFont(UiBuilder.IconFont);
            if (hasShownSyncShells)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered]);
            }
            if (ImGui.Button(FontAwesomeIcon.UserFriends.ToIconString(), new Vector2((UiShared.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30 * ImGuiHelpers.GlobalScale)))
            {
                _showSyncShells = true;
            }
            if (hasShownSyncShells)
            {
                ImGui.PopStyleColor();
            }
            ImGui.PopFont();

            UiShared.AttachToolTip("Syncshells");

            ImGui.Separator();
            if (!hasShownSyncShells)
            {
                UiShared.DrawWithID("pairlist", DrawPairList);
            }
            else
            {
                UiShared.DrawWithID("syncshells", _groupPanel.DrawSyncshells);

            }
            ImGui.Separator();
            UiShared.DrawWithID("transfers", DrawTransfers);
            TransferPartHeight = ImGui.GetCursorPosY() - TransferPartHeight;
            UiShared.DrawWithID("group-user-popup", () => _selectPairsForGroupUi.Draw(_pairManager.DirectPairs, ShowUidForEntry));
            UiShared.DrawWithID("grouping-popup", () => _selectGroupForPairUi.Draw(ShowUidForEntry));
        }

        if (_configService.Current.OpenPopupOnAdd && _apiController.LastAddedUser != null)
        {
            _lastAddedUser = _apiController.LastAddedUser;
            _apiController.LastAddedUser = null;
            ImGui.OpenPopup("Set Notes for New User");
            _showModalForUserAddition = true;
            _lastAddedUserComment = string.Empty;
        }

        if (ImGui.BeginPopupModal("Set Notes for New User", ref _showModalForUserAddition, UiShared.PopupWindowFlags))
        {
            if (_lastAddedUser == null)
            {
                _showModalForUserAddition = false;
            }
            else
            {
                UiShared.TextWrapped($"You have successfully added {_lastAddedUser.User.AliasOrUID}. Set a local note for the user in the field below:");
                ImGui.InputTextWithHint("##noteforuser", $"Note for {_lastAddedUser.User.AliasOrUID}", ref _lastAddedUserComment, 100);
                if (UiShared.IconTextButton(FontAwesomeIcon.Save, "Save Note"))
                {
                    _serverManager.CurrentServer!.UidServerComments[_lastAddedUser.User.UID] = _lastAddedUserComment;
                    _serverManager.Save();
                    _lastAddedUser = null;
                    _lastAddedUserComment = string.Empty;
                    _showModalForUserAddition = false;
                }
            }
            UiShared.SetScaledWindowSize(275);
            ImGui.EndPopup();
        }
    }

    public override void OnClose()
    {
        EditNickEntry = string.Empty;
        EditUserComment = string.Empty;
        base.OnClose();
    }

    private void DrawAddPair()
    {
        var buttonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Plus);
        ImGui.SetNextItemWidth(UiShared.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonSize.X);
        ImGui.InputTextWithHint("##otheruid", "Other players UID/Alias", ref _pairToAdd, 20);
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X);
        var canAdd = !_pairManager.DirectPairs.Any(p => string.Equals(p.UserData.UID, _pairToAdd, StringComparison.Ordinal) || string.Equals(p.UserData.Alias, _pairToAdd, StringComparison.Ordinal));
        if (!canAdd)
        {
            ImGuiComponents.DisabledButton(FontAwesomeIcon.Plus);
        }
        else
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                _ = _apiController.UserAddPair(new(new(_pairToAdd)));
                _pairToAdd = string.Empty;
            }
            UiShared.AttachToolTip("Pair with " + (_pairToAdd.IsNullOrEmpty() ? "other user" : _pairToAdd));
        }

        ImGuiHelpers.ScaledDummy(2);
    }

    private void DrawFilter()
    {
        var buttonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.ArrowUp);
        var playButtonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Play);
        if (!_configService.Current.ReverseUserSort)
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowDown))
            {
                _configService.Current.ReverseUserSort = true;
                _configService.Save();
            }
            UiShared.AttachToolTip("Sort by name descending");
        }
        else
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowUp))
            {
                _configService.Current.ReverseUserSort = false;
                _configService.Save();
            }
            UiShared.AttachToolTip("Sort by name ascending");
        }
        ImGui.SameLine();

        var users = GetFilteredUsers();
        var userCount = users.Count;

        var spacing = userCount > 0
            ? playButtonSize.X + ImGui.GetStyle().ItemSpacing.X * 2
            : ImGui.GetStyle().ItemSpacing.X;

        ImGui.SetNextItemWidth(WindowContentWidth - buttonSize.X - spacing);
        ImGui.InputTextWithHint("##filter", "Filter for UID/notes", ref _characterOrCommentFilter, 255);

        if (userCount == 0) return;

        var pausedUsers = users.Where(u => u.UserPair!.OwnPermissions.IsPaused() && u.UserPair.OtherPermissions.IsPaired()).ToList();
        var resumedUsers = users.Where(u => !u.UserPair!.OwnPermissions.IsPaused() && u.UserPair.OtherPermissions.IsPaired()).ToList();

        if (!pausedUsers.Any() && !resumedUsers.Any()) return;
        ImGui.SameLine();

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
                    foreach (var entry in users)
                    {
                        var perm = entry.UserPair!.OwnPermissions;
                        perm.SetPaused(!perm.IsPaused());
                        _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(entry.UserData, perm));
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

    private void DrawPairedClient(Pair entry)
    {
        if (entry.UserPair == null) return;

        var pauseIcon = entry.UserPair!.OwnPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseIconSize = UiShared.GetIconButtonSize(pauseIcon);
        var barButtonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Bars);
        var entryUID = entry.UserData.AliasOrUID;
        var textSize = ImGui.CalcTextSize(entryUID);
        var originalY = ImGui.GetCursorPosY();
        var buttonSizes = pauseIconSize.Y + barButtonSize.Y;
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth();

        var textPos = originalY + pauseIconSize.Y / 2 - textSize.Y / 2;
        ImGui.SetCursorPosY(textPos);
        FontAwesomeIcon presenceIcon;
        FontAwesomeIcon connectionIcon;
        string connectionText = string.Empty;
        string presenceText = string.Empty;
        Vector4 presenceColor;
        Vector4 connectionColor;
        if (!(entry.UserPair!.OwnPermissions.IsPaired() && entry.UserPair!.OtherPermissions.IsPaired()))
        {
            connectionIcon = FontAwesomeIcon.ArrowUp;
            connectionText = entryUID + " has not added you back";
            connectionColor = ImGuiColors.DalamudRed;
            presenceIcon = FontAwesomeIcon.Question;
            presenceColor = ImGuiColors.DalamudGrey;
            presenceText = entryUID + " online status is unknown (not paired)";
        }
        else if (entry.UserPair!.OwnPermissions.IsPaused() || entry.UserPair!.OtherPermissions.IsPaused())
        {
            connectionIcon = FontAwesomeIcon.PauseCircle;
            connectionText = "Pairing status with " + entryUID + " is paused";
            connectionColor = ImGuiColors.DalamudYellow;
            presenceIcon = FontAwesomeIcon.Question;
            presenceColor = ImGuiColors.DalamudGrey;
            presenceText = entryUID + " online status is unknown (paused)";
        }
        else
        {
            connectionIcon = FontAwesomeIcon.Check;
            connectionText = "You are paired with " + entryUID;
            connectionColor = ImGuiColors.ParsedGreen;
            presenceIcon = entry.IsVisible ? FontAwesomeIcon.Eye : (entry.IsOnline ? FontAwesomeIcon.Link : FontAwesomeIcon.Unlink);
            presenceColor = (entry.IsOnline || entry.IsVisible) ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
            presenceText = entryUID + " is offline";
            if (entry.IsOnline && !entry.IsVisible) presenceText = entryUID + " is online";
            else if (entry.IsOnline && entry.IsVisible) presenceText = entryUID + " is visible: " + entry.PlayerName;
        }

        ImGui.PushFont(UiBuilder.IconFont);
        UiShared.ColorText(connectionIcon.ToIconString(), connectionColor);
        ImGui.PopFont();
        UiShared.AttachToolTip(connectionText);
        ImGui.SameLine();
        ImGui.SetCursorPosY(textPos);
        ImGui.PushFont(UiBuilder.IconFont);
        UiShared.ColorText(presenceIcon.ToIconString(), presenceColor);
        ImGui.PopFont();
        UiShared.AttachToolTip(presenceText);

        var textIsUid = true;
        ShowUidForEntry.TryGetValue(entry.UserPair!.User.UID, out var showUidInsteadOfName);
        if (!showUidInsteadOfName && _serverManager.CurrentServer!.UidServerComments.TryGetValue(entry.UserPair!.User.UID, out var playerText))
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
        if (!string.Equals(EditNickEntry, entry.UserData.UID, StringComparison.Ordinal))
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
                if (ShowUidForEntry.ContainsKey(entry.UserPair!.User.UID))
                {
                    prevState = ShowUidForEntry[entry.UserPair!.User.UID];
                }

                ShowUidForEntry[entry.UserPair!.User.UID] = !prevState;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                var pair = _pairManager.DirectPairs.Find(p => p.UserData.UID == EditNickEntry);
                if (pair != null)
                {
                    pair.SetNote(EditUserComment);
                    _configService.Save();
                }
                EditUserComment = entry.GetNote() ?? string.Empty;
                EditNickEntry = entry.UserPair!.User.UID;
            }
        }
        else
        {
            ImGui.SetCursorPosY(originalY);

            ImGui.SetNextItemWidth(UiShared.GetWindowContentRegionWidth() - ImGui.GetCursorPosX() - buttonSizes - ImGui.GetStyle().ItemSpacing.X * 2);
            if (ImGui.InputTextWithHint("", "Nick/Notes", ref EditUserComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _serverManager.CurrentServer!.UidServerComments[entry.UserPair!.User.UID] = EditUserComment;
                _serverManager.Save();
                EditNickEntry = string.Empty;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                EditNickEntry = string.Empty;
            }
            UiShared.AttachToolTip("Hit ENTER to save\nRight click to cancel");
        }

        // Pause Button
        if (entry.UserPair!.OwnPermissions.IsPaired() && entry.UserPair!.OtherPermissions.IsPaired())
        {
            ImGui.SameLine(windowEndX - barButtonSize.X - spacingX - pauseIconSize.X);
            ImGui.SetCursorPosY(originalY);
            if (ImGuiComponents.IconButton(pauseIcon))
            {
                var perm = entry.UserPair!.OwnPermissions;
                perm.SetPaused(!perm.IsPaused());
                _ = _apiController.UserSetPairPermissions(new(entry.UserData, perm));
            }
            UiShared.AttachToolTip(!entry.UserPair!.OwnPermissions.IsPaused()
                ? "Pause pairing with " + entryUID
                : "Resume pairing with " + entryUID);
        }

        // Flyout Menu
        ImGui.SameLine(windowEndX - barButtonSize.X);
        ImGui.SetCursorPosY(originalY);

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup("User Flyout Menu");
        }
        if (ImGui.BeginPopup("User Flyout Menu"))
        {
            UiShared.DrawWithID($"buttons-{entry.UserPair!.User.UID}", () => DrawPairedClientMenu(entry));
            ImGui.EndPopup();
        }
    }

    private void DrawPairedClientMenu(Pair entry)
    {
        var entryUID = entry.UserData.AliasOrUID;
        if (UiShared.IconTextButton(FontAwesomeIcon.Folder, "Pair Groups"))
        {
            _selectGroupForPairUi.Open(entry);
        }
        UiShared.AttachToolTip("Choose pair groups for " + entryUID);

        if (UiShared.IconTextButton(FontAwesomeIcon.Trash, "Unpair Permanently"))
        {
            if (UiShared.CtrlPressed())
            {
                _ = _apiController.UserRemovePair(new(entry.UserData));
            }
        }
        UiShared.AttachToolTip("Hold CTRL and click to unpair permanently from " + entryUID);
    }

    private void DrawPairList()
    {
        UiShared.DrawWithID("addpair", DrawAddPair);
        UiShared.DrawWithID("pairs", DrawPairs);
        TransferPartHeight = ImGui.GetCursorPosY();
        UiShared.DrawWithID("filter", DrawFilter);
    }

    private void DrawPairs()
    {
        var ySize = TransferPartHeight == 0
            ? 1
            : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y) - TransferPartHeight - ImGui.GetCursorPosY();
        var users = GetFilteredUsers();

        ImGui.BeginChild("list", new Vector2(WindowContentWidth, ySize), border: false);
        var visibleUsers = users.Where(u => u.IsVisible && u.UserPair!.OtherPermissions.IsPaired()).OrderBy(u => u.GetNote() ?? u.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase).ToList();
        var onlineUsers = users.Where(u => u.IsOnline && !u.IsVisible && u.UserPair!.OtherPermissions.IsPaired()).OrderBy(u => u.GetNote() ?? u.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase).ToList();
        var offlineUsers = users.Where(u => !u.IsOnline && !u.IsVisible || !u.UserPair!.OtherPermissions.IsPaired()).OrderBy(u => u.GetNote() ?? u.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase).ToList();

        if (_configService.Current.ReverseUserSort)
        {
            visibleUsers.Reverse();
            onlineUsers.Reverse();
            offlineUsers.Reverse();
        }

        _pairGroupsUi.Draw(visibleUsers, onlineUsers, offlineUsers);

        visibleUsers = visibleUsers.Where(pair => !_tagHandler.HasAnyTag(pair.UserPair!)).ToList();
        onlineUsers = onlineUsers.Where(pair => !_tagHandler.HasAnyTag(pair.UserPair!)).ToList();
        offlineUsers = offlineUsers.Where(pair => !_tagHandler.HasAnyTag(pair.UserPair!)).ToList();

        if (visibleUsers.Any())
        {
            ImGui.Text("Visible");
            ImGui.Separator();
            foreach (var entry in visibleUsers)
            {
                UiShared.DrawWithID(entry.UserData.UID, () => DrawPairedClient(entry));
            }
        }

        if (onlineUsers.Any())
        {
            ImGui.Text("Online");
            ImGui.Separator();
            foreach (var entry in onlineUsers)
            {
                UiShared.DrawWithID(entry.UserData.UID, () => DrawPairedClient(entry));
            }
        }

        if (offlineUsers.Any())
        {
            ImGui.Text("Offline/Unknown");
            ImGui.Separator();
            foreach (var entry in offlineUsers)
            {
                UiShared.DrawWithID(entry.UserData.UID, () => DrawPairedClient(entry));
            }
        }

        ImGui.EndChild();
    }

    private List<Pair> GetFilteredUsers()
    {
        return _pairManager.DirectPairs.Where(p =>
        {
            if (_characterOrCommentFilter.IsNullOrEmpty()) return true;
            return p.UserData.AliasOrUID.Contains(_characterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ||
                   (p.GetNote()?.Contains(_characterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ?? false);
        }).ToList();
    }

    private void DrawServerStatus()
    {
        var buttonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Link);
        var userCount = _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture);
        var userSize = ImGui.CalcTextSize(userCount);
        var textSize = ImGui.CalcTextSize("Users Online");
#if DEBUG
        string shardConnection = $"Shard: {_apiController.ServerInfo.ShardName}";
#else
        string shardConnection = string.Equals(_apiController.ServerInfo.ShardName, "Main", StringComparison.OrdinalIgnoreCase) ? string.Empty : $"Shard: {_apiController.ServerInfo.ShardName}";
#endif
        var shardTextSize = ImGui.CalcTextSize(shardConnection);
        var printShard = !string.IsNullOrEmpty(_apiController.ServerInfo.ShardName) && shardConnection != string.Empty;

        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X) / 2 - (userSize.X + textSize.X) / 2 - ImGui.GetStyle().ItemSpacing.X / 2);
            if (!printShard) ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiColors.ParsedGreen, userCount);
            ImGui.SameLine();
            if (!printShard) ImGui.AlignTextToFramePadding();
            ImGui.Text("Users Online");
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiColors.DalamudRed, "Not connected to any server");
        }

        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X) / 2 - shardTextSize.X / 2);
            ImGui.TextUnformatted(shardConnection);
        }

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X);
        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ((userSize.Y + textSize.Y) / 2 + shardTextSize.Y) / 2 - ImGui.GetStyle().ItemSpacing.Y + buttonSize.Y / 2);
        }
        var color = UiShared.GetBoolColor(!_serverManager.CurrentServer!.FullPause);
        var connectedIcon = !_serverManager.CurrentServer.FullPause ? FontAwesomeIcon.Link : FontAwesomeIcon.Unlink;

        ImGui.PushStyleColor(ImGuiCol.Text, color);
        if (ImGuiComponents.IconButton(connectedIcon))
        {
            _serverManager.CurrentServer.FullPause = !_serverManager.CurrentServer.FullPause;
            _serverManager.Save();
            _ = _apiController.CreateConnections();
        }
        ImGui.PopStyleColor();
        UiShared.AttachToolTip(!_serverManager.CurrentServer.FullPause ? "Disconnect from " + _serverManager.CurrentServer.ServerName : "Connect to " + _serverManager.CurrentServer.ServerName);
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
            ImGui.SameLine(WindowContentWidth - textSize.X);
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
            ImGui.SameLine(WindowContentWidth - textSize.X);
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
            ServerState.Connecting => "Attempting to connect to the server.",
            ServerState.Reconnecting => "Connection to server interrupted, attempting to reconnect to the server.",
            ServerState.Disconnected => "You are currently disconnected from the Mare Synchronos server.",
            ServerState.Unauthorized => "Server Response: " + _apiController.AuthFailureMessage,
            ServerState.Offline => "Your selected Mare Synchronos server is currently offline.",
            ServerState.VersionMisMatch =>
                "Your plugin or the server you are connecting to is out of date. Please update your plugin now. If you already did so, contact the server provider to update their server to the latest version.",
            ServerState.RateLimited => "You are rate limited for (re)connecting too often. Disconnect, wait 10 minutes and try again.",
            ServerState.Connected => string.Empty,
            ServerState.NoSecretKey => "You have no secret key set for this current character. Open the settings and set a secret key. You can reuse one secret key for different characters.",
            _ => string.Empty
        };
    }

    private Vector4 GetUidColor()
    {
        return _apiController.ServerState switch
        {
            ServerState.Connecting => ImGuiColors.DalamudYellow,
            ServerState.Reconnecting => ImGuiColors.DalamudRed,
            ServerState.Connected => ImGuiColors.ParsedGreen,
            ServerState.Disconnected => ImGuiColors.DalamudYellow,
            ServerState.Unauthorized => ImGuiColors.DalamudRed,
            ServerState.VersionMisMatch => ImGuiColors.DalamudRed,
            ServerState.Offline => ImGuiColors.DalamudRed,
            ServerState.RateLimited => ImGuiColors.DalamudYellow,
            ServerState.NoSecretKey => ImGuiColors.DalamudYellow,
            _ => ImGuiColors.DalamudRed
        };
    }

    private string GetUidText()
    {
        return _apiController.ServerState switch
        {
            ServerState.Reconnecting => "Reconnecting",
            ServerState.Connecting => "Connecting",
            ServerState.Disconnected => "Disconnected",
            ServerState.Unauthorized => "Unauthorized",
            ServerState.VersionMisMatch => "Version mismatch",
            ServerState.Offline => "Unavailable",
            ServerState.RateLimited => "Rate Limited",
            ServerState.NoSecretKey => "No Secret Key",
            ServerState.Connected => _apiController.DisplayName,
            _ => string.Empty
        };
    }
}
