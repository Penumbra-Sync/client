using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.VM;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class CompactUi : WindowVMBase<ImguiVM>
{
    private readonly CompactVM _compactVM;
    private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new();
    private readonly FileUploadManager _fileTransferManager;
    private readonly GroupPanel _groupPanel;
    private readonly PairGroupsUi _pairGroupsUi;
    private readonly SelectGroupForPairUi _selectGroupForPairUi;
    private readonly SelectPairForGroupUi _selectPairsForGroupUi;
    private readonly Stopwatch _timeout = new();
    private readonly UiSharedService _uiShared;
    private bool _buttonState;
    private Vector2 _lastPosition = Vector2.One;
    private Vector2 _lastSize = Vector2.One;
    private bool _showModalForUserAddition;
    private bool _showSyncShells;
    private float _transferPartHeight;
    private bool _wasOpen;
    private float _windowContentWidth;

    public CompactUi(CompactVM compactVM, ILogger<CompactUi> logger, MareMediator mediator, UiSharedService uiShared,
        GroupPanel groupPanel, PairGroupsUi pairGroupsUi, SelectGroupForPairUi selectGroupForPairUi,
        SelectPairForGroupUi selectPairForGroupUi, FileUploadManager fileTransferManager) : base(compactVM, logger, mediator, "###MareSynchronosMainUI")
    {
        _uiShared = uiShared;
        _fileTransferManager = fileTransferManager;
        _compactVM = VM.As<CompactVM>();

        _groupPanel = groupPanel;
        _selectGroupForPairUi = selectGroupForPairUi;
        _selectPairsForGroupUi = selectPairForGroupUi;
        _pairGroupsUi = pairGroupsUi;

#if DEBUG
        string dev = "Dev Build";
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        WindowName = $"Mare Synchronos {dev} ({ver.Major}.{ver.Minor}.{ver.Build})###MareSynchronosMainUI";
        Toggle();
#else
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        WindowName = "Mare Synchronos " + ver.Major + "." + ver.Minor + "." + ver.Build + "###MareSynchronosMainUI";
#endif
        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = true);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<CutsceneStartMessage>(this, (_) => UiSharedService_GposeStart());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => UiSharedService_GposeEnd());
        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) => _currentDownloads[msg.DownloadId] = msg.DownloadStatus);
        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => _currentDownloads.TryRemove(msg.DownloadId, out _));

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(350, 400),
            MaximumSize = new Vector2(350, 2000),
        };
    }

    private float TransferPartHeight
    {
        get => _transferPartHeight;
        set
        {
            if (value != _transferPartHeight)
            {
                _transferPartHeight = value;
                Mediator.Publish(new CompactUiContentChangeMessage(_transferPartHeight, _windowContentWidth));
            }
        }
    }

    private float WindowContentWidth
    {
        get => _windowContentWidth;
        set
        {
            if (value != _windowContentWidth)
            {
                _windowContentWidth = value;
                Mediator.Publish(new CompactUiContentChangeMessage(_transferPartHeight, _windowContentWidth));
            }
        }
    }

    public override void Draw()
    {
        WindowContentWidth = UiSharedService.GetWindowContentRegionWidth();
        var versionInfo = _compactVM.Version;
        if (!versionInfo.IsCurrent)
        {
            var ver = versionInfo.Version;
            var unsupported = "UNSUPPORTED VERSION";
            var uidTextSize = ImGui.CalcTextSize(unsupported);
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 - uidTextSize.X / 2);
            UiSharedService.ColorFontText(unsupported, _uiShared.UidFont, ImGuiColors.DalamudRed);
            UiSharedService.ColorText($"Your Mare Synchronos installation is out of date, the current version is {ver.Major}.{ver.Minor}.{ver.Build}. " +
                $"It is highly recommended to keep Mare Synchronos up to date. Open /xlplugins and update the plugin.", ImGuiColors.DalamudRed, true);
        }

        UiSharedService.DrawWithID("header", DrawUIDHeader);
        ImGui.Separator();
        UiSharedService.DrawWithID("serverstatus", DrawServerStatus);

        if (_compactVM.IsConnected)
        {
            var hasShownSyncShells = _showSyncShells;

            ImGui.PushFont(UiBuilder.IconFont);
            if (!hasShownSyncShells)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered]);
            }
            if (ImGui.Button(FontAwesomeIcon.User.ToIconString(), new Vector2((UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30 * ImGuiHelpers.GlobalScale)))
            {
                _showSyncShells = false;
            }
            if (!hasShownSyncShells)
            {
                ImGui.PopStyleColor();
            }
            ImGui.PopFont();
            UiSharedService.AttachToolTip("Individual pairs");

            ImGui.SameLine();

            ImGui.PushFont(UiBuilder.IconFont);
            if (hasShownSyncShells)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered]);
            }
            if (ImGui.Button(FontAwesomeIcon.UserFriends.ToIconString(), new Vector2((UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30 * ImGuiHelpers.GlobalScale)))
            {
                _showSyncShells = true;
            }
            if (hasShownSyncShells)
            {
                ImGui.PopStyleColor();
            }
            ImGui.PopFont();

            UiSharedService.AttachToolTip("Syncshells");

            ImGui.Separator();
            if (!hasShownSyncShells)
            {
                UiSharedService.DrawWithID("pairlist", DrawPairList);
            }
            else
            {
                UiSharedService.DrawWithID("syncshells", _groupPanel.DrawSyncshells);
                TransferPartHeight = ImGui.GetCursorPosY();
            }
            ImGui.Separator();
            UiSharedService.DrawWithID("transfers", DrawTransfers);
            TransferPartHeight = ImGui.GetCursorPosY() - TransferPartHeight;
            UiSharedService.DrawWithID("group-user-popup", () => _selectPairsForGroupUi.Draw(_compactVM.FilteredUsers.Value));
            UiSharedService.DrawWithID("grouping-popup", () => _selectGroupForPairUi.Draw());
        }

        if (_compactVM.OpenPopupOnAdd && _compactVM.CheckLastAddedUser())
        {
            ImGui.OpenPopup("Set Notes for New User");
            _showModalForUserAddition = true;
        }

        if (ImGui.BeginPopupModal("Set Notes for New User", ref _showModalForUserAddition, UiSharedService.PopupWindowFlags))
        {
            if (_compactVM.LastAddedUser == null)
            {
                _showModalForUserAddition = false;
            }
            else
            {
                VM.ExecuteWithProp<string>(nameof(_compactVM.LastAddedUserComment), (comment) =>
                {
                    UiSharedService.TextWrapped($"You have successfully added {_compactVM.LastAddedUser.UserData.AliasOrUID}. Set a local note for the user in the field below:");
                    ImGui.InputTextWithHint("##noteforuser", $"Note for {_compactVM.LastAddedUser.UserData.AliasOrUID}", ref comment, 100);
                    if (UiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save Note"))
                    {
                        _compactVM.SetNoteForLastAddedUser();
                        _showModalForUserAddition = false;
                    }

                    return comment;
                });
            }
            UiSharedService.SetScaledWindowSize(275);
            ImGui.EndPopup();
        }

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (_lastSize != size || _lastPosition != pos)
        {
            _lastSize = size;
            _lastPosition = pos;
            Mediator.Publish(new CompactUiChange(_lastSize, _lastPosition));
        }
    }

    private void DrawAddCharacter()
    {
        ImGui.Dummy(new(10));
        var keys = _compactVM.SecretKeys;
        if (keys.TryGetValue(_compactVM.SecretKeyIdx, out var secretKey))
        {
            if (UiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Add current character with secret key"))
            {
                _compactVM.AddCurrentCharacter();
            }

            _compactVM.SecretKeyIdx = _uiShared.DrawCombo("Secret Key##addCharacterSecretKey", keys, (f) => f.Value.FriendlyName, (f) => _compactVM.SecretKeyIdx = f.Key).Key;
        }
        else
        {
            UiSharedService.ColorText("No secret keys are configured for the current server.", ImGuiColors.DalamudYellow, true);
        }
    }

    private void DrawAddPair()
    {
        var buttonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus);
        ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonSize.X);
        VM.ExecuteWithProp<string>(nameof(CompactVM.PairToAdd), (pairToAdd) =>
        {
            ImGui.InputTextWithHint("##otheruid", "Other players UID/Alias", ref pairToAdd, 20);
            return pairToAdd;
        });
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
        var canAdd = _compactVM.CanAddPair;
        if (!canAdd)
        {
            ImGuiComponents.DisabledButton(FontAwesomeIcon.Plus);
        }
        else
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                _compactVM.AddPair();
            }
            UiSharedService.AttachToolTip("Pair with " + (_compactVM.PairToAdd.IsNullOrEmpty() ? "other user" : _compactVM.PairToAdd));
        }

        ImGuiHelpers.ScaledDummy(2);
    }

    private void DrawFilter()
    {
        var buttonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowUp);
        var playButtonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Play);

        _compactVM.ExecuteWithProp<bool>(nameof(_compactVM.ReverseUserSort), (reverse) =>
        {
            var icon = reverse ? FontAwesomeIcon.ArrowUp : FontAwesomeIcon.ArrowDown;
            var tooltip = reverse ? "Sort by name ascending" : "Sort by name descending";
            if (ImGuiComponents.IconButton(icon))
            {
                reverse = !reverse;
            }
            UiSharedService.AttachToolTip(tooltip);
            return reverse;
        });
        ImGui.SameLine();
        /*
        var users = GetFilteredUsers();
        var userCount = users.Count;

        var spacing = userCount > 0
            ? playButtonSize.X + ImGui.GetStyle().ItemSpacing.X * 2
            : ImGui.GetStyle().ItemSpacing.X;

        ImGui.SetNextItemWidth(WindowContentWidth - buttonSize.X - spacing);
        VM.ExecuteWithProp<string>(nameof(CompactVM.CharacterOrCommentFilter), (str) =>
        {
            ImGui.InputTextWithHint("##filter", "Filter for UID/notes", ref str, 255);
            return str;
        });

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

            if (ImGuiComponents.IconButton(button) && UiSharedService.CtrlPressed())
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
            UiSharedService.AttachToolTip($"Hold Control to {(button == FontAwesomeIcon.Play ? "resume" : "pause")} pairing with {users.Count} out of {userCount} displayed users.");
        }
        else
        {
            var availableAt = (15000 - _timeout.ElapsedMilliseconds) / 1000;
            ImGuiComponents.DisabledButton(button);
            UiSharedService.AttachToolTip($"Next execution is available at {availableAt} seconds");
        }
        */
    }

    private void DrawPairList()
    {
        UiSharedService.DrawWithID("addpair", DrawAddPair);
        UiSharedService.DrawWithID("pairs", DrawPairs);
        TransferPartHeight = ImGui.GetCursorPosY();
        UiSharedService.DrawWithID("filter", DrawFilter);
    }

    private void DrawPairs()
    {
        var ySize = TransferPartHeight == 0
            ? 1
            : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y) - TransferPartHeight - ImGui.GetCursorPosY();

        ImGui.BeginChild("list", new Vector2(WindowContentWidth, ySize), border: false);

        _pairGroupsUi.Draw(_compactVM.VisibleUsers.Value, _compactVM.OnlineUsers.Value, _compactVM.OfflineUsers.Value);

        ImGui.EndChild();
    }

    private void DrawServerStatus()
    {
        var buttonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Link);
        var userCount = _compactVM.OnlineUserCount.ToString(CultureInfo.InvariantCulture);
        var userSize = ImGui.CalcTextSize(userCount);
        var textSize = ImGui.CalcTextSize("Users Online");
        string shardConnection = _compactVM.ShardString;
        var shardTextSize = ImGui.CalcTextSize(_compactVM.ShardString);
        var printShard = !string.IsNullOrEmpty(_compactVM.ShardString) && shardConnection != string.Empty;

        if (_compactVM.IsConnected)
        {
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth()) / 2 - (userSize.X + textSize.X) / 2 - ImGui.GetStyle().ItemSpacing.X / 2);
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
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth()) / 2 - shardTextSize.X / 2);
            ImGui.TextUnformatted(shardConnection);
        }

        ImGui.SameLine();
        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ((userSize.Y + textSize.Y) / 2 + shardTextSize.Y) / 2 - ImGui.GetStyle().ItemSpacing.Y + buttonSize.Y / 2);
        }

        var color = UiSharedService.GetBoolColor(!_compactVM.ManuallyDisconnected);
        var connectedIcon = !_compactVM.ManuallyDisconnected ? FontAwesomeIcon.Link : FontAwesomeIcon.Unlink;

        if (_compactVM.IsConnected)
        {
            ImGui.SetCursorPosX(0 + ImGui.GetStyle().ItemSpacing.X);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.UserCircle))
            {
                Mediator.Publish(new UiToggleMessage(typeof(EditProfileUi)));
            }
            UiSharedService.AttachToolTip("Edit your Mare Profile");
        }

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ((userSize.Y + textSize.Y) / 2 + shardTextSize.Y) / 2 - ImGui.GetStyle().ItemSpacing.Y + buttonSize.Y / 2);
        }

        if (!_compactVM.IsReconnecting)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            if (ImGuiComponents.IconButton(connectedIcon))
            {
                _compactVM.ToggleConnection();
            }
            ImGui.PopStyleColor();
            UiSharedService.AttachToolTip(!_compactVM.ManuallyDisconnected ? "Disconnect from " + _compactVM.ServerName : "Connect to " + _compactVM.ServerName);
        }
    }

    private void DrawTransfers()
    {
        var currentUploads = _fileTransferManager.CurrentUploads.ToList();
        UiSharedService.Icon(FontAwesomeIcon.Upload);
        ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

        if (currentUploads.Any())
        {
            var totalUploads = currentUploads.Count;

            var doneUploads = currentUploads.Count(c => c.IsTransferred);
            var totalUploaded = currentUploads.Sum(c => c.Transferred);
            var totalToUpload = currentUploads.Sum(c => c.Total);

            ImGui.Text($"{doneUploads}/{totalUploads}");
            var uploadText = $"({UiSharedService.ByteToString(totalUploaded)}/{UiSharedService.ByteToString(totalToUpload)})";
            var textSize = ImGui.CalcTextSize(uploadText);
            ImGui.SameLine(_windowContentWidth - textSize.X);
            ImGui.Text(uploadText);
        }
        else
        {
            ImGui.Text("No uploads in progress");
        }

        var currentDownloads = _currentDownloads.SelectMany(d => d.Value.Values).ToList();
        UiSharedService.Icon(FontAwesomeIcon.Download);
        ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

        if (currentDownloads.Any())
        {
            var totalDownloads = currentDownloads.Sum(c => c.TotalFiles);
            var doneDownloads = currentDownloads.Sum(c => c.TransferredFiles);
            var totalDownloaded = currentDownloads.Sum(c => c.TransferredBytes);
            var totalToDownload = currentDownloads.Sum(c => c.TotalBytes);

            ImGui.Text($"{doneDownloads}/{totalDownloads}");
            var downloadText =
                $"({UiSharedService.ByteToString(totalDownloaded)}/{UiSharedService.ByteToString(totalToDownload)})";
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
        var uidText = _compactVM.GetUidText();
        var buttonSizeX = 0f;

        var uidTextSize = UiSharedService.CalcFontTextSize(uidText, _uiShared.UidFont);

        var originalPos = ImGui.GetCursorPos();
        ImGui.SetWindowFontScale(1.5f);
        var buttonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Cog);
        buttonSizeX -= buttonSize.X - ImGui.GetStyle().ItemSpacing.X * 2;
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
        ImGui.SetCursorPosY(originalPos.Y + uidTextSize.Y / 2 - buttonSize.Y / 2);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
        {
            Mediator.Publish(new OpenSettingsUiMessage());
        }
        UiSharedService.AttachToolTip("Open the Mare Synchronos Settings");

        ImGui.SameLine(); //Important to draw the uidText consistently
        ImGui.SetCursorPos(originalPos);

        if (_compactVM.IsConnected)
        {
            buttonSizeX += UiSharedService.GetIconButtonSize(FontAwesomeIcon.Copy).X - ImGui.GetStyle().ItemSpacing.X * 2;
            ImGui.SetCursorPosY(originalPos.Y + uidTextSize.Y / 2 - buttonSize.Y / 2);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
            {
                ImGui.SetClipboardText(_compactVM.GetUidText());
            }
            UiSharedService.AttachToolTip("Copy your UID to clipboard");
            ImGui.SameLine();
        }
        ImGui.SetWindowFontScale(1f);

        ImGui.SetCursorPosY(originalPos.Y + buttonSize.Y / 2 - uidTextSize.Y / 2 - ImGui.GetStyle().ItemSpacing.Y / 2);
        ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 + buttonSizeX - uidTextSize.X / 2);
        UiSharedService.ColorFontText(uidText, _uiShared.UidFont, _compactVM.GetUidColor());

        if (!_compactVM.IsConnected)
        {
            UiSharedService.ColorText(_compactVM.GetServerError(), _compactVM.GetUidColor(), true);
            if (_compactVM.IsNoSecretKey)
            {
                DrawAddCharacter();
            }
        }
    }

    private void UiSharedService_GposeEnd()
    {
        IsOpen = _wasOpen;
    }

    private void UiSharedService_GposeStart()
    {
        _wasOpen = IsOpen;
        IsOpen = false;
    }
}