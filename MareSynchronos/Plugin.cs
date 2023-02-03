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
    private readonly MarePlugin plugin;
    public string Name => "Mare Synchronos";

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
        collection.AddSingleton<MareCharaFileManager>();
        collection.AddSingleton<NotificationService>();
        collection.AddSingleton<TransferManager>();

        collection.AddSingleton<UiShared>();
        collection.AddSingleton<SettingsUi>();
        collection.AddSingleton<CompactUi>();
        collection.AddSingleton<GposeUi>();
        collection.AddSingleton<IntroUi>();
        collection.AddSingleton<DownloadUi>();

        collection.AddScoped<CacheCreationService>();
        collection.AddScoped<TransientResourceManager>();
        collection.AddScoped<CharacterDataFactory>();
        collection.AddScoped<OnlinePlayerManager>();

        var serviceProvider = collection.BuildServiceProvider(new ServiceProviderOptions() { ValidateOnBuild = true, ValidateScopes = true });

        serviceProvider.GetRequiredService<Dalamud.Localization>().SetupWithLangCode("en");
        serviceProvider.GetRequiredService<DalamudPluginInterface>().UiBuilder.DisableGposeUiHide = true;

        var mediator = serviceProvider.GetRequiredService<MareMediator>();
        plugin = new MarePlugin(serviceProvider, mediator);
    }

    public void Dispose()
    {
        Logger.Verbose($"Disposing {GetType()}");
        plugin.Dispose();
    }
}
