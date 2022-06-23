using System;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.Managers;
using MareSynchronos.Utils;

namespace MareSynchronos.UI
{
    internal class IntroUI : Window, IDisposable
    {
        private readonly UIShared _uiShared;
        private readonly Configuration _pluginConfiguration;
        private readonly FileCacheManager _fileCacheManager;
        private readonly WindowSystem _windowSystem;
        private bool _readFirstPage = false;

        public event EventHandler? FinishedRegistration;

        public void Dispose()
        {
            Logger.Debug("Disposing " + nameof(IntroUI));

            _windowSystem.RemoveWindow(this);
        }

        public IntroUI(WindowSystem windowSystem, UIShared uiShared, Configuration pluginConfiguration,
            FileCacheManager fileCacheManager) : base("Mare Synchronos Setup")
        {
            _uiShared = uiShared;
            _pluginConfiguration = pluginConfiguration;
            _fileCacheManager = fileCacheManager;
            _windowSystem = windowSystem;

            SizeConstraints = new WindowSizeConstraints()
            {
                MinimumSize = new(600, 400),
                MaximumSize = new(600, 2000)
            };

            _windowSystem.AddWindow(this);
        }

        public override void Draw()
        {
            if (!IsOpen)
            {
                return;
            }

            if (!_pluginConfiguration.AcceptedAgreement && !_readFirstPage)
            {
                ImGui.SetWindowFontScale(1.3f);
                ImGui.Text("Welcome to Mare Synchronos!");
                ImGui.SetWindowFontScale(1.0f);
                ImGui.Separator();
                UIShared.TextWrapped("Mare Synchronos is a plugin that will replicate your full current character state including all Penumbra mods to other paired Mare Synchronos users. " +
                                  "Note that you will have to have Penumbra as well as Glamourer installed to use this plugin.");
                UIShared.TextWrapped("We will have to setup a few things first before you can start using this plugin. Click on next to continue.");

                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                UIShared.TextWrapped("Note: Any modifications you have applied through anything but Penumbra cannot be shared and your character state on other clients " +
                                     "might look broken because of this. If you want to use this plugin you will have to move your mods to Penumbra.");
                ImGui.PopStyleColor();
                if (!_uiShared.DrawOtherPluginState()) return;
                ImGui.Separator();
                if (ImGui.Button("Next##toAgreement"))
                {
                    _readFirstPage = true;
                }
            }
            else if (!_pluginConfiguration.AcceptedAgreement && _readFirstPage)
            {
                ImGui.SetWindowFontScale(1.3f);
                ImGui.Text("Agreement of Usage of Service");
                ImGui.SetWindowFontScale(1.0f);
                ImGui.Separator();
                ImGui.SetWindowFontScale(1.5f);
                string readThis = "READ THIS CAREFULLY";
                var textSize = ImGui.CalcTextSize(readThis);
                ImGui.SetCursorPosX(ImGui.GetWindowSize().X / 2 - textSize.X / 2);
                ImGui.TextColored(ImGuiColors.DalamudRed, readThis);
                ImGui.SetWindowFontScale(1.0f);
                ImGui.Separator();
                UIShared.TextWrapped("All of the mod files currently active on your character as well as your current character state will be uploaded to the service you registered yourself at automatically. " +
                    "The plugin will exclusively upload the necessary mod files and not the whole mod.");
                UIShared.TextWrapped("If you are on a data capped internet connection, higher fees due to data usage depending on the amount of downloaded and uploaded mod files might occur. " +
                    "Mod files will be compressed on up- and download to save on bandwidth usage. Due to varying up- and download speeds, changes in characters might not be visible immediately. " +
                    "Files present on the service that already represent your active mod files will not be uploaded again.");
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                UIShared.TextWrapped("The mod files you are uploading are confidential and will not be distributed to parties other than the ones who are requesting the exact same mod files. " +
                    "Please think about who you are going to pair since it is unavoidable that they will receive and locally cache the necessary mod files that you have currently in use. " +
                    "Locally cached mod files will have arbitrary file names to discourage attempts at replicating the original mod.");
                ImGui.PopStyleColor();
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                UIShared.TextWrapped("The plugin creator tried their best to keep you secure. However, there is no guarantee for 100% security. Do not blindly pair your client with everyone.");
                ImGui.PopStyleColor();
                UIShared.TextWrapped("Mod files that are saved on the service will remain on the service as long as there are requests for the files from clients. " +
                                  "After a period of not being used, the mod files will be automatically deleted. " +
                                  "You will also be able to wipe all the files you have personally uploaded on request. " +
                                  "The service holds no information about which mod files belong to which mod.");
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                UIShared.TextWrapped("This service is provided as-is. In case of abuse, contact darkarchon#4313 on Discord or join the Mare Synchronos Discord. " +
                                                          "To accept those conditions hold CTRL while clicking 'I agree'");
                ImGui.PopStyleColor();
                ImGui.Separator();

                if (ImGui.Button("I agree##toSetup"))
                {
                    if (ImGui.GetIO().KeyCtrl)
                    {
                        _pluginConfiguration.AcceptedAgreement = true;
                        _pluginConfiguration.Save();
                    }
                }
            }
            else if (_pluginConfiguration.AcceptedAgreement && (string.IsNullOrEmpty(_pluginConfiguration.CacheFolder) || _pluginConfiguration.InitialScanComplete == false))
            {
                ImGui.SetWindowFontScale(1.3f);
                ImGui.Text("File Cache Setup");
                ImGui.SetWindowFontScale(1.0f);
                ImGui.Separator();
                UIShared.TextWrapped("To not unnecessary download files already present on your computer, Mare Synchronos will have to scan your Penumbra mod directory. " +
                                  "Additionally, a local cache folder must be set where Mare Synchronos will download its local file cache to. " +
                                  "Once the Cache Folder is set and the scan complete, this page will automatically forward to registration at a service.");
                UIShared.TextWrapped("Note: The initial scan, depending on the amount of mods you have, might take a while. Please wait until it is completed.");
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                UIShared.TextWrapped("Warning: once past this step you should not delete the FileCache.db of Mare Synchronos in the Plugin Configurations folder of Dalamud. " +
                                  "Otherwise on the next launch a full re-scan of the file cache database will be initiated.");
                ImGui.PopStyleColor();
                _uiShared.DrawCacheDirectorySetting();


                if (!_fileCacheManager.IsScanRunning)
                {
                    UIShared.TextWrapped("You can adjust how many parallel threads will be used for scanning. Mind that ultimately it will depend on the amount of mods, your disk speed and your CPU. " +
                                      "More is not necessarily better, the default of 10 should be fine for most cases.");
                    _uiShared.DrawParallelScansSetting();


                    if (ImGui.Button("Start Scan##startScan"))
                    {
                        _fileCacheManager.StartInitialScan();
                    }
                }
                else
                {
                    _uiShared.DrawFileScanState();
                }
            }
            else
            {
                ImGui.SetWindowFontScale(1.3f);
                ImGui.Text("Service registration");
                ImGui.SetWindowFontScale(1.0f);
                ImGui.Separator();
                if (_pluginConfiguration.ClientSecret.ContainsKey(_pluginConfiguration.ApiUri))
                {
                    ImGui.Separator();
                    UIShared.TextWrapped(_pluginConfiguration.ClientSecret[_pluginConfiguration.ApiUri]);
                    ImGui.Separator();
                    if (ImGui.Button("Copy secret key to clipboard"))
                    {
                        ImGui.SetClipboardText(_pluginConfiguration.ClientSecret[_pluginConfiguration.ApiUri]);
                    }
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                    UIShared.TextWrapped("This is the only time you will be able to see this key in the UI. You can copy it to make a backup somewhere.");
                    ImGui.PopStyleColor();
                    ImGui.Separator();
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.ParsedGreen);
                    UIShared.TextWrapped("You are now ready to go. Press Finish to finalize the settings and open the Mare Synchronos main UI.");
                    ImGui.PopStyleColor();
                    ImGui.Separator();
                    if (ImGui.Button("Finish##finishIntro"))
                    {
                        FinishedRegistration?.Invoke(null, EventArgs.Empty);
                        IsOpen = false;
                    }
                }
                else
                {
                    UIShared.TextWrapped("You will now have to register at a service. You can use the provided central service or pick a custom one. " +
                                         "There is no support for custom services from the plugin creator. Use at your own risk.");
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                    UIShared.TextWrapped("On registration on a service the plugin will create and save a secret key to your plugin configuration. " +
                                         "Make a backup of your secret key. In case of loss, it cannot be restored. The secret key is your identification to the service " +
                                         "to verify who you are. It is directly tied to the UID you will be receiving. In case of loss, you will have to re-register an account.");
                    UIShared.TextWrapped("Do not ever, under any circumstances, share your secret key to anyone! Likewise do not share your Mare Synchronos plugin configuration to anyone!");
                    ImGui.PopStyleColor();
                    _uiShared.DrawServiceSelection();
                }
            }
        }
    }
}
