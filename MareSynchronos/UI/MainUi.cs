using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.WebAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using MareSynchronos.API;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;

namespace MareSynchronos.UI
{
    public delegate void SwitchUi();
    public class MainUi : Window, IDisposable
    {
        private readonly Configuration _configuration;
        private readonly WindowSystem _windowSystem;
        private readonly ApiController _apiController;
        private readonly UiShared _uiShared;
        public event SwitchUi? SwitchFromMainUiToIntro;

        public MainUi(WindowSystem windowSystem,
            UiShared uiShared, Configuration configuration, ApiController apiController) : base("Mare Synchronos Settings", ImGuiWindowFlags.None)
        {
            Logger.Verbose("Creating " + nameof(MainUi));

            SizeConstraints = new WindowSizeConstraints()
            {
                MinimumSize = new Vector2(800, 400),
                MaximumSize = new Vector2(800, 2000),
            };

            _configuration = configuration;
            _windowSystem = windowSystem;
            _apiController = apiController;
            _uiShared = uiShared;
            windowSystem.AddWindow(this);
        }

        public void Dispose()
        {
            Logger.Verbose("Disposing " + nameof(MainUi));

            _windowSystem.RemoveWindow(this);
        }

        public override void Draw()
        {
            if (!IsOpen)
            {
                return;
            }

            var pluginState = _uiShared.DrawOtherPluginState();

            if (pluginState)
                DrawSettingsContent();
        }

        private void DrawSettingsContent()
        {
            _uiShared.PrintServerState();
            ImGui.Separator();

            if (_apiController.ServerState is ServerState.Connected)
            {
                ImGui.SetWindowFontScale(1.2f);
                ImGui.Text("Your UID");
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.ParsedGreen, _apiController.UID);
                ImGui.SameLine();
                ImGui.SetWindowFontScale(1.0f);
                if (ImGui.Button("Copy UID"))
                {
                    ImGui.SetClipboardText(_apiController.UID);
                }

                ImGui.Text("Share this UID to other Mare users so they pair their client with yours.");
            }
            else
            {
                string errorMsg = _apiController.ServerState switch
                {
                    ServerState.Disconnected => "Disconnected",
                    ServerState.Unauthorized => "Unauthorized",
                    ServerState.VersionMisMatch => "Service version mismatch",
                    ServerState.Offline => "Service unavailable"
                };
                ImGui.SetWindowFontScale(1.2f);
                ImGui.TextColored(ImGuiColors.DalamudRed, $"No UID ({errorMsg})");
                ImGui.SetWindowFontScale(1.0f);
                switch (_apiController.ServerState)
                {
                    case ServerState.Disconnected:
                        UiShared.ColorTextWrapped("You are currently disconnected from the Mare Synchronos service.", ImGuiColors.DalamudRed);
                        break;
                    case ServerState.Unauthorized:
                        UiShared.ColorTextWrapped("Your account is not present on the service anymore or you are banned.", ImGuiColors.DalamudRed);
                        UiShared.ColorTextWrapped("If you think your secret key is just invalid, use the following button to reset " +
                                                  "the local secret key to be able to re-register. If you continue to see this message after " +
                                                  "registering, tough luck, asshole.", ImGuiColors.DalamudYellow);
                        if (ImGui.Button("Reset Secret Key"))
                        {
                            _configuration.ClientSecret.Remove(_configuration.ApiUri);
                            _configuration.Save();
                            SwitchFromMainUiToIntro?.Invoke();
                        }
                        break;
                    case ServerState.Offline:
                        UiShared.ColorTextWrapped("Your selected Mare Synchronos server is currently offline.", ImGuiColors.DalamudRed);
                        break;
                    case ServerState.VersionMisMatch:
                        UiShared.ColorTextWrapped("The server or your client is outdated. If the client has recently been updated for breaking " +
                                                  "service changes, the service must follow suit.", ImGuiColors.DalamudRed);
                        break;
                    case ServerState.Connected:
                    default:
                        break;

                }
            }

            ImGui.Separator();
            if (_apiController.IsConnected)
                DrawPairedClientsContent();
            DrawFileCacheSettings();
            if (_apiController.IsConnected)
                DrawCurrentTransfers();
            DrawBlockedTransfers();
            DrawUserAdministration(_apiController.IsConnected);
            if (_apiController.IsConnected && _apiController.IsModerator)
                DrawAdministration();
        }

