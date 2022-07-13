using Dalamud.Game.Command;
using Dalamud.Plugin;
using MareSynchronos.FileCacheDB;
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

namespace MareSynchronos
{
    public sealed class Plugin : IDalamudPlugin
    {
        private const string CommandName = "/mare";
        private readonly ApiController _apiController;
        private readonly CommandManager _commandManager;
        private readonly Framework _framework;
        private readonly Configuration _configuration;
        private readonly FileCacheManager _fileCacheManager;
        private readonly IntroUi _introUi;
        private readonly IpcManager _ipcManager;
        public static DalamudPluginInterface PluginInterface { get; set; }
        private readonly SettingsUi _settingsUi;
        private readonly WindowSystem _windowSystem;
        private PlayerManager? _playerManager;
        private readonly DalamudUtil _dalamudUtil;
        private OnlinePlayerManager? _characterCacheManager;
        private readonly DownloadUi _downloadUi;
        private readonly FileDialogManager _fileDialogManager;
        private readonly CompactUi _compactUi;
        private readonly UiShared _uiSharedComponent;


        public Plugin(DalamudPluginInterface pluginInterface, CommandManager commandManager,
            Framework framework, ObjectTable objectTable, ClientState clientState)
        {
            Logger.Debug("Launching " + Name);
            PluginInterface = pluginInterface;
            _commandManager = commandManager;
            _framework = framework;
            _configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            _configuration.Initialize(PluginInterface);
            _configuration.Migrate();

            _windowSystem = new WindowSystem("MareSynchronos");

            new FileCacheContext().Dispose(); // make sure db is initialized I guess

            // those can be initialized outside of game login
            _dalamudUtil = new DalamudUtil(clientState, objectTable, framework);

            _apiController = new ApiController(_configuration, _dalamudUtil);
            _ipcManager = new IpcManager(PluginInterface);

            _fileCacheManager = new FileCacheManager(_ipcManager, _configuration);
            _fileDialogManager = new FileDialogManager();

            _uiSharedComponent =
                new UiShared(_ipcManager, _apiController, _fileCacheManager, _fileDialogManager, _configuration, _dalamudUtil, PluginInterface);
            _settingsUi = new SettingsUi(_windowSystem, _uiSharedComponent, _configuration, _apiController);
            _compactUi = new CompactUi(_windowSystem, _uiSharedComponent, _configuration, _apiController);

            _introUi = new IntroUi(_windowSystem, _uiSharedComponent, _configuration, _fileCacheManager);
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
                _fileCacheManager.StartWatchers();
                ReLaunchCharacterManager();
            };
            _compactUi.OpenSettingsUi += () =>
            {
                _settingsUi.Toggle();
            };
            _downloadUi = new DownloadUi(_windowSystem, _configuration, _apiController, _uiSharedComponent);


            _dalamudUtil.LogIn += DalamudUtilOnLogIn;
            _dalamudUtil.LogOut += DalamudUtilOnLogOut;
            _apiController.RegisterFinalized += ApiControllerOnRegisterFinalized;

            if (_dalamudUtil.IsLoggedIn)
            {
                DalamudUtilOnLogIn();
            }
        }

        private void ApiControllerOnRegisterFinalized()
        {
            _introUi.IsOpen = false;
            _compactUi.IsOpen = true;
        }

        public string Name => "Mare Synchronos";
        public void Dispose()
        {
            Logger.Verbose("Disposing " + Name);
            _apiController.RegisterFinalized -= ApiControllerOnRegisterFinalized;
            _apiController?.Dispose();

            _commandManager.RemoveHandler(CommandName);
            _dalamudUtil.LogIn -= DalamudUtilOnLogIn;
            _dalamudUtil.LogOut -= DalamudUtilOnLogOut;

            _uiSharedComponent.Dispose();
            _settingsUi?.Dispose();
            _introUi?.Dispose();
            _downloadUi?.Dispose();
            _compactUi?.Dispose();

            _fileCacheManager?.Dispose();
            _ipcManager?.Dispose();
            _playerManager?.Dispose();
            _characterCacheManager?.Dispose();
            Logger.Debug("Shut down");
        }


        private void DalamudUtilOnLogIn()
        {
            Logger.Debug("Client login");

            PluginInterface.UiBuilder.Draw += Draw;
            PluginInterface.UiBuilder.OpenConfigUi += OpenUi;
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

            ReLaunchCharacterManager();
        }

        private void DalamudUtilOnLogOut()
        {
            Logger.Debug("Client logout");
            _characterCacheManager?.Dispose();
            _playerManager?.Dispose();
            PluginInterface.UiBuilder.Draw -= Draw;
            PluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
            _commandManager.RemoveHandler(CommandName);
        }

        public void ReLaunchCharacterManager()
        {
            _characterCacheManager?.Dispose();
            _playerManager?.Dispose();

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
                var characterCacheFactory =
                    new CharacterDataFactory(_dalamudUtil, _ipcManager);
                _playerManager = new PlayerManager(_apiController, _ipcManager,
                    characterCacheFactory, _dalamudUtil);
                _characterCacheManager = new OnlinePlayerManager(_framework,
                    _apiController, _dalamudUtil, _ipcManager, _playerManager);
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
