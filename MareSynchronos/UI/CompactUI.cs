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
        private readonly UiShared _uiShared;
        private readonly WindowSystem _windowSystem;
        private string _characterOrCommentFilter = string.Empty;

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
                MinimumSize = new Vector2(300, 200),
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
            _windowContentWidth = ImGui.GetWindowContentRegionWidth();
            DrawUIDHeader();
            ImGui.Separator();
            if (_apiController.ServerState is not ServerState.Offline)
            {
                DrawServerStatus();
            }

            if (_apiController.ServerState is ServerState.Connected)
            {
                ImGui.Separator();
                DrawPairList();
                ImGui.Separator();
                DrawTransfers();
                _transferPartHeight = ImGui.GetCursorPosY() - _transferPartHeight;
            }
        }
        private void DrawAddPair()
        {
            ImGui.PushID("pairs");
            var buttonSize = GetIconButtonSize(FontAwesomeIcon.Plus);
            ImGui.SetNextItemWidth(ImGui.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonSize.X);
            ImGui.InputTextWithHint("##otheruid", "Other players UID", ref _pairToAdd, 10);
            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + ImGui.GetWindowContentRegionWidth() - buttonSize.X);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                if (_apiController.PairedClients.All(w => w.OtherUID != _pairToAdd))
                {
                    _ = _apiController.SendPairedClientAddition(_pairToAdd);
                    _pairToAdd = string.Empty;
                }
            }
            AttachToolTip("Pair with " + (_pairToAdd.IsNullOrEmpty() ? "other user" : _pairToAdd));

            ImGuiHelpers.ScaledDummy(2);
            ImGui.PopID();
        }

        private void DrawFilter()
        {
            ImGui.PushID("filter");
            ImGui.SetNextItemWidth(_windowContentWidth);
            ImGui.InputTextWithHint("##filter", "Filter for UID/notes", ref _characterOrCommentFilter, 255);
            ImGui.PopID();
        }

        private void DrawPairedClient(ClientPairDto entry)
        {
            ImGui.PushID(entry.OtherUID);

            var pauseIcon = entry.IsPaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;

            var buttonSize = GetIconButtonSize(pauseIcon);
            var textSize = ImGui.CalcTextSize(_apiController.SystemInfoDto.CpuUsage.ToString("0.00") + "%");
            var originalY = ImGui.GetCursorPosY();

            var textPos = originalY + buttonSize.Y / 2 - textSize.Y / 2;
            ImGui.SetCursorPosY(textPos);
            if (!entry.IsSynced)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                UiShared.ColorText(FontAwesomeIcon.ArrowUp.ToIconString(), ImGuiColors.DalamudRed);
                ImGui.PopFont();

                AttachToolTip(entry.OtherUID + " has not added you back");
            }
            else if (entry.IsPaused || entry.IsPausedFromOthers)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                UiShared.ColorText(FontAwesomeIcon.PauseCircle.ToIconString(), ImGuiColors.DalamudYellow);
                ImGui.PopFont();

                AttachToolTip("Pairing status with " + entry.OtherUID + " is paused");
            }
            else
            {
                ImGui.PushFont(UiBuilder.IconFont);
                UiShared.ColorText(FontAwesomeIcon.Check.ToIconString(), ImGuiColors.ParsedGreen);
                ImGui.PopFont();

                AttachToolTip("You are paired with " + entry.OtherUID);
            }
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPos);
            ImGui.Text(entry.OtherUID);

            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + ImGui.GetWindowContentRegionWidth() - buttonSize.X);
            ImGui.SetCursorPosY(originalY);
            if (ImGuiComponents.IconButton(pauseIcon))
            {
                _ = _apiController.SendPairedClientPauseChange(entry.OtherUID, !entry.IsPaused);
            }
            AttachToolTip(entry.IsSynced
                ? "Pause pairing with " + entry.OtherUID
                : "Resume pairing with " + entry.OtherUID);

            string charComment = _configuration.GetCurrentServerUidComments().ContainsKey(entry.OtherUID) ? _configuration.GetCurrentServerUidComments()[entry.OtherUID] : string.Empty;
            buttonSize = GetIconButtonSize(FontAwesomeIcon.Trash);
            ImGui.SetNextItemWidth(ImGui.GetWindowContentRegionMin().X + ImGui.GetWindowContentRegionWidth() - buttonSize.X - ImGui.GetStyle().ItemSpacing.X);
            if (ImGui.InputTextWithHint("##comment", "Nick/Notes", ref charComment, 255))
            {
                if (_configuration.GetCurrentServerUidComments().Count == 0)
                {
                    _configuration.UidServerComments[_configuration.ApiUri] =
                        new Dictionary<string, string>();
                }
                _configuration.SetCurrentServerUidComment(entry.OtherUID, charComment);
                _configuration.Save();
            }

            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + ImGui.GetWindowContentRegionWidth() - buttonSize.X);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
            {
                if (ImGui.GetIO().KeyCtrl)
                {
                    _ = _apiController.SendPairedClientRemoval(entry.OtherUID);
                    _apiController.PairedClients.Remove(entry);
                }
            }
            AttachToolTip("Hold CTRL to unpair permanently from " + entry.OtherUID);

            ImGuiHelpers.ScaledDummy(5);

            ImGui.PopID();
        }

        private void AttachToolTip(string text)
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(text);
            }
        }

        private void DrawPairList()
        {
            ImGui.PushID("pairlist");
            DrawAddPair();
            DrawPairs();
            _transferPartHeight = ImGui.GetCursorPosY();
            DrawFilter();
            ImGui.PopID();
        }

        private void DrawPairs()
        {
            ImGui.PushID("pairs");

            var ySize = _transferPartHeight == 0
                ? 1
                : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y) - _transferPartHeight - ImGui.GetCursorPosY();
            ImGui.BeginChild("list", new Vector2(_windowContentWidth, ySize), false);
            foreach (var entry in _apiController.PairedClients.Where(p =>
                     {
                         if (_characterOrCommentFilter.IsNullOrEmpty()) return true;
                         _configuration.GetCurrentServerUidComments().TryGetValue(p.OtherUID, out string? comment);
                         return p.OtherUID.ToLower().Contains(_characterOrCommentFilter.ToLower()) || (comment?.ToLower().Contains(_characterOrCommentFilter.ToLower()) ?? false);
                     }).ToList())
            {
                DrawPairedClient(entry);
            }
            ImGui.EndChild();

            ImGui.PopID();
        }

        private void DrawServerStatus()
        {
            ImGui.PushID("serverstate");
            if (_apiController.ServerAlive)
            {
                var buttonSize = GetIconButtonSize(FontAwesomeIcon.Link);
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
                AttachToolTip("This is the current servers' CPU load");

                ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + ImGui.GetWindowContentRegionWidth() - buttonSize.X);
                ImGui.SetCursorPosY(originalY);
                var serverIsConnected = _apiController.ServerState is ServerState.Connected;
                var color = UiShared.GetBoolColor(serverIsConnected);
                FontAwesomeIcon connectedIcon = serverIsConnected ? FontAwesomeIcon.Link : FontAwesomeIcon.Unlink;

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
                AttachToolTip(_apiController.IsConnected ? "Disconnect from " + _apiController.ServerDictionary[_configuration.ApiUri] : "Connect to " + _apiController.ServerDictionary[_configuration.ApiUri]);
            }
            else
            {
                UiShared.ColorTextWrapped("Server is offline", ImGuiColors.DalamudRed);
            }
            ImGui.PopID();
        }

        private void DrawTransfers()
        {
            ImGui.PushID("transfers");
            var currentUploads = _apiController.CurrentUploads.ToList();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text(FontAwesomeIcon.Upload.ToIconString());
            ImGui.PopFont();
            ImGui.SameLine(35);

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
            ImGui.SameLine(35);

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
            ImGui.PopID();
        }

        private void DrawUIDHeader()
        {
            ImGui.PushID("header");
            var uidText = GetUidText();

            if (_uiShared.UidFontBuilt) ImGui.PushFont(_uiShared.UidFont);
            var uidTextSize = ImGui.CalcTextSize(uidText);
            if (_uiShared.UidFontBuilt) ImGui.PopFont();

            var originalPos = ImGui.GetCursorPosY();
            ImGui.SetWindowFontScale(1.5f);
            var buttonSize = GetIconButtonSize(FontAwesomeIcon.Cog);
            if (_apiController.ServerState is ServerState.Connected)
            {
                ImGui.SetCursorPosY(originalPos + uidTextSize.Y / 2 - buttonSize.Y / 2);
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
                {
                    ImGui.SetClipboardText(_apiController.UID);
                }
                AttachToolTip("Copy your UID to clipboard");
                ImGui.SameLine();
            }
            ImGui.SetWindowFontScale(1f);

            ImGui.SetCursorPosY(originalPos - uidTextSize.Y / 8);
            if (_uiShared.UidFontBuilt) ImGui.PushFont(_uiShared.UidFont);
            ImGui.TextColored(GetUidColor(), uidText);
            if (_uiShared.UidFontBuilt) ImGui.PopFont();

            ImGui.SetWindowFontScale(1.5f);

            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + ImGui.GetWindowContentRegionWidth() - buttonSize.X);
            ImGui.SetCursorPosY(originalPos + uidTextSize.Y / 2 - buttonSize.Y / 2);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
            {
                OpenSettingsUi?.Invoke();
            }
            AttachToolTip("Open the Mare Synchronos Settings");

            ImGui.SetWindowFontScale(1f);

            if (_apiController.ServerState is not ServerState.Connected)
            {
                UiShared.ColorTextWrapped(GetServerError(), GetUidColor());
            }

            ImGui.PopID();
        }

        private Vector2 GetIconButtonSize(FontAwesomeIcon icon)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            var buttonSize = ImGuiHelpers.GetButtonSize(icon.ToIconString());
            ImGui.PopFont();
            return buttonSize;
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