        private string _forbiddenFileHashEntry = string.Empty;
        private string _forbiddenFileHashForbiddenBy = string.Empty;
        private string _bannedUserHashEntry = string.Empty;
        private string _bannedUserReasonEntry = string.Empty;

        private void DrawAdministration()
        {
            if (ImGui.TreeNode("Administrative Actions"))
            {
                if (ImGui.TreeNode("Forbidden Files Changes"))
                {
                    if (ImGui.BeginTable("ForbiddenFilesTable", 3, ImGuiTableFlags.RowBg))
                    {
                        ImGui.TableSetupColumn("File Hash", ImGuiTableColumnFlags.None, 290);
                        ImGui.TableSetupColumn("Forbidden By", ImGuiTableColumnFlags.None, 290);
                        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 70);

                        ImGui.TableHeadersRow();

                        foreach (var forbiddenFile in _apiController.AdminForbiddenFiles)
                        {
                            ImGui.TableNextColumn();

                            ImGui.Text(forbiddenFile.Hash);
                            ImGui.TableNextColumn();
                            string by = forbiddenFile.ForbiddenBy;
                            if (ImGui.InputText("##forbiddenBy" + forbiddenFile.Hash, ref by, 255))
                            {
                                forbiddenFile.ForbiddenBy = by;
                            }

                            ImGui.TableNextColumn();
                            if (_apiController.IsAdmin)
                            {
                                ImGui.PushFont(UiBuilder.IconFont);
                                if (ImGui.Button(
                                        FontAwesomeIcon.Upload.ToIconString() + "##updateFile" + forbiddenFile.Hash))
                                {
                                    _ = _apiController.AddOrUpdateForbiddenFileEntry(forbiddenFile);
                                }

                                ImGui.SameLine();
                                if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString() + "##deleteFile" +
                                                 forbiddenFile.Hash))
                                {
                                    _ = _apiController.DeleteForbiddenFileEntry(forbiddenFile);
                                }

                                ImGui.PopFont();
                            }

                        }

                        if (_apiController.IsAdmin)
                        {
                            ImGui.TableNextColumn();
                            ImGui.InputText("##addFileHash", ref _forbiddenFileHashEntry, 255);
                            ImGui.TableNextColumn();
                            ImGui.InputText("##addForbiddenBy", ref _forbiddenFileHashForbiddenBy, 255);
                            ImGui.TableNextColumn();
                            ImGui.PushFont(UiBuilder.IconFont);
                            if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString() + "##addForbiddenFile"))
                            {
                                _ = _apiController.AddOrUpdateForbiddenFileEntry(new ForbiddenFileDto()
                                {
                                    ForbiddenBy = _forbiddenFileHashForbiddenBy,
                                    Hash = _forbiddenFileHashEntry
                                });
                            }

