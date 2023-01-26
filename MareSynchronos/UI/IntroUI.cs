using System.Numerics;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.Utils;
using MareSynchronos.Localization;
using Dalamud.Utility;
using MareSynchronos.FileCache;

namespace MareSynchronos.UI;

internal class IntroUi : Window, IDisposable
{
    private readonly UiShared _uiShared;
    private readonly Configuration _pluginConfiguration;
    private readonly PeriodicFileScanner _fileCacheManager;
    private readonly WindowSystem _windowSystem;
    private bool _readFirstPage;

    public event SwitchUi? SwitchToMainUi;

    private string[] TosParagraphs;

    private Task _timeoutTask;
    private string _timeoutLabel;

    private Dictionary<string, string> _languages = new(StringComparer.Ordinal) { { "English", "en" }, { "Deutsch", "de" }, { "Français", "fr" } };
    private int _currentLanguage;

    public void Dispose()
    {
        Logger.Verbose("Disposing " + nameof(IntroUi));

        _windowSystem.RemoveWindow(this);
    }

    public IntroUi(WindowSystem windowSystem, UiShared uiShared, Configuration pluginConfiguration,
        PeriodicFileScanner fileCacheManager) : base("Mare Synchronos Setup")
    {
        Logger.Verbose("Creating " + nameof(IntroUi));

        _uiShared = uiShared;
        _pluginConfiguration = pluginConfiguration;
        _fileCacheManager = fileCacheManager;
        _windowSystem = windowSystem;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(600, 400),
            MaximumSize = new Vector2(600, 2000)
        };

        GetToSLocalization();

