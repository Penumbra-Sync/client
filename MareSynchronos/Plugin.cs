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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace MareSynchronos;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Mare Synchronos";
    private readonly CancellationTokenSource _pluginCts = new();

    public Plugin(DalamudPluginInterface pluginInterface, CommandManager commandManager, DataManager gameData,
        Framework framework, ObjectTable objectTable, ClientState clientState, Condition condition, ChatGui chatGui)
    {
        new HostBuilder()
        .UseContentRoot(pluginInterface.ConfigDirectory.FullName)
        .ConfigureLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddDalamudLogging();
            lb.SetMinimumLevel(LogLevel.Trace);
        })
        .ConfigureServices(collection =>
        {
            collection.AddSingleton(new WindowSystem("MareSynchronos"));
            collection.AddSingleton<FileDialogManager>();
            collection.AddSingleton(new Dalamud.Localization("MareSynchronos.Localization.", "", useEmbedded: true));

            // add mare related singletons
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
            collection.AddSingleton((s) => new MareConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new NotesConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerTagConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new TransientConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ConfigurationMigrator(s.GetRequiredService<ILogger<ConfigurationMigrator>>(), pluginInterface));
            collection.AddSingleton((s) => new UiShared(s.GetRequiredService<ILogger<UiShared>>(), s.GetRequiredService<IpcManager>(), s.GetRequiredService<ApiController>(),
                s.GetRequiredService<PeriodicFileScanner>(), s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MareConfigService>(), s.GetRequiredService<DalamudUtil>(),
                pluginInterface, s.GetRequiredService<Dalamud.Localization>(), s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<MareMediator>()));
            collection.AddSingleton((s) => new MarePlugin(s.GetRequiredService<ILogger<MarePlugin>>(),
                pluginInterface,
                s.GetRequiredService<PerformanceCollectorService>(),
                commandManager,
                s.GetRequiredService<MareConfigService>(),
                s.GetRequiredService<ServerConfigurationManager>(),
                s.GetRequiredService<ApiController>(),
                s.GetRequiredService<PeriodicFileScanner>(),
                s.GetRequiredService<IServiceProvider>(),
                s.GetRequiredService<MareMediator>()));


            // add ui stuff
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

            collection.AddHostedService(p => p.GetRequiredService<MarePlugin>());
            collection.AddHostedService(p => p.GetRequiredService<NotificationService>());
            collection.AddHostedService(p => p.GetRequiredService<DownloadUi>());
            collection.AddHostedService(p => p.GetRequiredService<GposeUi>());
            collection.AddHostedService(p => p.GetRequiredService<SettingsUi>());
            collection.AddHostedService(p => p.GetRequiredService<CompactUi>());
            collection.AddHostedService(p => p.GetRequiredService<IntroUi>());
            collection.AddHostedService(p => p.GetRequiredService<ConfigurationMigrator>());
        })
        .Build()
        .RunAsync(_pluginCts.Token);
    }

    public void Dispose()
    {
        _pluginCts.Cancel();
    }
}
