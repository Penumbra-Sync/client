using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using ImGuiNET;
using MareSynchronos.Managers;
using MareSynchronos.WebAPI;
using System;
using System.Linq;
using System.Numerics;
using Dalamud.Configuration;
using MareSynchronos.API;

namespace MareSynchronos
{
    // It is good to have this be disposable in general, in case you ever need it
    // to do any cleanup
    class PluginUI : Window, IDisposable
    {
        private Configuration configuration;
        private readonly WindowSystem windowSystem;
        private readonly ApiController apiController;
        private readonly IpcManager ipcManager;
        private string? uid;
        private const string mainServer = "Lunae Crescere Incipientis (Central Server EU)";

        // passing in the image here just for simplicity
        public PluginUI(Configuration configuration, WindowSystem windowSystem, ApiController apiController, IpcManager ipcManager) : base("Mare Synchronos Settings", ImGuiWindowFlags.None)
        {
            SizeConstraints = new WindowSizeConstraints()
            {
                MinimumSize = new(700, 400),
                MaximumSize = new(700, 2000)
            };

            this.configuration = configuration;
            this.windowSystem = windowSystem;
            this.apiController = apiController;
            this.ipcManager = ipcManager;
            windowSystem.AddWindow(this);
        }

        public void Dispose()
        {
            windowSystem.RemoveWindow(this);
        }

        public override void Draw()
        {
            if (!IsOpen)
            {
                return;
            }


            if (apiController.SecretKey != "-" && !apiController.IsConnected && apiController.ServerAlive)
            {
                if (ImGui.Button("Reset Secret Key"))
                {
                    configuration.ClientSecret.Clear();
                    configuration.Save();
                    apiController.RestartHeartbeat();
                }
            }
            else if (apiController.SecretKey == "-")
            {
                DrawIntroContent();
            }
            else
            {
                if (!OtherPluginStateOk()) return;

                DrawSettingsContent();
            }
        }

        private void DrawSettingsContent()
        {
            PrintServerState();
            ImGui.Separator();
            ImGui.Text("Your UID");
            ImGui.SameLine();
            if (apiController.ServerAlive)
            {
                ImGui.TextColored(ImGuiColors.ParsedGreen, apiController.UID);
                ImGui.SameLine();
                if (ImGui.Button("Copy UID"))
                {
                    ImGui.SetClipboardText(apiController.UID);
                }
                ImGui.Text("Share this UID to other Mare users so they can add you to their whitelist.");
                ImGui.Separator();
                DrawWhiteListContent();
                ImGui.Separator();
                string cachePath = configuration.CacheFolder;
                if (ImGui.InputText("CachePath", ref cachePath, 255))
                {
                    configuration.CacheFolder = cachePath;
                    configuration.Save();
                }
            }
            else
            {
                ImGui.TextColored(ImGuiColors.DalamudRed, "Service unavailable");
            }
        }

        private void DrawWhiteListContent()
        {
            if (!apiController.ServerAlive) return;
            ImGui.Text("Whitelists");
            if (ImGui.BeginTable("WhitelistTable", 5))
            {
                ImGui.TableSetupColumn("Pause");
                ImGui.TableSetupColumn("UID");
                ImGui.TableSetupColumn("Sync");
                ImGui.TableSetupColumn("Paused (you/other)");
                ImGui.TableSetupColumn("");
                ImGui.TableHeadersRow();
                foreach (var item in apiController.WhitelistEntries.ToList())
                {
                    ImGui.TableNextColumn();
                    bool isPaused = item.IsPaused;
                    if (ImGui.Checkbox("Paused##" + item.OtherUID, ref isPaused))
                    {
                        _ = apiController.SendWhitelistPauseChange(item.OtherUID, isPaused);
                    }

                    ImGui.TableNextColumn();
                    ImGui.TextColored(GetBoolColor(item.IsSynced && !item.IsPausedFromOthers && !item.IsPaused),
                        item.OtherUID);
                    ImGui.TableNextColumn();
                    ImGui.TextColored(GetBoolColor(item.IsSynced), !item.IsSynced ? "Has not added you" : "On both whitelists");
                    ImGui.TableNextColumn();
                    ImGui.TextColored(GetBoolColor((!item.IsPausedFromOthers && !item.IsPaused)), item.IsPaused + " / " + item.IsPausedFromOthers);
                    ImGui.TableNextColumn();
                    if (ImGui.Button("Delete##" + item.OtherUID))
                    {
                        _ = apiController.SendWhitelistRemoval(item.OtherUID);
                        apiController.WhitelistEntries.Remove(item);
                    }
                    ImGui.TableNextRow();
                }
                ImGui.EndTable();
            }

            var whitelistEntry = tempDto.OtherUID;
            if (ImGui.InputText("Add new whitelist entry", ref whitelistEntry, 20))
            {
                tempDto.OtherUID = whitelistEntry;
            }

            ImGui.SameLine();
            if (ImGui.Button("Add"))
            {
                if (apiController.WhitelistEntries.All(w => w.OtherUID != tempDto.OtherUID))
                {
                    apiController.WhitelistEntries.Add(new WhitelistDto()
                    {
                        OtherUID = tempDto.OtherUID
                    });

                    _ = apiController.SendWhitelistAddition(tempDto.OtherUID);

                    tempDto.OtherUID = string.Empty;
                }
            }
        }

