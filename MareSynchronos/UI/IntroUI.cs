using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using MareSynchronos.FileCache;
using MareSynchronos.Localization;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Text.RegularExpressions;

namespace MareSynchronos.UI;

public partial class IntroUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly CacheMonitor _cacheMonitor;
    private readonly Dictionary<string, string> _languages = new(StringComparer.Ordinal) { { "English", "en" }, { "Deutsch", "de" }, { "Français", "fr" } };
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly UiSharedService _uiShared;
    private int _currentLanguage;
    private bool _readFirstPage;

    private string _secretKey = string.Empty;
    private string _timeoutLabel = string.Empty;
    private Task? _timeoutTask;
    private string[]? _tosParagraphs;
    private bool _useLegacyLogin = false;

    public IntroUi(ILogger<IntroUi> logger, UiSharedService uiShared, MareConfigService configService,
        CacheMonitor fileCacheManager, ServerConfigurationManager serverConfigurationManager, MareMediator mareMediator,
        PerformanceCollectorService performanceCollectorService, DalamudUtilService dalamudUtilService) : base(logger, mareMediator, "Mare Synchronos Setup", performanceCollectorService)
    {
        _uiShared = uiShared;
        _configService = configService;
        _cacheMonitor = fileCacheManager;
        _serverConfigurationManager = serverConfigurationManager;
        _dalamudUtilService = dalamudUtilService;
        IsOpen = false;
        ShowCloseButton = false;
        RespectCloseHotkey = false;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(600, 400),
            MaximumSize = new Vector2(600, 2000),
        };

        GetToSLocalization();

        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) =>
        {
            _configService.Current.UseCompactor = !dalamudUtilService.IsWine;
            IsOpen = true;
        });
    }

    private int _prevIdx = -1;

    protected override void DrawInternal()
    {
        if (_uiShared.IsInGpose) return;

        if (!_configService.Current.AcceptedAgreement && !_readFirstPage)
        {
            _uiShared.BigText("Welcome to Mare Synchronos");
            ImGui.Separator();
            UiSharedService.TextWrapped("Mare Synchronos is a plugin that will replicate your full current character state including all Penumbra mods to other paired Mare Synchronos users. " +
                              "Note that you will have to have Penumbra as well as Glamourer installed to use this plugin.");
            UiSharedService.TextWrapped("We will have to setup a few things first before you can start using this plugin. Click on next to continue.");

            UiSharedService.ColorTextWrapped("Note: Any modifications you have applied through anything but Penumbra cannot be shared and your character state on other clients " +
                                 "might look broken because of this or others players mods might not apply on your end altogether. " +
                                 "If you want to use this plugin you will have to move your mods to Penumbra.", ImGuiColors.DalamudYellow);
            if (!_uiShared.DrawOtherPluginState()) return;
            ImGui.Separator();
            if (ImGui.Button("Next##toAgreement"))
            {
                _readFirstPage = true;
#if !DEBUG
                _timeoutTask = Task.Run(async () =>
                {
                    for (int i = 60; i > 0; i--)
                    {
                        _timeoutLabel = $"{Strings.ToS.ButtonWillBeAvailableIn} {i}s";
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    }
                });
#else
                _timeoutTask = Task.CompletedTask;
#endif
            }
        }
        else if (!_configService.Current.AcceptedAgreement && _readFirstPage)
        {
            Vector2 textSize;
            using (_uiShared.UidFont.Push())
            {
                textSize = ImGui.CalcTextSize(Strings.ToS.LanguageLabel);
                ImGui.TextUnformatted(Strings.ToS.AgreementLabel);
            }

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
            UiSharedService.ColorText(readThis, ImGuiColors.DalamudRed);
            ImGui.SetWindowFontScale(1.0f);
            ImGui.Separator();

            UiSharedService.TextWrapped(_tosParagraphs![0]);
            UiSharedService.TextWrapped(_tosParagraphs![1]);
            UiSharedService.TextWrapped(_tosParagraphs![2]);
            UiSharedService.TextWrapped(_tosParagraphs![3]);
            UiSharedService.TextWrapped(_tosParagraphs![4]);
            UiSharedService.TextWrapped(_tosParagraphs![5]);

            ImGui.Separator();
            if (_timeoutTask?.IsCompleted ?? true)
            {
                if (ImGui.Button(Strings.ToS.AgreeLabel + "##toSetup"))
                {
                    _configService.Current.AcceptedAgreement = true;
                    _configService.Save();
                }
            }
            else
            {
                UiSharedService.TextWrapped(_timeoutLabel);
            }
        }
        else if (_configService.Current.AcceptedAgreement
                 && (string.IsNullOrEmpty(_configService.Current.CacheFolder)
                     || !_configService.Current.InitialScanComplete
                     || !Directory.Exists(_configService.Current.CacheFolder)))
        {
            using (_uiShared.UidFont.Push())
                ImGui.TextUnformatted("File Storage Setup");

            ImGui.Separator();

            if (!_uiShared.HasValidPenumbraModPath)
            {
                UiSharedService.ColorTextWrapped("You do not have a valid Penumbra path set. Open Penumbra and set up a valid path for the mod directory.", ImGuiColors.DalamudRed);
            }
            else
            {
                UiSharedService.TextWrapped("To not unnecessary download files already present on your computer, Mare Synchronos will have to scan your Penumbra mod directory. " +
                                     "Additionally, a local storage folder must be set where Mare Synchronos will download other character files to. " +
                                     "Once the storage folder is set and the scan complete, this page will automatically forward to registration at a service.");
                UiSharedService.TextWrapped("Note: The initial scan, depending on the amount of mods you have, might take a while. Please wait until it is completed.");
                UiSharedService.ColorTextWrapped("Warning: once past this step you should not delete the FileCache.csv of Mare Synchronos in the Plugin Configurations folder of Dalamud. " +
                                          "Otherwise on the next launch a full re-scan of the file cache database will be initiated.", ImGuiColors.DalamudYellow);
                UiSharedService.ColorTextWrapped("Warning: if the scan is hanging and does nothing for a long time, chances are high your Penumbra folder is not set up properly.", ImGuiColors.DalamudYellow);
                _uiShared.DrawCacheDirectorySetting();
            }

            if (!_cacheMonitor.IsScanRunning && !string.IsNullOrEmpty(_configService.Current.CacheFolder) && _uiShared.HasValidPenumbraModPath && Directory.Exists(_configService.Current.CacheFolder))
            {
                if (ImGui.Button("Start Scan##startScan"))
                {
                    _cacheMonitor.InvokeScan();
                }
            }
            else
            {
                _uiShared.DrawFileScanState();
            }
            if (!_dalamudUtilService.IsWine)
            {
                var useFileCompactor = _configService.Current.UseCompactor;
                if (ImGui.Checkbox("Use File Compactor", ref useFileCompactor))
                {
                    _configService.Current.UseCompactor = useFileCompactor;
                    _configService.Save();
                }
                UiSharedService.ColorTextWrapped("The File Compactor can save a tremendeous amount of space on the hard disk for downloads through Mare. It will incur a minor CPU penalty on download but can speed up " +
                    "loading of other characters. It is recommended to keep it enabled. You can change this setting later anytime in the Mare settings.", ImGuiColors.DalamudYellow);
            }
        }
        else if (!_uiShared.ApiController.ServerAlive)
        {
            using (_uiShared.UidFont.Push())
                ImGui.TextUnformatted("Service Registration");
            ImGui.Separator();
            UiSharedService.TextWrapped("To be able to use Mare Synchronos you will have to register an account.");
            UiSharedService.TextWrapped("For the official Mare Synchronos Servers the account creation will be handled on the official Mare Synchronos Discord. Due to security risks for the server, there is no way to handle this sensibly otherwise.");
            UiSharedService.TextWrapped("If you want to register at the main server \"" + WebAPI.ApiController.MainServer + "\" join the Discord and follow the instructions as described in #mare-service.");

            if (ImGui.Button("Join the Mare Synchronos Discord"))
            {
                Util.OpenLink("https://discord.gg/mpNdkrTRjW");
            }

            UiSharedService.TextWrapped("For all other non official services you will have to contact the appropriate service provider how to obtain a secret key.");

            UiSharedService.DistanceSeparator();

            UiSharedService.TextWrapped("Once you have registered you can connect to the service using the tools provided below.");

            int serverIdx = 0;
            var selectedServer = _serverConfigurationManager.GetServerByIndex(serverIdx);

            using (var node = ImRaii.TreeNode("Advanced Options"))
            {
                if (node)
                {
                    serverIdx = _uiShared.DrawServiceSelection(selectOnChange: true, showConnect: false);
                    if (serverIdx != _prevIdx)
                    {
                        _uiShared.ResetOAuthTasksState();
                        _prevIdx = serverIdx;
                    }

                    selectedServer = _serverConfigurationManager.GetServerByIndex(serverIdx);
                    _useLegacyLogin = !selectedServer.UseOAuth2;

                    if (ImGui.Checkbox("Use Legacy Login with Secret Key", ref _useLegacyLogin))
                    {
                        _serverConfigurationManager.GetServerByIndex(serverIdx).UseOAuth2 = !_useLegacyLogin;
                        _serverConfigurationManager.Save();
                    }
                }
            }

            if (_useLegacyLogin)
            {
                var text = "Enter Secret Key";
                var buttonText = "Save";
                var buttonWidth = _secretKey.Length != 64 ? 0 : ImGuiHelpers.GetButtonSize(buttonText).X + ImGui.GetStyle().ItemSpacing.X;
                var textSize = ImGui.CalcTextSize(text);

                ImGuiHelpers.ScaledDummy(5);
                UiSharedService.DrawGroupedCenteredColorText("Strongly consider to use OAuth2 to authenticate, if the server supports it (the current main server does). " +
                    "The authentication flow is simpler and you do not require to store or maintain Secret Keys. " +
                    "You already implicitly register using Discord, so the OAuth2 method will be cleaner and more straight-forward to use.", ImGuiColors.DalamudYellow, 500);
                ImGuiHelpers.ScaledDummy(5);

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(text);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonWidth - textSize.X);
                ImGui.InputText("", ref _secretKey, 64);
                if (_secretKey.Length > 0 && _secretKey.Length != 64)
                {
                    UiSharedService.ColorTextWrapped("Your secret key must be exactly 64 characters long. Don't enter your Lodestone auth here.", ImGuiColors.DalamudRed);
                }
                else if (_secretKey.Length == 64 && !HexRegex().IsMatch(_secretKey))
                {
                    UiSharedService.ColorTextWrapped("Your secret key can only contain ABCDEF and the numbers 0-9.", ImGuiColors.DalamudRed);
                }
                else if (_secretKey.Length == 64)
                {
                    ImGui.SameLine();
                    if (ImGui.Button(buttonText))
                    {
                        if (_serverConfigurationManager.CurrentServer == null) _serverConfigurationManager.SelectServer(0);
                        if (!_serverConfigurationManager.CurrentServer!.SecretKeys.Any())
                        {
                            _serverConfigurationManager.CurrentServer!.SecretKeys.Add(_serverConfigurationManager.CurrentServer.SecretKeys.Select(k => k.Key).LastOrDefault() + 1, new SecretKey()
                            {
                                FriendlyName = $"Secret Key added on Setup ({DateTime.Now:yyyy-MM-dd})",
                                Key = _secretKey,
                            });
                            _serverConfigurationManager.AddCurrentCharacterToServer();
                        }
                        else
                        {
                            _serverConfigurationManager.CurrentServer!.SecretKeys[0] = new SecretKey()
                            {
                                FriendlyName = $"Secret Key added on Setup ({DateTime.Now:yyyy-MM-dd})",
                                Key = _secretKey,
                            };
                        }
                        _secretKey = string.Empty;
                        _ = Task.Run(() => _uiShared.ApiController.CreateConnectionsAsync());
                    }
                }
            }
            else
            {
                if (string.IsNullOrEmpty(selectedServer.OAuthToken))
                {
                    UiSharedService.TextWrapped("Press the button below to verify the server has OAuth2 capabilities. Afterwards, authenticate using Discord in the Browser window.");
                    _uiShared.DrawOAuth(selectedServer);
                }
                else
                {
                    UiSharedService.ColorTextWrapped($"OAuth2 is connected. Linked to: Discord User {_serverConfigurationManager.GetDiscordUserFromToken(selectedServer)}", ImGuiColors.HealerGreen);
                    UiSharedService.TextWrapped("Now press the update UIDs button to get a list of all of your UIDs on the server.");
                    _uiShared.DrawUpdateOAuthUIDsButton(selectedServer);
                    var playerName = _dalamudUtilService.GetPlayerName();
                    var playerWorld = _dalamudUtilService.GetHomeWorldId();
                    UiSharedService.TextWrapped($"Once pressed, select the UID you want to use for your current character {_dalamudUtilService.GetPlayerName()}. If no UIDs are visible, make sure you are connected to the correct Discord account. " +
                        $"If that is not the case, use the unlink button below (hold CTRL to unlink).");
                    _uiShared.DrawUnlinkOAuthButton(selectedServer);

                    var auth = selectedServer.Authentications.Find(a => string.Equals(a.CharacterName, playerName, StringComparison.Ordinal) && a.WorldId == playerWorld);
                    if (auth == null)
                    {
                        auth = new Authentication()
                        {
                            CharacterName = playerName,
                            WorldId = playerWorld
                        };
                        selectedServer.Authentications.Add(auth);
                        _serverConfigurationManager.Save();
                    }

                    _uiShared.DrawUIDComboForAuthentication(0, auth, selectedServer.ServerUri);

                    using (ImRaii.Disabled(string.IsNullOrEmpty(auth.UID)))
                    {
                        if (_uiShared.IconTextButton(Dalamud.Interface.FontAwesomeIcon.Link, "Connect to Service"))
                        {
                            _ = Task.Run(() => _uiShared.ApiController.CreateConnectionsAsync());
                        }
                    }
                    if (string.IsNullOrEmpty(auth.UID))
                        UiSharedService.AttachToolTip("Select a UID to be able to connect to the service");
                }
            }
        }
        else
        {
            Mediator.Publish(new SwitchToMainUiMessage());
            IsOpen = false;
        }
    }

    private void GetToSLocalization(int changeLanguageTo = -1)
    {
        if (changeLanguageTo != -1)
        {
            _uiShared.LoadLocalization(_languages.ElementAt(changeLanguageTo).Value);
        }

        _tosParagraphs = [Strings.ToS.Paragraph1, Strings.ToS.Paragraph2, Strings.ToS.Paragraph3, Strings.ToS.Paragraph4, Strings.ToS.Paragraph5, Strings.ToS.Paragraph6];
    }

    [GeneratedRegex("^([A-F0-9]{2})+")]
    private static partial Regex HexRegex();
}