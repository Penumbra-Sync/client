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

namespace MareSynchronos;

public sealed class Plugin : IDalamudPlugin
{
    private const string _commandName = "/mare";
    private IServiceScope? _runtimeServiceServiceScope;
    private readonly ServiceProvider _serviceProvider;


    public Plugin(DalamudPluginInterface pluginInterface, CommandManager commandManager, DataManager gameData,
        Framework framework, ObjectTable objectTable, ClientState clientState, Condition condition, ChatGui chatGui)
    {
        Logger.Debug("Launching " + Name);

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
        collection.AddSingleton(pluginInterface.UiBuilder);
        collection.AddSingleton(new WindowSystem("MareSynchronos"));
        collection.AddSingleton<FileDialogManager>();

        // add mare related stuff
        collection.AddSingleton(new Dalamud.Localization("MareSynchronos.Localization.", "", useEmbedded: true));

        collection.AddSingleton<ConfigurationService>();
        collection.AddSingleton<MareMediator>();
        collection.AddSingleton<DalamudUtil>();
        collection.AddSingleton<IpcManager>();
        collection.AddSingleton<FileCacheManager>();
        collection.AddSingleton<CachedPlayerFactory>();
        collection.AddSingleton<PairFactory>();
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
        collection.AddSingleton<DownloadUi>();

        collection.AddScoped<TransientResourceManager>();
        collection.AddScoped<CharacterDataFactory>();
        collection.AddScoped<PlayerManager>();
        collection.AddScoped<OnlinePlayerManager>();

        _serviceProvider = collection.BuildServiceProvider(new ServiceProviderOptions() { ValidateOnBuild = true, ValidateScopes = true });

        // those can be initialized outside of game login

        _serviceProvider.GetRequiredService<Dalamud.Localization>().SetupWithLangCode("en");
        _serviceProvider.GetRequiredService<DalamudPluginInterface>().UiBuilder.DisableGposeUiHide = true;

        var mediator = _serviceProvider.GetRequiredService<MareMediator>();
        mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => ReLaunchCharacterManager());
        mediator.Subscribe<DalamudLoginMessage>(this, (_) => DalamudUtilOnLogIn());
        mediator.Subscribe<DalamudLogoutMessage>(this, (_) => DalamudUtilOnLogOut());

        _serviceProvider.GetRequiredService<SettingsUi>();
        _serviceProvider.GetRequiredService<CompactUi>();
        _serviceProvider.GetRequiredService<GposeUi>();
        _serviceProvider.GetRequiredService<IntroUi>();
        _serviceProvider.GetRequiredService<DownloadUi>();
    }

    public string Name => "Mare Synchronos";

    public void Dispose()
    {
        Logger.Verbose("Disposing " + Name);

        _serviceProvider.GetRequiredService<CommandManager>().RemoveHandler(_commandName);

        _runtimeServiceServiceScope?.Dispose();
        _serviceProvider.Dispose();

        Logger.Debug("Shut down");
    }


    private void DalamudUtilOnLogIn()
    {
        Logger.Debug("Client login");

        var pi = _serviceProvider.GetRequiredService<DalamudPluginInterface>();
        pi.UiBuilder.Draw += Draw;
        pi.UiBuilder.OpenConfigUi += OpenUi;
        _serviceProvider.GetRequiredService<CommandManager>().AddHandler(_commandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Mare Synchronos UI",
        });

        if (!_serviceProvider.GetRequiredService<ConfigurationService>().Current.HasValidSetup()
            || !_serviceProvider.GetRequiredService<ServerConfigurationManager>().HasValidConfig())
        {
            _serviceProvider.GetRequiredService<MareMediator>().Publish(new SwitchToIntroUiMessage());
            return;
        }

        _serviceProvider.GetRequiredService<PeriodicFileScanner>().StartScan();
        ReLaunchCharacterManager();
    }

    private void DalamudUtilOnLogOut()
    {
        Logger.Debug("Client logout");
        _runtimeServiceServiceScope?.Dispose();
        var pi = _serviceProvider.GetRequiredService<DalamudPluginInterface>();
        pi.UiBuilder.Draw -= Draw;
        pi.UiBuilder.OpenConfigUi -= OpenUi;
        _serviceProvider.GetRequiredService<CommandManager>().RemoveHandler(_commandName);
    }

    public void ReLaunchCharacterManager()
    {
        _runtimeServiceServiceScope?.Dispose();

        Task.Run(WaitForPlayerAndLaunchCharacterManager);
    }

    private async Task WaitForPlayerAndLaunchCharacterManager()
    {
        var dalamudUtil = _serviceProvider.GetRequiredService<DalamudUtil>();
        while (!dalamudUtil.IsPlayerPresent)
        {
            await Task.Delay(100).ConfigureAwait(false);
        }

        try
        {
            Logger.Debug("Launching Managers");

            _runtimeServiceServiceScope = _serviceProvider.CreateScope();
            _runtimeServiceServiceScope.ServiceProvider.GetRequiredService<TransientResourceManager>();
            _runtimeServiceServiceScope.ServiceProvider.GetRequiredService<PlayerManager>();
            _runtimeServiceServiceScope.ServiceProvider.GetRequiredService<OnlinePlayerManager>();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex.Message);
        }
    }

    private void Draw()
    {
        _serviceProvider.GetRequiredService<WindowSystem>().Draw();
        _serviceProvider.GetRequiredService<FileDialogManager>().Draw();
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
            var _serverConfigurationManager = _serviceProvider.GetRequiredService<ServerConfigurationManager>();
            var _apiController = _serviceProvider.GetRequiredService<ApiController>();
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
                _ = _serviceProvider.GetRequiredService<ApiController>().CreateConnections();
            }
        }
        else if (string.Equals(splitArgs[0], "gpose", StringComparison.OrdinalIgnoreCase))
        {
            _serviceProvider.GetRequiredService<GposeUi>().Toggle();
        }
    }

    private void OpenUi()
    {
        
        if (_serviceProvider.GetRequiredService<ConfigurationService>().Current.HasValidSetup())
            _serviceProvider.GetRequiredService<CompactUi>().Toggle();
        else
            _serviceProvider.GetRequiredService<IntroUi>().Toggle();
    }
}
