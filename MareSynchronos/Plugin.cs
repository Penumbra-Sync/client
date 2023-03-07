using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState;
using Dalamud.Interface.ImGuiFileDialog;
using MareSynchronos.WebAPI;
using Dalamud.Interface.Windowing;
using MareSynchronos.UI;
using Dalamud.Game.ClientState.Conditions;
using MareSynchronos.FileCache;
using Dalamud.Game.Gui;
using Dalamud.Data;
using MareSynchronos.MareConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MareSynchronos.WebAPI.SignalR;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Interop;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Export;
using MareSynchronos.PlayerData.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI.Files;
using System.Reflection;

namespace MareSynchronos;

public sealed class Plugin : IDalamudPlugin
{
    private readonly MarePlugin _plugin;
    public string Name => "Mare Synchronos";
    private readonly ILogger<Plugin> _pluginLogger;

    public Plugin(DalamudPluginInterface pluginInterface, CommandManager commandManager, DataManager gameData,
        Framework framework, ObjectTable objectTable, ClientState clientState, Condition condition, ChatGui chatGui)
    {
        IServiceCollection collection = new ServiceCollection();
        collection.AddLogging(o =>
        {
            o.AddDalamudLogging();
            o.SetMinimumLevel(LogLevel.Trace);
        });

        // inject dalamud related things
        collection.AddSingleton(new WindowSystem("MareSynchronos"));
        collection.AddSingleton<FileDialogManager>();
        collection.AddSingleton(new Dalamud.Localization("MareSynchronos.Localization.", "", useEmbedded: true));

        // add mare related singletons
        collection.AddSingleton<ConfigurationMigrator>();
        collection.AddSingleton<MareConfigService>();
        collection.AddSingleton<ServerTagConfigService>();
        collection.AddSingleton<TransientConfigService>();
        collection.AddSingleton<NotesConfigService>();
        collection.AddSingleton<ServerConfigService>();
        collection.AddSingleton<MareMediator>();
        collection.AddSingleton<FileCacheManager>();
        collection.AddSingleton<CachedPlayerFactory>();
        collection.AddSingleton<PairFactory>();
        collection.AddSingleton<ServerConfigurationManager>();
        collection.AddSingleton<PairManager>();
        collection.AddSingleton<ApiController>();
        collection.AddSingleton<PeriodicFileScanner>();
        collection.AddSingleton<MareCharaFileManager>();
        collection.AddSingleton<GameObjectHandlerFactory>();
        collection.AddSingleton<PerformanceCollectorService>();
        collection.AddSingleton<HubFactory>();
        collection.AddSingleton<FileUploadManager>();
        collection.AddSingleton<FileTransferOrchestrator>();
        collection.AddSingleton<FileDownloadManagerFactory>();

        // factoried singletons
        collection.AddSingleton((s) => new DalamudUtil(s.GetRequiredService<ILogger<DalamudUtil>>(),
            clientState, objectTable, framework, condition, gameData,
            s.GetRequiredService<MareMediator>(), s.GetRequiredService<PerformanceCollectorService>()));
        collection.AddSingleton((s) => new IpcManager(s.GetRequiredService<ILogger<IpcManager>>(),
            pluginInterface, s.GetRequiredService<DalamudUtil>(), s.GetRequiredService<MareMediator>()));
        collection.AddSingleton((s) => new NotificationService(s.GetRequiredService<ILogger<NotificationService>>(),
            s.GetRequiredService<MareMediator>(), pluginInterface.UiBuilder, chatGui, s.GetRequiredService<MareConfigService>()));

        // add ui stuff
        collection.AddSingleton<UiShared>();
        collection.AddSingleton<SettingsUi>();
        collection.AddSingleton<CompactUi>();
        collection.AddSingleton<GposeUi>();
        collection.AddSingleton<IntroUi>();
        collection.AddSingleton<DownloadUi>();

        // add scoped services
        collection.AddScoped<CacheCreationService>();
        collection.AddScoped<TransientResourceManager>();
        collection.AddScoped<PlayerDataFactory>();
        collection.AddScoped<OnlinePlayerManager>();

        // set up remaining things and launch mareplugin
        var serviceProvider = collection.BuildServiceProvider(new ServiceProviderOptions() { ValidateOnBuild = true, ValidateScopes = true });

        _pluginLogger = serviceProvider.GetRequiredService<ILogger<Plugin>>();
        var version = Assembly.GetExecutingAssembly().GetName().Version!;
        _pluginLogger.LogDebug("Launching {name} {major}.{minor}.{build}", Name, version.Major, version.Minor, version.Build);

        serviceProvider.GetRequiredService<Dalamud.Localization>().SetupWithLangCode("en");
        serviceProvider.GetRequiredService<DalamudPluginInterface>().UiBuilder.DisableGposeUiHide = true;

        var mediator = serviceProvider.GetRequiredService<MareMediator>();
        var logger = serviceProvider.GetRequiredService<ILogger<MarePlugin>>();
        _plugin = new MarePlugin(logger, serviceProvider, mediator);
    }

    public void Dispose()
    {
        _pluginLogger.LogTrace("Disposing {type}", GetType());
        _plugin.Dispose();
    }
}
