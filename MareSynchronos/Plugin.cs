using Dalamud.Game.Command;
using Dalamud.Plugin;
using MareSynchronos.Factories;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState;
using System;
using Dalamud.Interface.ImGuiFileDialog;
using MareSynchronos.Managers;
using MareSynchronos.WebAPI;
using Dalamud.Interface.Windowing;
using MareSynchronos.UI;
using MareSynchronos.Utils;
using Dalamud.Game.ClientState.Conditions;
using MareSynchronos.FileCache;

namespace MareSynchronos
{
    public sealed class Plugin : IDalamudPlugin
    {
        private const string CommandName = "/mare";
        private readonly ApiController _apiController;
        private readonly CommandManager _commandManager;
        private readonly Configuration _configuration;
        private readonly PeriodicFileScanner _periodicFileScanner;
        private readonly IntroUi _introUi;
        private readonly IpcManager _ipcManager;
        private readonly DalamudPluginInterface _pluginInterface;
        private readonly SettingsUi _settingsUi;
        private readonly WindowSystem _windowSystem;
        private PlayerManager? _playerManager;
        private TransientResourceManager? _transientResourceManager;
        private readonly DalamudUtil _dalamudUtil;
        private OnlinePlayerManager? _characterCacheManager;
        private readonly DownloadUi _downloadUi;
        private readonly FileDialogManager _fileDialogManager;
        private readonly FileCacheManager _fileDbManager;
        private readonly CompactUi _compactUi;
        private readonly UiShared _uiSharedComponent;
        private readonly Dalamud.Localization _localization;


        public Plugin(DalamudPluginInterface pluginInterface, CommandManager commandManager,
            Framework framework, ObjectTable objectTable, ClientState clientState, Condition condition)
        {
            Logger.Debug("Launching " + Name);
            _pluginInterface = pluginInterface;
            _commandManager = commandManager;
            _configuration = _pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            _configuration.Initialize(_pluginInterface);
            _configuration.Migrate();

            _localization = new Dalamud.Localization("MareSynchronos.Localization.", "", true);
            _localization.SetupWithLangCode("en");

            _windowSystem = new WindowSystem("MareSynchronos");

            // those can be initialized outside of game login
            _dalamudUtil = new DalamudUtil(clientState, objectTable, framework, condition);

            _ipcManager = new IpcManager(_pluginInterface, _dalamudUtil);
            _fileDialogManager = new FileDialogManager();
            _fileDbManager = new FileCacheManager(_ipcManager, _configuration, _pluginInterface.ConfigDirectory.FullName);
            _apiController = new ApiController(_configuration, _dalamudUtil, _fileDbManager);
            _periodicFileScanner = new PeriodicFileScanner(_ipcManager, _configuration, _fileDbManager, _apiController, _dalamudUtil);

            _uiSharedComponent =
                new UiShared(_ipcManager, _apiController, _periodicFileScanner, _fileDialogManager, _configuration, _dalamudUtil, _pluginInterface, _localization);
            _settingsUi = new SettingsUi(_windowSystem, _uiSharedComponent, _configuration, _apiController);
            _compactUi = new CompactUi(_windowSystem, _uiSharedComponent, _configuration, _apiController);

            _introUi = new IntroUi(_windowSystem, _uiSharedComponent, _configuration, _periodicFileScanner);
            _settingsUi.SwitchToIntroUi += () =>
            {
                _introUi.IsOpen = true;
                _settingsUi.IsOpen = false;
                _compactUi.IsOpen = false;
            };
            _introUi.SwitchToMainUi += () =>
            {
                _introUi.IsOpen = false;
                _compactUi.IsOpen = true;
                _periodicFileScanner.StartScan();
                ReLaunchCharacterManager();
            };
            _compactUi.OpenSettingsUi += () =>
            {
                _settingsUi.Toggle();
            };
            _downloadUi = new DownloadUi(_windowSystem, _configuration, _apiController, _uiSharedComponent);


            _dalamudUtil.LogIn += DalamudUtilOnLogIn;
            _dalamudUtil.LogOut += DalamudUtilOnLogOut;

            if (_dalamudUtil.IsLoggedIn)
            {
                DalamudUtilOnLogIn();
            }
        }

