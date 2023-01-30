using Dalamud.Game.Command;
using Dalamud.Plugin;
using MareSynchronos.Factories;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState;
using Dalamud.Interface.ImGuiFileDialog;
using MareSynchronos.Managers;
using MareSynchronos.WebAPI;
using Dalamud.Interface.Windowing;
using MareSynchronos.UI;
using MareSynchronos.Utils;
using Dalamud.Game.ClientState.Conditions;
using MareSynchronos.FileCache;
using Dalamud.Game.Gui;
using MareSynchronos.Export;
using Dalamud.Data;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MareSynchronos;

public sealed class Plugin : IDalamudPlugin
{
    private const string _commandName = "/mare";
    private readonly ApiController _apiController;
    private readonly CommandManager _commandManager;
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
    private readonly FileCacheManager _fileCacheManager;
    private readonly PairManager _pairManager;
    private readonly CompactUi _compactUi;
    private readonly UiShared _uiSharedComponent;
    private readonly Dalamud.Localization _localization;
    private readonly FileReplacementFactory _fileReplacementFactory;
    private readonly MareCharaFileManager _mareCharaFileManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly GposeUi _gposeUi;
    private readonly ConfigurationService _configurationService;
    private readonly MareMediator _mediator;
    private readonly ServiceProvider _serviceProvider;


    public Plugin(DalamudPluginInterface pluginInterface, CommandManager commandManager, DataManager gameData,
        Framework framework, ObjectTable objectTable, ClientState clientState, Condition condition, ChatGui chatGui)
    {
        Logger.Debug("Launching " + Name);
        _pluginInterface = pluginInterface;
        _pluginInterface.UiBuilder.DisableGposeUiHide = true;
        _commandManager = commandManager;

        IServiceCollection collection = new ServiceCollection();
        // inject dalamud stuff
        collection.AddSingleton(pluginInterface);
        collection.AddSingleton(commandManager);
        collection.AddSingleton(gameData);
        collection.AddSingleton(framework);
        collection.AddSingleton(objectTable);
        collection.AddSingleton(clientState);
        collection.AddSingleton(condition);
        collection.AddSingleton(chatGui);
        collection.AddSingleton(new WindowSystem("MareSynchronos"));
        collection.AddSingleton<FileDialogManager>();

        // add mare related stuff
        collection.AddSingleton(new Dalamud.Localization("MareSynchronos.Localization.", "", useEmbedded: true));

        collection.AddSingleton<ConfigurationService>();
        collection.AddSingleton<MareMediator>();
        collection.AddSingleton<DalamudUtil>();
        collection.AddSingleton<IpcManager>();
        collection.AddSingleton<FileCacheManager>();
        collection.AddSingleton<ServerConfigurationManager>();
        collection.AddSingleton<PairManager>();
        collection.AddSingleton<ApiController>();
        collection.AddSingleton<PeriodicFileScanner>();
        collection.AddSingleton<FileReplacementFactory>();
        collection.AddSingleton<MareCharaFileManager>();

        collection.AddSingleton<UiShared>();
        collection.AddSingleton<SettingsUi>();
        collection.AddSingleton<CompactUi>();
        collection.AddSingleton<GposeUi>();
        collection.AddSingleton<IntroUi>();

        collection.AddTransient<TransientResourceManager>();
        collection.AddTransient<CharacterDataFactory>();
        collection.AddTransient<PlayerManager>();
        collection.AddTransient<OnlinePlayerManager>();

        _serviceProvider = collection.BuildServiceProvider(new ServiceProviderOptions() { ValidateOnBuild = true, ValidateScopes = true });

        _serviceProvider.GetRequiredService<Dalamud.Localization>().SetupWithLangCode("en");

        // those can be initialized outside of game login

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
        _downloadUi = new DownloadUi(_windowSystem, _configurationService, _apiController, _uiSharedComponent);

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

        var services = _serviceProvider.GetServices<IDisposable>().ToList();
        services.ForEach(c => c.Dispose());

        _apiController?.Dispose();

        _commandManager.RemoveHandler(_commandName);
        _dalamudUtil.LogIn -= DalamudUtilOnLogIn;
        _dalamudUtil.LogOut -= DalamudUtilOnLogOut;

        _uiSharedComponent.Dispose();
        _settingsUi?.Dispose();
        _introUi?.Dispose();
        _downloadUi?.Dispose();
        _compactUi?.Dispose();
        _gposeUi?.Dispose();

        _pairManager.Dispose();
        _periodicFileScanner?.Dispose();
        _fileCacheManager?.Dispose();
        _playerManager?.Dispose();
        _characterCacheManager?.Dispose();
        _ipcManager?.Dispose();
        _transientResourceManager?.Dispose();
        _dalamudUtil.Dispose();
        _configurationService?.Dispose();

        _mediator.Dispose();

        Logger.Debug("Shut down");
    }


    private void DalamudUtilOnLogIn()
    {
        Logger.Debug("Client login");

        _pluginInterface.UiBuilder.Draw += Draw;
        _pluginInterface.UiBuilder.OpenConfigUi += OpenUi;
        _commandManager.AddHandler(_commandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Mare Synchronos UI",
        });

        if (!_configurationService.Current.HasValidSetup() || !_serverConfigurationManager.HasValidConfig())
        {
            _introUi.IsOpen = true;
            _compactUi.IsOpen = false;
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
        _commandManager.RemoveHandler(_commandName);
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
            await Task.Delay(100).ConfigureAwait(false);
        }

        try
        {
            Logger.Debug("Launching Managers");

            _transientResourceManager = _serviceProvider.GetRequiredService<TransientResourceManager>();
            _playerManager = _serviceProvider.GetRequiredService<PlayerManager>();
            _characterCacheManager = _serviceProvider.GetRequiredService<OnlinePlayerManager>();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex.Message);
        }
    }

    private void Draw()
    {
        _windowSystem.Draw();
        _fileDialogManager.Draw();
    }

    private void OnCommand(string command, string args)
    {
        var splitArgs = args.ToLowerInvariant().Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);

        if (splitArgs == null || splitArgs.Length == 0)
        {
            // Interpret this as toggling the UI
            OpenUi();
            return;
        }

        if (string.Equals(splitArgs[0], "toggle", StringComparison.OrdinalIgnoreCase))
        {
            if (_serverConfigurationManager.CurrentServer == null) return;
            var fullPause = splitArgs.Length > 1 ? splitArgs[1] switch
            {
                "on" => false,
                "off" => true,
                _ => !_serverConfigurationManager.CurrentServer.FullPause,
            } : !_serverConfigurationManager.CurrentServer.FullPause;

            if (fullPause != _serverConfigurationManager.CurrentServer.FullPause)
            {
                _serverConfigurationManager.CurrentServer.FullPause = fullPause;
                _serverConfigurationManager.Save();
                _ = _apiController.CreateConnections();
            }
        }
        else if (string.Equals(splitArgs[0], "gpose", StringComparison.OrdinalIgnoreCase))
        {
            _gposeUi.Toggle();
        }
    }

    private void OpenUi()
    {
        if (_configurationService.Current.HasValidSetup())
            _compactUi.Toggle();
        else
            _introUi.Toggle();
    }
}
