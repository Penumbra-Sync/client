using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.Managers;
using MareSynchronos.Utils;
using MareSynchronos.Localization;

namespace MareSynchronos.UI
{
    internal class IntroUi : Window, IDisposable
    {
        private readonly UiShared _uiShared;
        private readonly Configuration _pluginConfiguration;
        private readonly FileCacheManager _fileCacheManager;
        private readonly WindowSystem _windowSystem;
        private bool _readFirstPage;

        public event SwitchUi? SwitchToMainUi;

        private string[] TosParagraphs;

        private Tuple<string, string> _darkSoulsCaptcha1 = new(string.Empty, string.Empty);
        private Tuple<string, string> _darkSoulsCaptcha2 = new(string.Empty, string.Empty);
        private Tuple<string, string> _darkSoulsCaptcha3 = new(string.Empty, string.Empty);
        private string _enteredDarkSoulsCaptcha1 = string.Empty;
        private string _enteredDarkSoulsCaptcha2 = string.Empty;
        private string _enteredDarkSoulsCaptcha3 = string.Empty;

        private bool _failedOnce = false;
        private Task _timeoutTask;
        private string _timeoutTime;

        private Dictionary<string, string> _languages = new() { { "English", "en" }, { "Deutsch", "de" }, { "Français", "fr" } };
        private int _currentLanguage;

        private bool DarkSoulsCaptchaValid => _darkSoulsCaptcha1.Item2 == _enteredDarkSoulsCaptcha1
            && _darkSoulsCaptcha2.Item2 == _enteredDarkSoulsCaptcha2
            && _darkSoulsCaptcha3.Item2 == _enteredDarkSoulsCaptcha3;

        public void Dispose()
        {
            Logger.Verbose("Disposing " + nameof(IntroUi));

            _windowSystem.RemoveWindow(this);
        }