        public string Name => "Mare Synchronos";
        public void Dispose()
        {
            Logger.Verbose("Disposing " + Name);
            _apiController?.Dispose();

            _commandManager.RemoveHandler(CommandName);
            _dalamudUtil.LogIn -= DalamudUtilOnLogIn;
            _dalamudUtil.LogOut -= DalamudUtilOnLogOut;

            _uiSharedComponent.Dispose();
            _settingsUi?.Dispose();
            _introUi?.Dispose();
            _downloadUi?.Dispose();
            _compactUi?.Dispose();

            _periodicFileScanner?.Dispose();
            _fileDbManager?.Dispose();
            _playerManager?.Dispose();
            _characterCacheManager?.Dispose();
            _ipcManager?.Dispose();
            _transientResourceManager?.Dispose();
            _dalamudUtil.Dispose();
            Logger.Debug("Shut down");
        }


        private void DalamudUtilOnLogIn()
        {
            Logger.Debug("Client login");

            _pluginInterface.UiBuilder.Draw += Draw;
            _pluginInterface.UiBuilder.OpenConfigUi += OpenUi;
            _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the Mare Synchronos UI"
            });

            if (!_configuration.HasValidSetup())
            {
                _introUi.IsOpen = true;
                _configuration.FullPause = false;
                _configuration.Save();
                return;
            }

            _periodicFileScanner.StartScan();
            ReLaunchCharacterManager();
        }

        private void DalamudUtilOnLogOut()
        {
            Logger.Debug("Client logout");
            _characterCacheManager?.Dispose();
            _playerManager?.Dispose();
            _transientResourceManager?.Dispose();
            _pluginInterface.UiBuilder.Draw -= Draw;
            _pluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
            _commandManager.RemoveHandler(CommandName);
        }

        public void ReLaunchCharacterManager()
        {
            _characterCacheManager?.Dispose();
            _playerManager?.Dispose();
            _transientResourceManager?.Dispose();

            Task.Run(WaitForPlayerAndLaunchCharacterManager);
        }

        private async Task WaitForPlayerAndLaunchCharacterManager()
        {
            while (!_dalamudUtil.IsPlayerPresent)
            {
                await Task.Delay(100);
            }

            try
            {
                _transientResourceManager = new TransientResourceManager(_ipcManager, _dalamudUtil);
                var characterCacheFactory =
                    new CharacterDataFactory(_dalamudUtil, _ipcManager, _transientResourceManager, _fileDbManager);
                _playerManager = new PlayerManager(_apiController, _ipcManager,
                    characterCacheFactory, _dalamudUtil, _transientResourceManager, _periodicFileScanner);
                _characterCacheManager = new OnlinePlayerManager(_apiController,
                    _dalamudUtil, _ipcManager, _playerManager, _fileDbManager);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex.Message);
            }
        }

        private void Draw()
        {
            _windowSystem.Draw();
            _fileDialogManager.Draw();
        }

        private void OnCommand(string command, string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                OpenUi();
                return;
            }

            var splitArgs = args.ToLowerInvariant().Trim().Split();

            if (splitArgs.Length == 0)
            {
                // Interpret this as toggling the UI
                OpenUi();
                return;
            }

            if (splitArgs[0] == "toggle")
            {
                var fullPause = splitArgs.Length > 1 ? splitArgs[1] switch
                {
                    "on" => false,
                    "off" => true,
                    _ => !_configuration.FullPause,
                } : !_configuration.FullPause;

                if (fullPause != _configuration.FullPause)
                {
                    _configuration.FullPause = fullPause;
                    _configuration.Save();
                    _ = _apiController.CreateConnections();
                }
            }
        }

        private void OpenUi()
        {
            if (_configuration.HasValidSetup())
                _compactUi.Toggle();
            else
                _introUi.Toggle();
        }
    }
}