        private WhitelistDto tempDto = new WhitelistDto() { OtherUID = string.Empty };

        private int serverSelectionIndex = 0;

        private async void DrawIntroContent()
        {
            ImGui.SetWindowFontScale(1.3f);
            ImGui.Text("Welcome to Mare Synchronos!");
            ImGui.SetWindowFontScale(1.0f);
            ImGui.Separator();
            ImGui.TextWrapped("Mare Synchronos is a plugin that will replicate your full current character state including all Penumbra mods to other whitelisted Mare Synchronos users. " +
                "Note that you will have to have Penumbra as well as Glamourer installed to use this plugin.");
            if (!OtherPluginStateOk()) return;

            ImGui.SetWindowFontScale(1.5f);
            string readThis = "READ THIS CAREFULLY BEFORE REGISTERING";
            var textSize = ImGui.CalcTextSize(readThis);
            ImGui.SetCursorPosX(ImGui.GetWindowSize().X / 2 - textSize.X / 2);
            ImGui.TextColored(ImGuiColors.DalamudRed, readThis);
            ImGui.SetWindowFontScale(1.0f);

            ImGui.TextWrapped("All of the mod files currently active on your character as well as your current character state will be uploaded to the service you registered yourself at automatically. " +
                "The plugin will exclusively upload the necessary mod files and not the whole mod.");
            ImGui.TextWrapped("If you are on a data capped internet connection, higher fees due to data usage depending on the amount of downloaded and uploaded mod files might occur. " +
                "Mod files will be compressed on up- and download to save on bandwidth usage. Due to varying up- and download speeds, changes in characters might not be visible immediately. " +
                "Files present on the service that already represent your active mod files will not be uploaded again. To register at a service you will need to hold ctrl.");
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            ImGui.TextWrapped("The mod files you are uploading are confidential and will not be distributed to parties other than the ones who are requesting the exact same mod files. " +
                "Please think about who you are going to whitelist since it is unavoidable that they will receive and locally cache the necessary mod files that you have currently in use. " +
                "Locally cached mod files will have arbitrary file names to discourage attempts at replicating the original mod in question.");
            ImGui.PopStyleColor();
            ImGui.TextWrapped("Mod files that are saved on the service will remain on the service as long as there are requests for the files from clients. After a period of not being used, the mod files will be automatically deleted. " +
                "You will also be able to wipe all the files you have personally uploaded on request.");
            ImGui.TextColored(ImGuiColors.DalamudRed, "This service is provided as-is.");
            ImGui.Separator();
            string[] comboEntries = new[] { mainServer, "Custom Service" };
            if (ImGui.BeginCombo("Service", comboEntries[serverSelectionIndex]))
            {
                for (int n = 0; n < comboEntries.Length; n++)
                {
                    bool isSelected = serverSelectionIndex == n;
                    if (ImGui.Selectable(comboEntries[n], isSelected))
                    {
                        serverSelectionIndex = n;
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }

                    bool useCustomService = (serverSelectionIndex != 0);

                    if (apiController.UseCustomService != useCustomService)
                    {
                        PluginLog.Debug("Configuration " + apiController.UseCustomService + " changing to " + useCustomService);
                        apiController.UseCustomService = useCustomService;
                        configuration.Save();
                    }
                }

                ImGui.EndCombo();
            }

            if (apiController.UseCustomService)
            {
                string serviceAddress = configuration.ApiUri;
                if (ImGui.InputText("Service address", ref serviceAddress, 255))
                {
                    if (configuration.ApiUri != serviceAddress)
                    {
                        configuration.ApiUri = serviceAddress;
                        apiController.RestartHeartbeat();
                        configuration.Save();
                    }
                }
            }

            PrintServerState();
            if (apiController.ServerAlive)
            {
                if (ImGui.Button("Register"))
                {
                    if (ImGui.GetIO().KeyCtrl)
                    {
                        await apiController.Register();
                    }
                }
            }
        }

        private Vector4 GetBoolColor(bool input) => input ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;

        private bool OtherPluginStateOk()
        {
            var penumbraExists = ipcManager.CheckPenumbraAPI();
            var glamourerExists = ipcManager.CheckGlamourerAPI();

            var penumbraColor = penumbraExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
            var glamourerColor = glamourerExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
            ImGui.Text("Penumbra:");
            ImGui.SameLine();
            ImGui.TextColored(penumbraColor, penumbraExists ? "Available" : "Unavailable");
            ImGui.Text("Glamourer:");
            ImGui.SameLine();
            ImGui.TextColored(glamourerColor, glamourerExists ? "Available" : "Unavailable");

            if (!penumbraExists || !glamourerExists)
            {
                ImGui.TextColored(ImGuiColors.DalamudRed, "You need to install both Penumbra and Glamourer and keep them up to date to use Mare Synchronos.");
                return false;
            }

            return true;
        }

        private void PrintServerState()
        {
            ImGui.Text("Service status of " + (string.IsNullOrEmpty(configuration.ApiUri) ? mainServer : configuration.ApiUri));
            ImGui.SameLine();
            var color = apiController.ServerAlive ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
            ImGui.TextColored(color, apiController.ServerAlive ? "Available" : "Unavailable");
        }
    }
}
