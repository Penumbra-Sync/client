using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.WebAPI;
using System;
using System.Linq;
using MareSynchronos.Managers;
using MareSynchronos.Utils;

namespace MareSynchronos.UI
{
    class PluginUi : Window, IDisposable
    {
        private readonly Configuration _configuration;
        private readonly WindowSystem _windowSystem;
        private readonly ApiController _apiController;
        private readonly UIShared _uiShared;

        public PluginUi(WindowSystem windowSystem,
            UIShared uiShared, Configuration configuration, ApiController apiController) : base("Mare Synchronos Settings", ImGuiWindowFlags.None)
        {
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
            }
            else
            {
                ImGui.TextColored(ImGuiColors.DalamudRed, "No UID (Service unavailable)");
                ImGui.SetWindowFontScale(1.0f);
            }
        }

        private void DrawCurrentTransfers()
        {
            if (ImGui.TreeNode(
                    $"Current Transfers"))
            {
                if (ImGui.BeginTable("TransfersTable", 2))
                {
                    ImGui.TableSetupColumn(
                        $"Uploads ({UIShared.ByteToKiB(_apiController.CurrentUploads.Sum(a => a.Value.Item1))} / {UIShared.ByteToKiB(_apiController.CurrentUploads.Sum(a => a.Value.Item2))})");
                    ImGui.TableSetupColumn($"Downloads ({UIShared.ByteToKiB(_apiController.CurrentDownloads.Sum(a => a.Value.Item1))} / {UIShared.ByteToKiB(_apiController.CurrentDownloads.Sum(a => a.Value.Item2))})");

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
                            var color = UIShared.UploadColor(_apiController.CurrentUploads[hash]);
                            ImGui.PushStyleColor(ImGuiCol.Text, color);
                            ImGui.TableNextColumn();
                            ImGui.Text(hash);
                            ImGui.TableNextColumn();
                            ImGui.Text(UIShared.ByteToKiB(_apiController.CurrentUploads[hash].Item1));
                            ImGui.TableNextColumn();
                            ImGui.Text(UIShared.ByteToKiB(_apiController.CurrentUploads[hash].Item2));
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
                            var color = UIShared.UploadColor(_apiController.CurrentDownloads[hash]);
                            ImGui.PushStyleColor(ImGuiCol.Text, color);
                            ImGui.TableNextColumn();
                            ImGui.Text(hash);
                            ImGui.TableNextColumn();
                            ImGui.Text(UIShared.ByteToKiB(_apiController.CurrentDownloads[hash].Item1));
                            ImGui.TableNextColumn();
                            ImGui.Text(UIShared.ByteToKiB(_apiController.CurrentDownloads[hash].Item2));
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
                _uiShared.DrawParallelScansSetting();
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
                            UIShared.GetBoolColor(item.IsSynced && !item.IsPausedFromOthers && !item.IsPaused),
                            item.OtherUID);
                        ImGui.TableNextColumn();
                        string pairString = !item.IsSynced
                            ? "Has not added you"
                            : ((item.IsPaused || item.IsPausedFromOthers) ? "Unpaired" : "Paired");
                        ImGui.TextColored(UIShared.GetBoolColor(item.IsSynced && !item.IsPaused && !item.IsPausedFromOthers), pairString);
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
