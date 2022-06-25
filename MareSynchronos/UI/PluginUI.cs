using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.WebAPI;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using MareSynchronos.Utils;

namespace MareSynchronos.UI
{
    public class PluginUi : Window, IDisposable
    {
        private readonly Configuration _configuration;
        private readonly WindowSystem _windowSystem;
        private readonly ApiController _apiController;
        private readonly UiShared _uiShared;

        public PluginUi(WindowSystem windowSystem,
            UiShared uiShared, Configuration configuration, ApiController apiController) : base("Mare Synchronos Settings", ImGuiWindowFlags.None)
        {
            Logger.Debug("Creating " + nameof(PluginUi));

            SizeConstraints = new WindowSizeConstraints()
            {
                MinimumSize = new(800, 400),
                MaximumSize = new(800, 2000),
            };

            _configuration = configuration;
            _windowSystem = windowSystem;
            _apiController = apiController;
            _uiShared = uiShared;
            windowSystem.AddWindow(this);
        }

        public void Dispose()
        {
            Logger.Debug("Disposing " + nameof(PluginUi));

            _windowSystem.RemoveWindow(this);
        }

        public override void Draw()
        {
            if (!IsOpen)
            {
                return;
            }

            if (_apiController.SecretKey != "-" && !_apiController.IsConnected && _apiController.ServerAlive)
            {
                if (ImGui.Button("Reset Secret Key"))
                {
                    _configuration.ClientSecret.Clear();
                    _configuration.Save();
                    _apiController.RestartHeartbeat();
                }
            }
            else
            {
                if (!_uiShared.DrawOtherPluginState()) return;

                DrawSettingsContent();
            }
        }

        private void DrawSettingsContent()
        {
            _uiShared.PrintServerState();
            ImGui.Separator();
            ImGui.SetWindowFontScale(1.2f);
            ImGui.Text("Your UID");
            ImGui.SameLine();
            if (_apiController.ServerAlive)
            {
                ImGui.TextColored(ImGuiColors.ParsedGreen, _apiController.UID);
                ImGui.SameLine();
                ImGui.SetWindowFontScale(1.0f);
                if (ImGui.Button("Copy UID"))
                {
                    ImGui.SetClipboardText(_apiController.UID);
                }
                ImGui.Text("Share this UID to other Mare users so they pair their client with yours.");
                ImGui.Separator();
                DrawPairedClientsContent();
                DrawFileCacheSettings();
                DrawCurrentTransfers();
                DrawAdministration();
            }
            else
            {
                ImGui.TextColored(ImGuiColors.DalamudRed, "No UID (Service unavailable)");
                ImGui.SetWindowFontScale(1.0f);
            }
        }

        private bool _deleteFilesPopupModalShown = false;
        private bool _deleteAccountPopupModalShown = false;

        private void DrawAdministration()
        {
            if (ImGui.TreeNode(
                    $"User Administration"))
            {
                if (ImGui.Button("Delete all my files"))
                {
                    _deleteFilesPopupModalShown = true;
                    ImGui.OpenPopup("Delete all your files?");
                }

                if (ImGui.BeginPopupModal("Delete all your files?", ref _deleteFilesPopupModalShown, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    UiShared.TextWrapped("All your own uploaded files on the service will be deleted.\nThis operation cannot be undone.");
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

                if (ImGui.BeginPopupModal("Delete your account?", ref _deleteAccountPopupModalShown, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    UiShared.TextWrapped("Your account and all associated files and data on the service will be deleted.");
                    UiShared.TextWrapped("Your UID will be removed from all pairing lists.");
                    ImGui.Text("Are you sure you want to continue?");
                    ImGui.Separator();
                    if (ImGui.Button("Delete account", new Vector2(150, 0)))
                    {
                        Task.Run(() => _apiController.DeleteAccount());
                        ImGui.CloseCurrentPopup();
                        _deleteAccountPopupModalShown = false;
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("Cancel##cancelDelete", new Vector2(150, 0)))
                    {
                        ImGui.CloseCurrentPopup();
                        _deleteAccountPopupModalShown = false;
                    }

                    ImGui.EndPopup();
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

                if (ImGui.BeginTable("TransfersTable", 2))
                {
                    ImGui.TableSetupColumn(
                        $"Uploads ({UiShared.ByteToString(_apiController.CurrentUploads.Sum(a => a.Value.Item1))} / {UiShared.ByteToString(_apiController.CurrentUploads.Sum(a => a.Value.Item2))})");
                    ImGui.TableSetupColumn($"Downloads ({UiShared.ByteToString(_apiController.CurrentDownloads.Sum(a => a.Value.Item1))} / {UiShared.ByteToString(_apiController.CurrentDownloads.Sum(a => a.Value.Item2))})");

                    ImGui.TableHeadersRow();

                    ImGui.TableNextColumn();
                    if (ImGui.BeginTable("UploadsTable", 3))
                    {
                        ImGui.TableSetupColumn("File");
                        ImGui.TableSetupColumn("Uploaded");
                        ImGui.TableSetupColumn("Size");
                        ImGui.TableHeadersRow();
                        foreach (var hash in _apiController.CurrentUploads.Keys)
                        {
                            var color = UiShared.UploadColor(_apiController.CurrentUploads[hash]);
                            ImGui.PushStyleColor(ImGuiCol.Text, color);
                            ImGui.TableNextColumn();
                            ImGui.Text(hash);
                            ImGui.TableNextColumn();
                            ImGui.Text(UiShared.ByteToString(_apiController.CurrentUploads[hash].Item1));
                            ImGui.TableNextColumn();
                            ImGui.Text(UiShared.ByteToString(_apiController.CurrentUploads[hash].Item2));
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
                        foreach (var hash in _apiController.CurrentDownloads.Keys)
                        {
                            var color = UiShared.UploadColor(_apiController.CurrentDownloads[hash]);
                            ImGui.PushStyleColor(ImGuiCol.Text, color);
                            ImGui.TableNextColumn();
                            ImGui.Text(hash);
                            ImGui.TableNextColumn();
                            ImGui.Text(UiShared.ByteToString(_apiController.CurrentDownloads[hash].Item1));
                            ImGui.TableNextColumn();
                            ImGui.Text(UiShared.ByteToString(_apiController.CurrentDownloads[hash].Item2));
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
                        string charComment = _configuration.UidComments.ContainsKey(item.OtherUID) ? _configuration.UidComments[item.OtherUID] : string.Empty;
                        ImGui.SetNextItemWidth(400);
                        if (ImGui.InputTextWithHint("##comment" + item.OtherUID, "Add your comment here (comments will not be synced)", ref charComment, 255))
                        {
                            _configuration.UidComments[item.OtherUID] = charComment;
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

                var pairedClientEntry = tempNameUID;
                ImGui.SetNextItemWidth(200);
                if (ImGui.InputText("UID", ref pairedClientEntry, 20))
                {
                    tempNameUID = pairedClientEntry;
                }

                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString() + "##addToPairedClients"))
                {
                    if (_apiController.PairedClients.All(w => w.OtherUID != tempNameUID))
                    {
                        var nameToSend = tempNameUID;
                        tempNameUID = string.Empty;
                        _ = _apiController.SendPairedClientAddition(nameToSend);
                    }
                }
                ImGui.PopFont();

                ImGui.TreePop();
            }
        }

        private string tempNameUID = string.Empty;
    }
}