                            ImGui.PopFont();
                            ImGui.NextColumn();
                        }

                        ImGui.EndTable();
                    }

                    ImGui.TreePop();
                }

                if (ImGui.TreeNode("Banned Users"))
                {
                    if (ImGui.BeginTable("BannedUsersTable", 3, ImGuiTableFlags.RowBg))
                    {
                        ImGui.TableSetupColumn("Character Hash", ImGuiTableColumnFlags.None, 290);
                        ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.None, 290);
                        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 70);

                        ImGui.TableHeadersRow();

                        foreach (var bannedUser in _apiController.AdminBannedUsers)
                        {
                            ImGui.TableNextColumn();
                            ImGui.Text(bannedUser.CharacterHash);

                            ImGui.TableNextColumn();
                            string reason = bannedUser.Reason;
                            ImGuiInputTextFlags moderatorFlags = _apiController.IsModerator
                                ? ImGuiInputTextFlags.ReadOnly
                                : ImGuiInputTextFlags.None;
                            if (ImGui.InputText("##bannedReason" + bannedUser.CharacterHash, ref reason, 255,
                                    moderatorFlags))
                            {
                                bannedUser.Reason = reason;
                            }

                            ImGui.TableNextColumn();
                            ImGui.PushFont(UiBuilder.IconFont);
                            if (_apiController.IsAdmin)
                            {
                                if (ImGui.Button(FontAwesomeIcon.Upload.ToIconString() + "##updateUser" +
                                                 bannedUser.CharacterHash))
                                {
                                    _ = _apiController.AddOrUpdateBannedUserEntry(bannedUser);
                                }

                                ImGui.SameLine();
                            }

                            if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString() + "##deleteUser" +
                                             bannedUser.CharacterHash))
                            {
                                _ = _apiController.DeleteBannedUserEntry(bannedUser);
                            }

                            ImGui.PopFont();
                        }

                        ImGui.TableNextColumn();
                        ImGui.InputText("##addUserHash", ref _bannedUserHashEntry, 255);

                        ImGui.TableNextColumn();
                        if (_apiController.IsAdmin)
                        {
                            ImGui.InputText("##addUserReason", ref _bannedUserReasonEntry, 255);
                        }
                        else
                        {
                            _bannedUserReasonEntry = "Banned by " + _uiShared.PlayerName;
                            ImGui.InputText("##addUserReason", ref _bannedUserReasonEntry, 255,
                                ImGuiInputTextFlags.ReadOnly);
                        }

                        ImGui.TableNextColumn();
                        ImGui.PushFont(UiBuilder.IconFont);
                        if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString() + "##addForbiddenFile"))
                        {
                            _ = _apiController.AddOrUpdateBannedUserEntry(new BannedUserDto()
                            {
                                CharacterHash = _forbiddenFileHashForbiddenBy,
                                Reason = _forbiddenFileHashEntry
                            });
                        }

                        ImGui.PopFont();

                        ImGui.EndTable();
                    }

                    ImGui.TreePop();
                }

                if (ImGui.TreeNode("Online Users"))
                {
                    if (ImGui.Button("Refresh Online Users"))
                    {
                        _ = _apiController.RefreshOnlineUsers();
                    }

                    if (ImGui.BeginTable("OnlineUsersTable", 3, ImGuiTableFlags.RowBg))
                    {
                        ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.None, 100);
                        ImGui.TableSetupColumn("Character Hash", ImGuiTableColumnFlags.None, 300);
                        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 70);

                        ImGui.TableHeadersRow();

                        foreach (var onlineUser in _apiController.AdminOnlineUsers)
                        {
                            ImGui.TableNextColumn();
                            ImGui.PushFont(UiBuilder.IconFont);
                            string icon = onlineUser.IsModerator
                                ? FontAwesomeIcon.ChessKing.ToIconString()
                                : onlineUser.IsAdmin
                                    ? FontAwesomeIcon.Crown.ToIconString()
                                    : FontAwesomeIcon.User.ToIconString();
                            ImGui.Text(icon);
                            ImGui.PopFont();
                            ImGui.SameLine();

                            ImGui.Text(onlineUser.UID);
                            ImGui.SameLine();
                            ImGui.PushFont(UiBuilder.IconFont);
                            if (ImGui.Button(FontAwesomeIcon.Copy.ToIconString() + "##onlineUserCopyUID" +
                                             onlineUser.CharacterNameHash))
                            {
                                ImGui.SetClipboardText(onlineUser.UID);
                            }

                            ImGui.PopFont();

                            ImGui.TableNextColumn();
                            string charNameHash = onlineUser.CharacterNameHash;
                            ImGui.InputText("##onlineUserHash" + onlineUser.CharacterNameHash, ref charNameHash, 255,
                                ImGuiInputTextFlags.ReadOnly);
                            ImGui.SameLine();
                            ImGui.PushFont(UiBuilder.IconFont);
                            if (ImGui.Button(FontAwesomeIcon.Copy.ToIconString() + "##onlineUserCopyHash" +
                                             onlineUser.CharacterNameHash))
                            {
                                ImGui.SetClipboardText(onlineUser.UID);
                            }

                            ImGui.PopFont();

                            ImGui.TableNextColumn();
                            ImGui.PushFont(UiBuilder.IconFont);
                            if (ImGui.Button(FontAwesomeIcon.SkullCrossbones.ToIconString() + "##onlineUserBan" +
                                             onlineUser.CharacterNameHash))
                            {
                                _ = _apiController.AddOrUpdateBannedUserEntry(new BannedUserDto
                                {
                                    CharacterHash = onlineUser.CharacterNameHash,
                                    Reason = "Banned by " + _uiShared.PlayerName
                                });
                            }
                            ImGui.SameLine();
                            if (onlineUser.UID != _apiController.UID && _apiController.IsAdmin)
                            {
                                if (!onlineUser.IsModerator)
                                {
                                    if (ImGui.Button(FontAwesomeIcon.ChessKing.ToIconString() +
                                                     "##onlineUserModerator" +
                                                     onlineUser.CharacterNameHash))
                                    {
                                        _apiController.PromoteToModerator(onlineUser.UID);
                                    }
                                }
                                else
                                {
                                    if (ImGui.Button(FontAwesomeIcon.User.ToIconString() +
                                                     "##onlineUserNonModerator" +
                                                     onlineUser.CharacterNameHash))
                                    {
                                        _apiController.DemoteFromModerator(onlineUser.UID);
                                    }
                                }
                            }

                            ImGui.PopFont();
                        }
                        ImGui.EndTable();
                    }
                    ImGui.TreePop();
                }
                ImGui.TreePop();
            }
        }

        private bool _deleteFilesPopupModalShown = false;
        private bool _deleteAccountPopupModalShown = false;

        private void DrawUserAdministration(bool serverAlive)
        {
            if (ImGui.TreeNode(
                    $"User Administration"))
            {
                if (serverAlive)
                {
                    if (ImGui.Button("Delete all my files"))
                    {
                        _deleteFilesPopupModalShown = true;
                        ImGui.OpenPopup("Delete all your files?");
                    }

                    UiShared.DrawHelpText("Completely deletes all your uploaded files on the service.");

                    if (ImGui.BeginPopupModal("Delete all your files?", ref _deleteFilesPopupModalShown,
                            ImGuiWindowFlags.AlwaysAutoResize))
                    {
                        UiShared.TextWrapped(
                            "All your own uploaded files on the service will be deleted.\nThis operation cannot be undone.");
                        ImGui.Text("Are you sure you want to continue?");
                        ImGui.Separator();
                        if (ImGui.Button("Delete everything", new Vector2(150, 0)))
                        {
                            Task.Run(() => _apiController.DeleteAllMyFiles());
                            ImGui.CloseCurrentPopup();
                            _deleteFilesPopupModalShown = false;
                        }

                        ImGui.SameLine();

                        if (ImGui.Button("Cancel##cancelDelete", new Vector2(150, 0)))
                        {
                            ImGui.CloseCurrentPopup();
                            _deleteFilesPopupModalShown = false;
                        }

                        ImGui.EndPopup();
                    }

                    if (ImGui.Button("Delete account"))
                    {
                        _deleteAccountPopupModalShown = true;
                        ImGui.OpenPopup("Delete your account?");
                    }

                    UiShared.DrawHelpText("Completely deletes your account and all uploaded files to the service.");

                    if (ImGui.BeginPopupModal("Delete your account?", ref _deleteAccountPopupModalShown,
                            ImGuiWindowFlags.AlwaysAutoResize))
                    {
                        UiShared.TextWrapped(
                            "Your account and all associated files and data on the service will be deleted.");
                        UiShared.TextWrapped("Your UID will be removed from all pairing lists.");
                        ImGui.Text("Are you sure you want to continue?");
                        ImGui.Separator();
                        if (ImGui.Button("Delete account", new Vector2(150, 0)))
                        {
                            Task.Run(() => _apiController.DeleteAccount());
                            ImGui.CloseCurrentPopup();
                            _deleteAccountPopupModalShown = false;
                            SwitchFromMainUiToIntro?.Invoke();
                        }

                        ImGui.SameLine();

                        if (ImGui.Button("Cancel##cancelDelete", new Vector2(150, 0)))
                        {
                            ImGui.CloseCurrentPopup();
                            _deleteAccountPopupModalShown = false;
                        }

                        ImGui.EndPopup();
                    }
                }

                if (!_configuration.FullPause)
                {
                    UiShared.ColorTextWrapped("Note: to change servers you need to disconnect from your current Mare Synchronos server.", ImGuiColors.DalamudYellow);
                }

                var marePaused = _configuration.FullPause;

                if (_configuration.HasValidSetup())
                {
                    if (ImGui.Checkbox("Disconnect Mare Synchronos", ref marePaused))
                    {
                        _configuration.FullPause = marePaused;
                        _configuration.Save();
                        Task.Run(_apiController.CreateConnections);
                    }

                    UiShared.DrawHelpText("Completely pauses the sync and clears your current data (not uploaded files) on the service.");
                }
                else
                {
                    UiShared.ColorText("You cannot reconnect without a valid account on the service.", ImGuiColors.DalamudYellow);
                }

                if (marePaused)
                {
                    _uiShared.DrawServiceSelection(() => SwitchFromMainUiToIntro?.Invoke());
                }

                ImGui.TreePop();
            }
        }

        private void DrawBlockedTransfers()
        {
            if (ImGui.TreeNode(
                    $"Forbidden Transfers"))
            {
                UiShared.ColorTextWrapped("Files that you attempted to upload or download that were forbidden to be transferred by their creators will appear here. " +
                                     "If you see file paths from your drive here, then those files were not allowed to be uploaded. If you see hashes, those files were not allowed to be downloaded. " +
                                     "Ask your paired friend to send you the mod in question through other means, acquire the mod yourself or pester the mod creator to allow it to be sent over Mare.",
                    ImGuiColors.DalamudGrey);

                if (ImGui.BeginTable("TransfersTable", 2, ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn(
                        $"Hash/Filename");
                    ImGui.TableSetupColumn($"Forbidden by");

                    ImGui.TableHeadersRow();

                    foreach (var item in _apiController.ForbiddenTransfers)
                    {
                        ImGui.TableNextColumn();
                        if (item is UploadFileTransfer transfer)
                        {
                            ImGui.Text(transfer.LocalFile);
                        }
                        else
                        {
                            ImGui.Text(item.Hash);
                        }
                        ImGui.TableNextColumn();
                        ImGui.Text(item.ForbiddenBy);
                    }
                    ImGui.EndTable();
                }

                ImGui.TreePop();
            }
        }

        private void DrawCurrentTransfers()
        {
            if (ImGui.TreeNode(
                    $"Current Transfers"))
            {
                bool showTransferWindow = _configuration.ShowTransferWindow;
                if (ImGui.Checkbox("Show separate Transfer window while transfers are active", ref showTransferWindow))
                {
                    _configuration.ShowTransferWindow = showTransferWindow;
                    _configuration.Save();
                }

                if (_configuration.ShowTransferWindow)
                {
                    ImGui.Indent();
                    bool editTransferWindowPosition = _uiShared.EditTrackerPosition;
                    if (ImGui.Checkbox("Edit Transfer Window position", ref editTransferWindowPosition))
                    {
                        _uiShared.EditTrackerPosition = editTransferWindowPosition;
                    }
                    ImGui.Unindent();
                }

                if (ImGui.BeginTable("TransfersTable", 2))
                {
                    ImGui.TableSetupColumn(
                        $"Uploads ({UiShared.ByteToString(_apiController.CurrentUploads.Sum(a => a.Transferred))} / {UiShared.ByteToString(_apiController.CurrentUploads.Sum(a => a.Total))})");
                    ImGui.TableSetupColumn($"Downloads ({UiShared.ByteToString(_apiController.CurrentDownloads.Sum(a => a.Transferred))} / {UiShared.ByteToString(_apiController.CurrentDownloads.Sum(a => a.Total))})");

                    ImGui.TableHeadersRow();

                    ImGui.TableNextColumn();
                    if (ImGui.BeginTable("UploadsTable", 3))
                    {
                        ImGui.TableSetupColumn("File");
                        ImGui.TableSetupColumn("Uploaded");
                        ImGui.TableSetupColumn("Size");
                        ImGui.TableHeadersRow();
                        foreach (var transfer in _apiController.CurrentUploads.ToArray())
                        {
                            var color = UiShared.UploadColor((transfer.Transferred, transfer.Total));
                            ImGui.PushStyleColor(ImGuiCol.Text, color);
                            ImGui.TableNextColumn();
                            ImGui.Text(transfer.Hash);
                            ImGui.TableNextColumn();
                            ImGui.Text(UiShared.ByteToString(transfer.Transferred));
                            ImGui.TableNextColumn();
                            ImGui.Text(UiShared.ByteToString(transfer.Total));
                            ImGui.PopStyleColor();
                            ImGui.TableNextRow();
                        }

                        ImGui.EndTable();
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.BeginTable("DownloadsTable", 3))
                    {
                        ImGui.TableSetupColumn("File");
                        ImGui.TableSetupColumn("Downloaded");
                        ImGui.TableSetupColumn("Size");
                        ImGui.TableHeadersRow();
                        foreach (var transfer in _apiController.CurrentDownloads.ToArray())
                        {
                            var color = UiShared.UploadColor((transfer.Transferred, transfer.Total));
                            ImGui.PushStyleColor(ImGuiCol.Text, color);
                            ImGui.TableNextColumn();
                            ImGui.Text(transfer.Hash);
                            ImGui.TableNextColumn();
                            ImGui.Text(UiShared.ByteToString(transfer.Transferred));
                            ImGui.TableNextColumn();
                            ImGui.Text(UiShared.ByteToString(transfer.Total));
                            ImGui.PopStyleColor();
                            ImGui.TableNextRow();
                        }

                        ImGui.EndTable();
                    }

                    ImGui.EndTable();
                }

                ImGui.TreePop();
            }
        }

        private void DrawFileCacheSettings()
        {
            if (ImGui.TreeNode("File Cache Settings"))
            {
                _uiShared.DrawFileScanState();
                _uiShared.DrawCacheDirectorySetting();
                ImGui.Text($"Local cache size: {UiShared.ByteToString(_uiShared.FileCacheSize)}");
                ImGui.SameLine();
                if (ImGui.Button("Clear local cache"))
                {
                    Task.Run(() =>
                    {
                        foreach (var file in Directory.GetFiles(_configuration.CacheFolder))
                        {
                            File.Delete(file);
                        }
                    });
                }
                ImGui.TreePop();
            }
        }

        public override void OnClose()
        {
            _uiShared.EditTrackerPosition = false;
            base.OnClose();
        }

        private void DrawPairedClientsContent()
        {
            if (!_apiController.ServerAlive) return;
            if (ImGui.TreeNode("Pairing Configuration"))
            {
                if (ImGui.BeginTable("PairedClientsTable", 5))
                {
                    ImGui.TableSetupColumn("Pause", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.WidthFixed, 110);
                    ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 140);
                    ImGui.TableSetupColumn("Comment", ImGuiTableColumnFlags.WidthFixed, 400);
                    ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 70);

                    ImGui.TableHeadersRow();
                    foreach (var item in _apiController.PairedClients.ToList())
                    {
                        ImGui.TableNextColumn();
                        ImGui.PushFont(UiBuilder.IconFont);
                        string pauseIcon = item.IsPaused ? FontAwesomeIcon.Play.ToIconString() : FontAwesomeIcon.Pause.ToIconString();
                        if (ImGui.Button(pauseIcon + "##paused" + item.OtherUID))
                        {
                            _ = _apiController.SendPairedClientPauseChange(item.OtherUID, !item.IsPaused);
                        }
                        ImGui.PopFont();

                        ImGui.TableNextColumn();
                        ImGui.TextColored(
                            UiShared.GetBoolColor(item.IsSynced && !item.IsPausedFromOthers && !item.IsPaused),
                            item.OtherUID);
                        ImGui.TableNextColumn();
                        string pairString = !item.IsSynced
                            ? "Has not added you"
                            : ((item.IsPaused || item.IsPausedFromOthers) ? "Unpaired" : "Paired");
                        ImGui.TextColored(UiShared.GetBoolColor(item.IsSynced && !item.IsPaused && !item.IsPausedFromOthers), pairString);
                        ImGui.TableNextColumn();
                        string charComment = _configuration.GetCurrentServerUidComments().ContainsKey(item.OtherUID) ? _configuration.GetCurrentServerUidComments()[item.OtherUID] : string.Empty;
                        ImGui.SetNextItemWidth(400);
                        if (ImGui.InputTextWithHint("##comment" + item.OtherUID, "Add your comment here (comments will not be synced)", ref charComment, 255))
                        {
                            if (_configuration.GetCurrentServerUidComments().Count == 0)
                            {
                                _configuration.UidServerComments[_configuration.ApiUri] =
                                    new Dictionary<string, string>();
                            }
                            _configuration.SetCurrentServerUidComment(item.OtherUID, charComment);
                            _configuration.Save();
                        }
                        ImGui.TableNextColumn();
                        ImGui.PushFont(UiBuilder.IconFont);
                        if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString() + "##" + item.OtherUID))
                        {
                            _ = _apiController.SendPairedClientRemoval(item.OtherUID);
                            _apiController.PairedClients.Remove(item);
                        }
                        ImGui.PopFont();

                        ImGui.TableNextRow();
                    }

                    ImGui.EndTable();
                }

                var pairedClientEntry = _tempNameUID;
                ImGui.SetNextItemWidth(200);
                if (ImGui.InputText("UID", ref pairedClientEntry, 20))
                {
                    _tempNameUID = pairedClientEntry;
                }

                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString() + "##addToPairedClients"))
                {
                    if (_apiController.PairedClients.All(w => w.OtherUID != _tempNameUID))
                    {
                        var nameToSend = _tempNameUID;
                        _tempNameUID = string.Empty;
                        _ = _apiController.SendPairedClientAddition(nameToSend);
                    }
                }
                ImGui.PopFont();

                ImGui.TreePop();
            }
        }

        private string _tempNameUID = string.Empty;
    }
}
