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
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.User;

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
            collection.AddSingleton<ServerConfigurationManager>();
            collection.AddSingleton<PairManager>();
            collection.AddSingleton<ApiController>();
            collection.AddSingleton<MareCharaFileManager>();
            collection.AddSingleton<PerformanceCollectorService>();
            collection.AddSingleton<HubFactory>();
            collection.AddSingleton<FileUploadManager>();
            collection.AddSingleton<FileTransferOrchestrator>();
            collection.AddSingleton<MarePlugin>();
            collection.AddSingleton((s) => new DalamudUtilService(s.GetRequiredService<ILogger<DalamudUtilService>>(),
                clientState, objectTable, framework, condition, gameData,
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<PerformanceCollectorService>()));
            collection.AddSingleton((s) => new IpcManager(s.GetRequiredService<ILogger<IpcManager>>(),
                pluginInterface, s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>()));

            collection.AddSingleton((s) => new MareConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new NotesConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerTagConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new TransientConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ConfigurationMigrator(s.GetRequiredService<ILogger<ConfigurationMigrator>>(), pluginInterface));

            // func factory method singletons
            collection.AddSingleton(s =>
                new Func<ObjectKind, Func<nint>, bool, GameObjectHandler>((o, f, b)
                    => new GameObjectHandler(s.GetRequiredService<ILogger<GameObjectHandler>>(),
                        s.GetRequiredService<PerformanceCollectorService>(),
                        s.GetRequiredService<MareMediator>(),
                        s.GetRequiredService<DalamudUtilService>(),
                        o, f, b)));
            collection.AddSingleton(s =>
                new Func<OnlineUserIdentDto, CachedPlayer>((o)
                    => new CachedPlayer(s.GetRequiredService<ILogger<CachedPlayer>>(),
                        o,
                        s.GetRequiredService<Func<ObjectKind, Func<nint>, bool, GameObjectHandler>>(),
                        s.GetRequiredService<IpcManager>(),
                        s.GetRequiredService<Func<FileDownloadManager>>().Invoke(),
                        s.GetRequiredService<DalamudUtilService>(),
                        s.GetRequiredService<IHostApplicationLifetime>(),
                        s.GetRequiredService<FileCacheManager>(),
                        s.GetRequiredService<MareMediator>())));
            collection.AddSingleton(s =>
                new Func<Pair>(()
                    => new Pair(s.GetRequiredService<ILogger<Pair>>(),
                        s.GetRequiredService<Func<OnlineUserIdentDto, CachedPlayer>>(),
                        s.GetRequiredService<MareConfigService>(),
                        s.GetRequiredService<ServerConfigurationManager>())));
            collection.AddSingleton(s =>
                new Func<FileDownloadManager>(()
                    => new FileDownloadManager(s.GetRequiredService<ILogger<FileDownloadManager>>(),
                        s.GetRequiredService<MareMediator>(),
                        s.GetRequiredService<FileTransferOrchestrator>(),
                        s.GetRequiredService<FileCacheManager>())));

            // add scoped services
            collection.AddScoped<PeriodicFileScanner>();
            collection.AddScoped<WindowMediatorSubscriberBase, SettingsUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, CompactUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, GposeUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, IntroUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, DownloadUi>();
            collection.AddScoped<CacheCreationService>();
            collection.AddScoped<TransientResourceManager>();
            collection.AddScoped<PlayerDataFactory>();
            collection.AddScoped<OnlinePlayerManager>();
            collection.AddScoped((s) => new UiService(s.GetRequiredService<ILogger<UiService>>(), pluginInterface, s.GetRequiredService<MareConfigService>(),
                s.GetRequiredService<WindowSystem>(), s.GetServices<WindowMediatorSubscriberBase>(), s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MareMediator>()));
            collection.AddScoped((s) => new CommandManagerService(commandManager, s.GetRequiredService<PerformanceCollectorService>(), s.GetRequiredService<UiService>(),
                s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<PeriodicFileScanner>(), s.GetRequiredService<ApiController>(), s.GetRequiredService<MareMediator>()));
            collection.AddScoped((s) => new NotificationService(s.GetRequiredService<ILogger<NotificationService>>(),
                s.GetRequiredService<MareMediator>(), pluginInterface.UiBuilder, chatGui, s.GetRequiredService<MareConfigService>()));
            collection.AddScoped((s) => new UiSharedService(s.GetRequiredService<ILogger<UiSharedService>>(), s.GetRequiredService<IpcManager>(), s.GetRequiredService<ApiController>(),
                s.GetRequiredService<PeriodicFileScanner>(), s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MareConfigService>(), s.GetRequiredService<DalamudUtilService>(),
                pluginInterface, s.GetRequiredService<Dalamud.Localization>(), s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<MareMediator>()));

            collection.AddHostedService(p => p.GetRequiredService<DalamudUtilService>());
            collection.AddHostedService(p => p.GetRequiredService<ConfigurationMigrator>());
            collection.AddHostedService(p => p.GetRequiredService<PerformanceCollectorService>());
            collection.AddHostedService(p => p.GetRequiredService<MarePlugin>());
        })
        .Build()
        .RunAsync(_pluginCts.Token);
    }

    public void Dispose()
    {
        _pluginCts.Cancel();
        _pluginCts.Dispose();
    }
}