        _windowSystem.AddWindow(this);
    }

    public override void Draw()
    {
        if (_uiShared.IsInGpose) return;

        if (!_pluginConfiguration.AcceptedAgreement && !_readFirstPage)
        {
            if (_uiShared.UidFontBuilt) ImGui.PushFont(_uiShared.UidFont);
            ImGui.TextUnformatted("Welcome to Mare Synchronos");
            if (_uiShared.UidFontBuilt) ImGui.PopFont();
            ImGui.Separator();
            UiShared.TextWrapped("Mare Synchronos is a plugin that will replicate your full current character state including all Penumbra mods to other paired Mare Synchronos users. " +
                              "Note that you will have to have Penumbra as well as Glamourer installed to use this plugin.");
            UiShared.TextWrapped("We will have to setup a few things first before you can start using this plugin. Click on next to continue.");

            UiShared.ColorTextWrapped("Note: Any modifications you have applied through anything but Penumbra cannot be shared and your character state on other clients " +
                                 "might look broken because of this or others players mods might not apply on your end altogether. " +
                                 "If you want to use this plugin you will have to move your mods to Penumbra.", ImGuiColors.DalamudYellow);
            if (!_uiShared.DrawOtherPluginState()) return;
            ImGui.Separator();
            if (ImGui.Button("Next##toAgreement"))
            {
                _readFirstPage = true;
                _timeoutTask = Task.Run(async () =>
                {
                    for (int i = 60; i > 0; i--)
                    {
                        _timeoutLabel = $"{Strings.ToS.ButtonWillBeAvailableIn} {i}s";
                        Logger.Debug(_timeoutLabel);
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    }
                });
            }
        }
        else if (!_pluginConfiguration.AcceptedAgreement && _readFirstPage)
        {
            if (_uiShared.UidFontBuilt) ImGui.PushFont(_uiShared.UidFont);
            var textSize = ImGui.CalcTextSize(Strings.ToS.LanguageLabel);
            ImGui.TextUnformatted(Strings.ToS.AgreementLabel);
            if (_uiShared.UidFontBuilt) ImGui.PopFont();

            ImGui.SameLine();
            var languageSize = ImGui.CalcTextSize(Strings.ToS.LanguageLabel);
            ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X - languageSize.X - 80);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + textSize.Y / 2 - languageSize.Y / 2);

            ImGui.TextUnformatted(Strings.ToS.LanguageLabel);
            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + textSize.Y / 2 - (languageSize.Y + ImGui.GetStyle().FramePadding.Y) / 2);
            ImGui.SetNextItemWidth(80);
            if (ImGui.Combo("", ref _currentLanguage, _languages.Keys.ToArray(), _languages.Count))
            {
                GetToSLocalization(_currentLanguage);
            }

            ImGui.Separator();
            ImGui.SetWindowFontScale(1.5f);
            string readThis = Strings.ToS.ReadLabel;
            textSize = ImGui.CalcTextSize(readThis);
            ImGui.SetCursorPosX(ImGui.GetWindowSize().X / 2 - textSize.X / 2);
            UiShared.ColorText(readThis, ImGuiColors.DalamudRed);
            ImGui.SetWindowFontScale(1.0f);
            ImGui.Separator();


            UiShared.TextWrapped(TosParagraphs[0]);
            UiShared.TextWrapped(TosParagraphs[1]);
            UiShared.TextWrapped(TosParagraphs[2]);
            UiShared.TextWrapped(TosParagraphs[3]);
            UiShared.TextWrapped(TosParagraphs[4]);
            UiShared.TextWrapped(TosParagraphs[5]);

            ImGui.Separator();
            if (_timeoutTask?.IsCompleted ?? true)
            {
                if (ImGui.Button(Strings.ToS.AgreeLabel + "##toSetup"))
                {
                    _pluginConfiguration.AcceptedAgreement = true;
                    _pluginConfiguration.Save();
                }
            }
            else
            {
                UiShared.TextWrapped(_timeoutLabel);
            }
        }
        else if (_pluginConfiguration.AcceptedAgreement
                 && (string.IsNullOrEmpty(_pluginConfiguration.CacheFolder)
                     || _pluginConfiguration.InitialScanComplete == false
                     || !Directory.Exists(_pluginConfiguration.CacheFolder)))
        {
            if (_uiShared.UidFontBuilt) ImGui.PushFont(_uiShared.UidFont);
            ImGui.TextUnformatted("File Storage Setup");
            if (_uiShared.UidFontBuilt) ImGui.PopFont();
            ImGui.Separator();

            if (!_uiShared.HasValidPenumbraModPath)
            {
                UiShared.ColorTextWrapped("You do not have a valid Penumbra path set. Open Penumbra and set up a valid path for the mod directory.", ImGuiColors.DalamudRed);
            }
            else
            {
                UiShared.TextWrapped("To not unnecessary download files already present on your computer, Mare Synchronos will have to scan your Penumbra mod directory. " +
                                     "Additionally, a local storage folder must be set where Mare Synchronos will download other character files to. " +
                                     "Once the storage folder is set and the scan complete, this page will automatically forward to registration at a service.");
                UiShared.TextWrapped("Note: The initial scan, depending on the amount of mods you have, might take a while. Please wait until it is completed.");
                UiShared.ColorTextWrapped("Warning: once past this step you should not delete the FileCache.csv of Mare Synchronos in the Plugin Configurations folder of Dalamud. " +
                                          "Otherwise on the next launch a full re-scan of the file cache database will be initiated.", ImGuiColors.DalamudYellow);
                UiShared.ColorTextWrapped("Warning: if the scan is hanging and does nothing for a long time, chances are high your Penumbra folder is not set up properly.", ImGuiColors.DalamudYellow);
                _uiShared.DrawCacheDirectorySetting();
            }

            if (!_fileCacheManager.IsScanRunning && !string.IsNullOrEmpty(_pluginConfiguration.CacheFolder) && _uiShared.HasValidPenumbraModPath && Directory.Exists(_pluginConfiguration.CacheFolder))
            {
                if (ImGui.Button("Start Scan##startScan"))
                {
                    _fileCacheManager.InvokeScan(true);
                }
            }
            else
            {
                _uiShared.DrawFileScanState();
            }
        }
        else if (!_uiShared.ApiController.ServerAlive)
        {
            if (_uiShared.UidFontBuilt) ImGui.PushFont(_uiShared.UidFont);
            ImGui.TextUnformatted("Service Registration");
            if (_uiShared.UidFontBuilt) ImGui.PopFont();
            ImGui.Separator();
            UiShared.TextWrapped("To be able to use Mare Synchronos you will have to register an account.");
            UiShared.TextWrapped("For the official Mare Synchronos Servers the account creation will be handled on the official Mare Synchronos Discord. Due to security risks for the server, there is no way to handle this senisibly otherwise.");
            UiShared.TextWrapped("If you want to register at the main server \"" + WebAPI.ApiController.MainServer + "\" join the Discord and follow the instructions as described in #mare-commands.");

            if (ImGui.Button("Join the Mare Synchronos Discord"))
            {
                Util.OpenLink("https://discord.gg/mpNdkrTRjW");
            }

            UiShared.TextWrapped("For all other non official services you will have to contact the appropriate service provider how to obtain a secret key.");

            ImGui.Separator();

            UiShared.TextWrapped("Once you have received a secret key you can connect to the service using the tools provided below.");

            _uiShared.DrawServiceSelection(() => { });
        }
        else
        {
            SwitchToMainUi?.Invoke();
            IsOpen = false;
        }
    }

    private string _secretKey = string.Empty;

    private void GetToSLocalization(int changeLanguageTo = -1)
    {
        if (changeLanguageTo != -1)
        {
            _uiShared.LoadLocalization(_languages.ElementAt(changeLanguageTo).Value);
        }

        TosParagraphs = new[] { Strings.ToS.Paragraph1, Strings.ToS.Paragraph2, Strings.ToS.Paragraph3, Strings.ToS.Paragraph4, Strings.ToS.Paragraph5, Strings.ToS.Paragraph6 };
    }
}
