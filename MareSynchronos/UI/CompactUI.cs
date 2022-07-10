using System;
using System.Collections.Generic;
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

namespace MareSynchronos.UI
{
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

        private float _transferPartHeight = 0;

        private float _windowContentWidth = 0;

        public CompactUi(WindowSystem windowSystem,
            UiShared uiShared, Configuration configuration, ApiController apiController) : base("Mare Synchronos " + Assembly.GetExecutingAssembly().GetName().Version)
        {
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
            if (_apiController.ServerState is not ServerState.Offline)
            {
                UiShared.DrawWithID("serverstatus", DrawServerStatus);
            }

            if (_apiController.ServerState is ServerState.Connected)
            {
                ImGui.Separator();
                UiShared.DrawWithID("pairlist", DrawPairList);
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
            ImGui.SetNextItemWidth(_windowContentWidth - buttonSize.X - ImGui.GetStyle().ItemSpacing.X);
            ImGui.InputTextWithHint("##filter", "Filter for UID/notes", ref _characterOrCommentFilter, 255);
        }

        private void DrawPairedClient(ClientPairDto entry)
        {
            var pauseIcon = entry.IsPaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;

            var buttonSize = UiShared.GetIconButtonSize(pauseIcon);
            var trashButtonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Trash);
            var textSize = ImGui.CalcTextSize(entry.OtherUID);
            var originalY = ImGui.GetCursorPosY();
            var buttonSizes = buttonSize.Y + trashButtonSize.Y;

            var textPos = originalY + buttonSize.Y / 2 - textSize.Y / 2;
            ImGui.SetCursorPosY(textPos);
            if (!entry.IsSynced)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                UiShared.ColorText(FontAwesomeIcon.ArrowUp.ToIconString(), ImGuiColors.DalamudRed);
                ImGui.PopFont();

                UiShared.AttachToolTip(entry.OtherUID + " has not added you back");
            }
            else if (entry.IsPaused || entry.IsPausedFromOthers)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                UiShared.ColorText(FontAwesomeIcon.PauseCircle.ToIconString(), ImGuiColors.DalamudYellow);
                ImGui.PopFont();