        public IntroUi(WindowSystem windowSystem, UiShared uiShared, Configuration pluginConfiguration,
            FileCacheManager fileCacheManager) : base("Mare Synchronos Setup")
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
                if ((!_pluginConfiguration.DarkSoulsAgreement || DarkSoulsCaptchaValid) && (_timeoutTask?.IsCompleted ?? true))
                {
                    if (ImGui.Button(Strings.ToS.AgreeLabel + "##toSetup"))
                    {
                        _enteredDarkSoulsCaptcha1 = string.Empty;
                        _enteredDarkSoulsCaptcha2 = string.Empty;
                        _enteredDarkSoulsCaptcha3 = string.Empty;

                        if (UiShared.CtrlPressed())
                        {
                            _pluginConfiguration.AcceptedAgreement = true;
                            _pluginConfiguration.Save();
                        }
                        else
                        {
                            if (!_failedOnce)
                            {
                                _failedOnce = true;
                                _timeoutTask = Task.Run(async () =>
                                {
                                    for (int i = 60; i > 0; i--)
                                    {
                                        _timeoutTime = $"{i}s " + Strings.ToS.RemainingLabel;
                                        Logger.Debug(_timeoutTime);
                                        await Task.Delay(TimeSpan.FromSeconds(1));
                                    }
                                });
                            }
                            else
                            {
                                _pluginConfiguration.DarkSoulsAgreement = true;
                                _pluginConfiguration.Save();
                                GenerateDarkSoulsAgreementCaptcha();
                            }
                        }
                    }
                }
                else
                {
                    if (_failedOnce && (!_timeoutTask?.IsCompleted ?? true))
                    {
                        UiShared.ColorTextWrapped(Strings.ToS.FailedLabel, ImGuiColors.DalamudYellow);
                        UiShared.TextWrapped(Strings.ToS.TimeoutLabel);
                        UiShared.TextWrapped(_timeoutTime);
                    }
                    else
                    {
                        UiShared.ColorTextWrapped(Strings.ToS.FailedAgainLabel, ImGuiColors.DalamudYellow);
                        UiShared.TextWrapped(Strings.ToS.PuzzleLabel);
                        UiShared.TextWrapped(Strings.ToS.PuzzleDescLabel);
                        ImGui.SetNextItemWidth(100);
                        ImGui.InputText(_darkSoulsCaptcha1.Item1, ref _enteredDarkSoulsCaptcha1, 255);
                        ImGui.SetNextItemWidth(100);
                        ImGui.InputText(_darkSoulsCaptcha2.Item1, ref _enteredDarkSoulsCaptcha2, 255);
                        ImGui.SetNextItemWidth(100);
                        ImGui.InputText(_darkSoulsCaptcha3.Item1, ref _enteredDarkSoulsCaptcha3, 255);
                    }
                }
            }
            else if (_pluginConfiguration.AcceptedAgreement
                     && (string.IsNullOrEmpty(_pluginConfiguration.CacheFolder)
                         || _pluginConfiguration.InitialScanComplete == false
                         || !Directory.Exists(_pluginConfiguration.CacheFolder)))
            {
                if (_uiShared.UidFontBuilt) ImGui.PushFont(_uiShared.UidFont);
                ImGui.TextUnformatted("File Cache Setup");
                if (_uiShared.UidFontBuilt) ImGui.PopFont();
                ImGui.Separator();

                if (!_uiShared.HasValidPenumbraModPath)
                {
                    UiShared.ColorTextWrapped("You do not have a valid Penumbra path set. Open Penumbra and set up a valid path for the mod directory.", ImGuiColors.DalamudRed);
                }
                else
                {
                    UiShared.TextWrapped("To not unnecessary download files already present on your computer, Mare Synchronos will have to scan your Penumbra mod directory. " +
                                         "Additionally, a local cache folder must be set where Mare Synchronos will download its local file cache to. " +
                                         "Once the Cache Folder is set and the scan complete, this page will automatically forward to registration at a service.");
                    UiShared.TextWrapped("Note: The initial scan, depending on the amount of mods you have, might take a while. Please wait until it is completed.");
                    UiShared.ColorTextWrapped("Warning: once past this step you should not delete the FileCache.db of Mare Synchronos in the Plugin Configurations folder of Dalamud. " +
                                              "Otherwise on the next launch a full re-scan of the file cache database will be initiated.", ImGuiColors.DalamudYellow);
                    _uiShared.DrawCacheDirectorySetting();
                }

                if (!_fileCacheManager.IsScanRunning && !string.IsNullOrEmpty(_pluginConfiguration.CacheFolder) && _uiShared.HasValidPenumbraModPath && Directory.Exists(_pluginConfiguration.CacheFolder))
                {
                    UiShared.TextWrapped("You can adjust how many parallel threads will be used for scanning. Mind that ultimately it will depend on the amount of mods, your disk speed and your CPU. " +
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
                if (_uiShared.UidFontBuilt) ImGui.PushFont(_uiShared.UidFont);
                ImGui.TextUnformatted("Service Registration");
                if (_uiShared.UidFontBuilt) ImGui.PopFont();
                ImGui.Separator();
                if (_pluginConfiguration.ClientSecret.ContainsKey(_pluginConfiguration.ApiUri) && _uiShared.ShowClientSecret)
                {
                    ImGui.SetWindowFontScale(2f);
                    UiShared.ColorTextWrapped("DO NOT GIVE THIS KEY TO OTHER PEOPLE.", ImGuiColors.DalamudYellow);
                    ImGui.SetWindowFontScale(1f);
                    ImGui.Separator();
                    UiShared.TextWrapped(_pluginConfiguration.ClientSecret[_pluginConfiguration.ApiUri]);
                    ImGui.Separator();
                    if (ImGui.Button("Copy secret key to clipboard"))
                    {
                        ImGui.SetClipboardText(_pluginConfiguration.ClientSecret[_pluginConfiguration.ApiUri]);
                    }
                    UiShared.ColorTextWrapped("This is the only time you will be able to see this key in the UI. You can copy it to make a backup somewhere.", ImGuiColors.DalamudYellow);
                    ImGui.Separator();
                    UiShared.ColorTextWrapped("You are now ready to go. Press Finish to finalize the settings and open the Mare Synchronos main UI.", ImGuiColors.ParsedGreen);
                    ImGui.Separator();
                    if (ImGui.Button("Finish##finishIntro"))
                    {
                        SwitchToMainUi?.Invoke();
                        IsOpen = false;
                    }
                }
                else
                {
                    UiShared.TextWrapped("You will now have to register at a service. You can use the provided central service or pick a custom one. " +
                                         "There is no support for custom services from the plugin creator. Use at your own risk.");
                    UiShared.ColorTextWrapped("On registration on a service the plugin will create and save a secret key to your plugin configuration. " +
                                         "Make a backup of your secret key. In case of loss, it cannot be restored. The secret key is your identification to the service " +
                                         "to verify who you are. It is directly tied to the UID you will be receiving. In case of loss, you will have to re-register an account.", ImGuiColors.DalamudYellow);
                    UiShared.ColorTextWrapped("Do not ever, under any circumstances, share your secret key to anyone! Likewise do not share your Mare Synchronos plugin configuration to anyone!", ImGuiColors.DalamudYellow);
                    _uiShared.DrawServiceSelection(new Action(() => { }), true);
                }
            }
        }

        private void GetToSLocalization(int changeLanguageTo = -1)
        {
            if (changeLanguageTo != -1)
            {
                _uiShared.LoadLocalization(_languages.ElementAt(changeLanguageTo).Value);
            }
            
            TosParagraphs = new[] { Strings.ToS.Paragraph1, Strings.ToS.Paragraph2, Strings.ToS.Paragraph3, Strings.ToS.Paragraph4, Strings.ToS.Paragraph5, Strings.ToS.Paragraph6 };
            
            if (_pluginConfiguration.DarkSoulsAgreement)
            {
                GenerateDarkSoulsAgreementCaptcha();
            }
        }
        
        private void GenerateDarkSoulsAgreementCaptcha()
        {
            _darkSoulsCaptcha1 = GetCaptchaTuple();
            _darkSoulsCaptcha2 = GetCaptchaTuple();
            _darkSoulsCaptcha3 = GetCaptchaTuple();
        }

        private Tuple<string, string> GetCaptchaTuple()
        {
            Random random = new Random();
            var paragraphIdx = random.Next(TosParagraphs.Length);
            var splitParagraph = TosParagraphs[paragraphIdx].Split(".", StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).ToArray();
            var sentenceIdx = random.Next(splitParagraph.Length);
            var splitSentence = splitParagraph[sentenceIdx].Split(" ").Select(c => c.Trim()).Select(c => c.Replace(".", "").Replace(",", "").Replace("'", "")).ToArray();
            var wordIdx = random.Next(splitSentence.Length);
            return new($"{Strings.ToS.ParagraphLabel} {paragraphIdx + 1}, {Strings.ToS.SentenceLabel} {sentenceIdx + 1}, {Strings.ToS.WordLabel} {wordIdx + 1}", splitSentence[wordIdx]);
        }
    }
}
