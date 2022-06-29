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
using Penumbra.PlayerWatch;

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
        private readonly MainUi _mainUi;
        private readonly WindowSystem _windowSystem;
        private PlayerManager? _playerManager;
        private readonly DalamudUtil _dalamudUtil;
        private CachedPlayersManager? _characterCacheManager;
        private readonly DownloadUi _downloadUi;
        private readonly FileDialogManager _fileDialogManager;

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
            _dalamudUtil = new DalamudUtil(clientState, objectTable, PlayerWatchFactory.Create(framework, clientState, objectTable));

            _apiController = new ApiController(_configuration, _dalamudUtil);
            _ipcManager = new IpcManager(PluginInterface);

            _fileCacheManager = new FileCacheManager(_ipcManager, _configuration);
            _fileDialogManager = new FileDialogManager();

            var uiSharedComponent =
                new UiShared(_ipcManager, _apiController, _fileCacheManager, _fileDialogManager, _configuration, _dalamudUtil);
            _mainUi = new MainUi(_windowSystem, uiSharedComponent, _configuration, _apiController);

            _introUi = new IntroUi(_windowSystem, uiSharedComponent, _configuration, _fileCacheManager);
            _mainUi.SwitchFromMainUiToIntro += () =>
            {
                _introUi.IsOpen = true;
                _mainUi.IsOpen = false;
            };
            _introUi.SwitchFromIntroToMainUi += () =>
            {
                _introUi.IsOpen = false;
                _mainUi.IsOpen = true;
                _fileCacheManager.StartWatchers();
                ReLaunchCharacterManager();
            };
            _downloadUi = new DownloadUi(_windowSystem, _configuration, _apiController);


            _dalamudUtil.LogIn += DalamudUtilOnLogIn;
            _dalamudUtil.LogOut += DalamudUtilOnLogOut;
            _apiController.ChangingServers += ApiControllerOnChangingServers;

            if (_dalamudUtil.IsLoggedIn)
            {
                DalamudUtilOnLogIn();
            }
        }

        private void ApiControllerOnChangingServers(object? sender, EventArgs e)
        {
            _mainUi.IsOpen = false;
            _introUi.IsOpen = true;
        }

        public string Name => "Mare Synchronos";
        public void Dispose()
        {
            Logger.Debug("Disposing " + Name);
            _apiController.ChangingServers -= ApiControllerOnChangingServers;
            _apiController?.Dispose();

            _commandManager.RemoveHandler(CommandName);
            _dalamudUtil.LogIn -= DalamudUtilOnLogIn;
            _dalamudUtil.LogOut -= DalamudUtilOnLogOut;

            _mainUi?.Dispose();
            _introUi?.Dispose();
            _downloadUi?.Dispose();

            _fileCacheManager?.Dispose();
            _ipcManager?.Dispose();
            _playerManager?.Dispose();
            _characterCacheManager?.Dispose();
            _dalamudUtil.Dispose();
        }


        private void DalamudUtilOnLogIn()
        {
            Logger.Debug("Client login");

            PluginInterface.UiBuilder.Draw += Draw;
            PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
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
            PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
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
                _characterCacheManager = new CachedPlayersManager(_framework,
                    _apiController, _dalamudUtil, _ipcManager);
                _playerManager = new PlayerManager(_apiController, _ipcManager,
                    characterCacheFactory, _characterCacheManager, _dalamudUtil);
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
                _mainUi.Toggle();
            }
        }

        private void OpenConfigUi()
        {
            if (_configuration.HasValidSetup())
                _mainUi.Toggle();
            else
                _introUi.Toggle();
        }
    }
}
