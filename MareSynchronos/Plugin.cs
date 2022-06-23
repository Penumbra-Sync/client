using Dalamud.Game.Command;
using Dalamud.Logging;
using Dalamud.Plugin;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Factories;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState;
using System;
using MareSynchronos.Models;
using MareSynchronos.PenumbraMod;
using Newtonsoft.Json;
using MareSynchronos.Managers;
using LZ4;
using MareSynchronos.WebAPI;
using Dalamud.Interface.Windowing;
using MareSynchronos.UI;

namespace MareSynchronos
{
    public sealed class Plugin : IDalamudPlugin
    {
        private const string CommandName = "/mare";
        private readonly ApiController _apiController;
        private readonly ClientState _clientState;
        private readonly CommandManager _commandManager;
        private readonly Configuration _configuration;
        private readonly FileCacheManager _fileCacheManager;
        private readonly Framework _framework;
        private readonly IntroUI _introUi;
        private readonly IpcManager _ipcManager;
        private readonly ObjectTable _objectTable;
        private readonly DalamudPluginInterface _pluginInterface;
        private readonly PluginUi _pluginUi;
        private readonly WindowSystem _windowSystem;
        private CharacterManager? _characterManager;

        public Plugin(DalamudPluginInterface pluginInterface, CommandManager commandManager,
            Framework framework, ObjectTable objectTable, ClientState clientState)
        {
            _pluginInterface = pluginInterface;
            _commandManager = commandManager;
            _framework = framework;
            _objectTable = objectTable;
            _clientState = clientState;
            _configuration = _pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            _configuration.Initialize(_pluginInterface);

            _windowSystem = new WindowSystem("MareSynchronos");

            _apiController = new ApiController(_configuration);
            _ipcManager = new IpcManager(_pluginInterface);
            _fileCacheManager = new FileCacheManager(new FileCacheFactory(), _ipcManager, _configuration);

            var uiSharedComponent =
                new UIShared(_ipcManager, _apiController, _fileCacheManager, _configuration);

            _pluginUi = new PluginUi(_windowSystem, uiSharedComponent, _configuration, _apiController);
            _introUi = new IntroUI(_windowSystem, uiSharedComponent, _configuration, _fileCacheManager);
            _introUi.FinishedRegistration += (_, _) =>
            {
                _introUi.IsOpen = false;
                _pluginUi.IsOpen = true;
                ReLaunchCharacterManager();
            };

            new FileCacheContext().Dispose(); // make sure db is initialized I guess

            clientState.Login += ClientState_Login;
            clientState.Logout += ClientState_Logout;

            if (clientState.IsLoggedIn)
            {
                ClientState_Login(null, null!);
            }
        }

        public string Name => "Mare Synchronos";
        public void Dispose()
        {
            _commandManager.RemoveHandler(CommandName);
            _clientState.Login -= ClientState_Login;
            _clientState.Logout -= ClientState_Logout;

            _pluginUi?.Dispose();
            _introUi?.Dispose();

            _fileCacheManager?.Dispose();
            _ipcManager?.Dispose();
            _characterManager?.Dispose();
            _apiController?.Dispose();
        }


        private void ClientState_Login(object? sender, EventArgs e)
        {
            PluginLog.Debug("Client login");

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
            PluginLog.Debug("Client logout");
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
                while (_clientState.LocalPlayer == null)
                {
                    await Task.Delay(50);
                }

                var characterCacheFactory =
                    new CharacterCacheFactory(_clientState, _ipcManager, new FileReplacementFactory(_ipcManager));
                _characterManager = new CharacterManager(
                    _clientState, _framework, _apiController, _objectTable, _ipcManager, _configuration, characterCacheFactory);
                _characterManager.StartWatchingPlayer();
                _ipcManager.PenumbraRedraw(_clientState.LocalPlayer!.Name.ToString());
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
            if(_configuration.HasValidSetup)
                _pluginUi.Toggle();
            else
                _introUi.Toggle();
        }
    }
}