                UiShared.AttachToolTip("Pairing status with " + entry.OtherUID + " is paused");
            }
            else
            {
                ImGui.PushFont(UiBuilder.IconFont);
                UiShared.ColorText(FontAwesomeIcon.Check.ToIconString(), ImGuiColors.ParsedGreen);
                ImGui.PopFont();

                UiShared.AttachToolTip("You are paired with " + entry.OtherUID);
            }

            var textIsUid = true;
            _showUidForEntry.TryGetValue(entry.OtherUID, out var showUidInsteadOfName);
            if (!showUidInsteadOfName && _configuration.GetCurrentServerUidComments().TryGetValue(entry.OtherUID, out var playerText))
            {
                if (playerText.IsNullOrEmpty())
                {
                    playerText = entry.OtherUID;
                }
                else
                {
                    textIsUid = false;
                }
            }
            else
            {
                playerText = entry.OtherUID;
            }

            ImGui.SameLine();
            if (_editNickEntry != entry.OtherUID)
            {
                ImGui.SetCursorPosY(textPos);
                if (textIsUid) ImGui.PushFont(UiBuilder.MonoFont);
                ImGui.TextUnformatted(playerText);
                if (textIsUid) ImGui.PopFont();
                UiShared.AttachToolTip("Left click to switch between UID display and nick" + Environment.NewLine +
                              "Right click to change nick for " + entry.OtherUID);
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
                if (ImGui.GetIO().KeyCtrl)
                {
                    _ = _apiController.SendPairedClientRemoval(entry.OtherUID);
                    _apiController.PairedClients.Remove(entry);
                }
            }
            UiShared.AttachToolTip("Hold CTRL and click to unpair permanently from " + entry.OtherUID);

            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X - ImGui.GetStyle().ItemSpacing.X - trashButtonSize.X);
            ImGui.SetCursorPosY(originalY);
            if (ImGuiComponents.IconButton(pauseIcon))
            {
                _ = _apiController.SendPairedClientPauseChange(entry.OtherUID, !entry.IsPaused);
            }
            UiShared.AttachToolTip(entry.IsSynced
                ? "Pause pairing with " + entry.OtherUID
                : "Resume pairing with " + entry.OtherUID);
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
            var users = _apiController.PairedClients.Where(p =>
            {
                if (_characterOrCommentFilter.IsNullOrEmpty()) return true;
                _configuration.GetCurrentServerUidComments().TryGetValue(p.OtherUID, out var comment);
                return p.OtherUID.ToLower().Contains(_characterOrCommentFilter.ToLower()) ||
                       (comment?.ToLower().Contains(_characterOrCommentFilter.ToLower()) ?? false);
            });

            if (_configuration.ReverseUserSort) users = users.Reverse();

            ImGui.BeginChild("list", new Vector2(_windowContentWidth, ySize), false);
            foreach (var entry in users.ToList())
            {
                UiShared.DrawWithID(entry.OtherUID, () => DrawPairedClient(entry));
            }
            ImGui.EndChild();
        }

        private void DrawServerStatus()
        {
            if (_apiController.ServerAlive)
            {
                var buttonSize = UiShared.GetIconButtonSize(FontAwesomeIcon.Link);
                var textSize = ImGui.CalcTextSize(_apiController.SystemInfoDto.CpuUsage.ToString("0.00") + "%");
                var originalY = ImGui.GetCursorPosY();

                var textPos = originalY + buttonSize.Y / 2 - textSize.Y / 2;

                ImGui.SetCursorPosY(textPos);
                ImGui.TextColored(ImGuiColors.ParsedGreen, _apiController.OnlineUsers.ToString());
                ImGui.SameLine();
                ImGui.SetCursorPosY(textPos);
                ImGui.Text("Users Online");
                ImGui.SameLine();
                ImGui.SetCursorPosY(textPos);
                UiShared.ColorText(_apiController.SystemInfoDto.CpuUsage.ToString("0.00") + "%", UiShared.GetCpuLoadColor(_apiController.SystemInfoDto.CpuUsage));
                ImGui.SameLine();
                ImGui.SetCursorPosY(textPos);
                ImGui.Text("Load");
                UiShared.AttachToolTip("This is the current servers' CPU load");

                ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiShared.GetWindowContentRegionWidth() - buttonSize.X);
                ImGui.SetCursorPosY(originalY);
                var serverIsConnected = _apiController.ServerState is ServerState.Connected;
                var color = UiShared.GetBoolColor(serverIsConnected);
                var connectedIcon = serverIsConnected ? FontAwesomeIcon.Link : FontAwesomeIcon.Unlink;

                ImGui.PushStyleColor(ImGuiCol.Text, color);
                if (ImGuiComponents.IconButton(connectedIcon))
                {
                    if (_apiController.ServerState == ServerState.Connected)
                    {
                        _configuration.FullPause = true;
                        _configuration.Save();
                    }
                    else
                    {
                        _configuration.FullPause = false;
                        _configuration.Save();
                    }
                    _ = _apiController.CreateConnections();
                }
                ImGui.PopStyleColor();
                UiShared.AttachToolTip(_apiController.IsConnected ? "Disconnect from " + _apiController.ServerDictionary[_configuration.ApiUri] : "Connect to " + _apiController.ServerDictionary[_configuration.ApiUri]);
            }
            else
            {
                UiShared.ColorTextWrapped("Server is offline", ImGuiColors.DalamudRed);
            }
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

            var currentDownloads = _apiController.CurrentDownloads.ToList();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text(FontAwesomeIcon.Download.ToIconString());
            ImGui.PopFont();
            ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

            if (currentDownloads.Any())
            {
                var totalDownloads = currentDownloads.Count;
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
                    "The plugin or server you are connecting to is outdated. Please update your plugin now. If you already did so, contact the server provider to update their server to the latest version.",
                ServerState.NoAccount => "Idk how you got here but you have no account. What are you doing?",
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
                ServerState.NoAccount => ImGuiColors.DalamudRed,
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
                ServerState.NoAccount => "No account",
                ServerState.Connected => _apiController.UID,
                _ => string.Empty
            };
        }
    }
}
