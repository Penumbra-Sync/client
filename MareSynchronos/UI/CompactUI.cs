using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
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
    public readonly Dictionary<string, bool> ShowUidForEntry = new(StringComparer.Ordinal);
    private readonly UiShared _uiShared;
    private readonly WindowSystem _windowSystem;
    private string _characterOrCommentFilter = string.Empty;

    public string EditUserComment = string.Empty;
    public string EditNickEntry = string.Empty;

    private string _pairToAdd = string.Empty;

    private readonly Stopwatch _timeout = new();
    private bool _buttonState;

    public float TransferPartHeight = 0;
    public float _windowContentWidth = 0;


    private bool showSyncShells = false;
    private GroupPanel groupPanel;

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
        this.WindowName = "Mare Synchronos " + Assembly.GetExecutingAssembly().GetName().Version + "###MareSynchronosMainUI";
#endif
        Logger.Verbose("Creating " + nameof(CompactUi));

        _windowSystem = windowSystem;
        _uiShared = uiShared;
        _configuration = configuration;
        _apiController = apiController;

        groupPanel = new(this, uiShared, configuration, apiController);

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(350, 400),
            MaximumSize = new Vector2(350, 2000),
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
            var hasShownSyncShells = showSyncShells;

            ImGui.PushFont(UiBuilder.IconFont);
            if (!hasShownSyncShells)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered]);
            }
            if (ImGui.Button(FontAwesomeIcon.User.ToIconString(), new Vector2((UiShared.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30)))
            {
                showSyncShells = false;
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
            if (ImGui.Button(FontAwesomeIcon.UserFriends.ToIconString(), new Vector2((UiShared.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30)))
            {
                showSyncShells = true;
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
                UiShared.DrawWithID("syncshells", groupPanel.DrawSyncshells);

            }
            ImGui.Separator();
            UiShared.DrawWithID("transfers", DrawTransfers);
            TransferPartHeight = ImGui.GetCursorPosY() - TransferPartHeight;
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
        ImGui.InputTextWithHint("##otheruid", "Other players UID/GID", ref _pairToAdd, 10);
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
        {
            if (_apiController.PairedClients.All(w => !string.Equals(w.OtherUID, _pairToAdd, StringComparison.Ordinal)))
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
            UiShared.AttachToolTip("Sort by name descending");
        }
        else
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowUp))
            {
                _configuration.ReverseUserSort = false;
                _configuration.Save();
            }
            UiShared.AttachToolTip("Sort by name ascending");
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
        ShowUidForEntry.TryGetValue(entry.OtherUID, out var showUidInsteadOfName);
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
        if (!string.Equals(EditNickEntry, entry.OtherUID, StringComparison.Ordinal))
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
                if (ShowUidForEntry.ContainsKey(entry.OtherUID))
                {
                    prevState = ShowUidForEntry[entry.OtherUID];
                }

                ShowUidForEntry[entry.OtherUID] = !prevState;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _configuration.SetCurrentServerUidComment(EditNickEntry, EditUserComment);
                _configuration.Save();
                EditUserComment = _configuration.GetCurrentServerUidComments().ContainsKey(entry.OtherUID)
                    ? _configuration.GetCurrentServerUidComments()[entry.OtherUID]
                    : string.Empty;
                EditNickEntry = entry.OtherUID;
            }
        }
        else
        {
            ImGui.SetCursorPosY(originalY);

            ImGui.SetNextItemWidth(UiShared.GetWindowContentRegionWidth() - ImGui.GetCursorPosX() - buttonSizes - ImGui.GetStyle().ItemSpacing.X * 2);
            if (ImGui.InputTextWithHint("", "Nick/Notes", ref EditUserComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _configuration.SetCurrentServerUidComment(entry.OtherUID, EditUserComment);
                _configuration.Save();
                EditNickEntry = string.Empty;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                EditNickEntry = string.Empty;
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

        users = users.OrderBy(u => _configuration.GetCurrentServerUidComments().ContainsKey(u.OtherUID) ? _configuration.GetCurrentServerUidComments()[u.OtherUID] : !string.IsNullOrEmpty(u.VanityUID) ? u.VanityUID : u.OtherUID);
        if (_configuration.ReverseUserSort) users = users.Reverse();

        ImGui.BeginChild("list", new Vector2(_windowContentWidth, ySize), false);
        foreach (var entry in users.ToList())
        {
            UiShared.DrawWithID(entry.OtherUID, () => DrawPairedClient(entry));
        }
        ImGui.EndChild();
    }



    private IEnumerable<ClientPairDto> GetFilteredUsers()
    {
        return _apiController.PairedClients.Where(p =>
        {
            if (_characterOrCommentFilter.IsNullOrEmpty()) return true;
            _configuration.GetCurrentServerUidComments().TryGetValue(p.OtherUID, out var comment);
            var uid = p.VanityUID.IsNullOrEmpty() ? p.OtherUID : p.VanityUID;
            return uid.Contains(_characterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ||
                   (comment?.Contains(_characterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ?? false);
        });
    }

    private void DrawServerStatus()
    {
        var buttonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Link);
        var userCount = _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture);
        var userSize = ImGui.CalcTextSize(userCount);
        var textSize = ImGui.CalcTextSize("Users Online");
#if DEBUG
        string shardConnection = $"Connected shard: {_apiController.ServerInfo.ShardName}";
#else
        string shardConnection = string.Equals(_apiController.ServerInfo.ShardName, "Main", StringComparison.OrdinalIgnoreCase) ? string.Empty : $"Connected shard: {_apiController.ServerInfo.ShardName}";
#endif
        var shardTextSize = ImGui.CalcTextSize(shardConnection);

        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X) / 2 - (userSize.X + textSize.X) / 2);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiColors.ParsedGreen, userCount);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Users Online");
            ImGui.AlignTextToFramePadding();
            if (!string.IsNullOrEmpty(shardConnection))
            {
                ImGui.TextUnformatted(shardConnection);
            }
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
