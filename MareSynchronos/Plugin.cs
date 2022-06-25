using Dalamud.Game.Command;
using Dalamud.Plugin;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Factories;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState;
using System;
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
        private readonly ClientState _clientState;
        private readonly CommandManager _commandManager;
        private readonly Framework _framework;
        private readonly Configuration _configuration;
        private readonly FileCacheManager _fileCacheManager;
        private readonly IntroUi _introUi;
        private readonly IpcManager _ipcManager;
        private readonly ObjectTable _objectTable;
        private readonly DalamudPluginInterface _pluginInterface;
        private readonly PluginUi _pluginUi;
        private readonly WindowSystem _windowSystem;
        private CharacterManager? _characterManager;
        private readonly DalamudUtil _dalamudUtil;
        private CharacterCacheManager? _characterCacheManager;
        private readonly IPlayerWatcher _playerWatcher;
        private readonly DownloadUi _downloadUi;

        public Plugin(DalamudPluginInterface pluginInterface, CommandManager commandManager,
            Framework framework, ObjectTable objectTable, ClientState clientState)
        {
            Logger.Debug("Launching " + Name);
            _pluginInterface = pluginInterface;
            _commandManager = commandManager;
            _framework = framework;
            _objectTable = objectTable;
            _clientState = clientState;
            _configuration = _pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            _configuration.Initialize(_pluginInterface);

            _windowSystem = new WindowSystem("MareSynchronos");

            new FileCacheContext().Dispose(); // make sure db is initialized I guess

            // those can be initialized outside of game login
            _apiController = new ApiController(_configuration);
            _ipcManager = new IpcManager(_pluginInterface);

            _fileCacheManager = new FileCacheManager(_ipcManager, _configuration);

            var uiSharedComponent =
                new UiShared(_ipcManager, _apiController, _fileCacheManager, _configuration);
            _pluginUi = new PluginUi(_windowSystem, uiSharedComponent, _configuration, _apiController);
            _introUi = new IntroUi(_windowSystem, uiSharedComponent, _configuration, _fileCacheManager);
            _introUi.FinishedRegistration += (_, _) =>
            {
                _introUi.IsOpen = false;
                _pluginUi.IsOpen = true;
                ReLaunchCharacterManager();
            };
            _downloadUi = new DownloadUi(_windowSystem, _configuration, _apiController);

            _dalamudUtil = new DalamudUtil(_clientState, _objectTable);
            _playerWatcher = PlayerWatchFactory.Create(framework, _clientState, _objectTable);
            _playerWatcher.Enable();

            clientState.Login += ClientState_Login;
            clientState.Logout += ClientState_Logout;
            _apiController.AccountDeleted += ApiControllerOnAccountDeleted;

            if (clientState.IsLoggedIn)
            {
                ClientState_Login(null, null!);
            }
        }

        private void ApiControllerOnAccountDeleted(object? sender, EventArgs e)
        {
            _pluginUi.IsOpen = false;
            _introUi.IsOpen = true;
            _characterCacheManager?.Dispose();
            _characterManager?.Dispose();
        }

        public string Name => "Mare Synchronos";
        public void Dispose()
        {
            Logger.Debug("Disposing " + Name);
            _apiController.AccountDeleted -= ApiControllerOnAccountDeleted;
            _apiController?.Dispose();

            _commandManager.RemoveHandler(CommandName);
            _clientState.Login -= ClientState_Login;
            _clientState.Logout -= ClientState_Logout;

            _pluginUi?.Dispose();
            _introUi?.Dispose();
            _downloadUi?.Dispose();

            _fileCacheManager?.Dispose();
            _ipcManager?.Dispose();
            _characterManager?.Dispose();
            _characterCacheManager?.Dispose();
            _playerWatcher.Disable();
            _playerWatcher.Dispose();
        }


        private void ClientState_Login(object? sender, EventArgs e)
        {
            Logger.Debug("Client login");

            _pluginInterface.UiBuilder.Draw += Draw;
            _pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
            _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the Mare Synchronos UI"
            });

            if (!_configuration.HasValidSetup)
            {
                _introUi.IsOpen = true;
                return;
            }

            ReLaunchCharacterManager();
        }

        private void ClientState_Logout(object? sender, EventArgs e)
        {
            Logger.Debug("Client logout");
            _characterCacheManager?.Dispose();
            _characterManager?.Dispose();
            _pluginInterface.UiBuilder.Draw -= Draw;
            _pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
            _commandManager.RemoveHandler(CommandName);
        }

        public void ReLaunchCharacterManager()
        {
            _characterManager?.Dispose();

            Task.Run(async () =>
            {
                while (!_dalamudUtil.IsPlayerPresent)
                {
                    await Task.Delay(100);
                }

                try
                {
                    var characterCacheFactory =
                        new CharacterDataFactory(_dalamudUtil, _ipcManager);
                    _characterCacheManager = new CharacterCacheManager(_clientState, _framework, _objectTable,
                        _apiController, _dalamudUtil, _ipcManager);
                    _characterManager = new CharacterManager(_apiController, _objectTable, _ipcManager,
                        characterCacheFactory, _characterCacheManager, _dalamudUtil, _playerWatcher);
                    _characterManager.StartWatchingPlayer();
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex.Message);
                }
            });
        }

        private void Draw()
        {
            _windowSystem.Draw();
        }

        private void OnCommand(string command, string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                _pluginUi.Toggle();
            }
        }

        private void OpenConfigUi()
        {
            if (_configuration.HasValidSetup)
                _pluginUi.Toggle();
            else
                _introUi.Toggle();
        }
    }
}
