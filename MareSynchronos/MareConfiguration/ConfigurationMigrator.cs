using Dalamud.Plugin;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.MareConfiguration.Configurations.Obsolete;
using MareSynchronos.MareConfiguration.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MareSynchronos.MareConfiguration;

public class ConfigurationMigrator : IHostedService
{
    private readonly ILogger<ConfigurationMigrator> _logger;
    private readonly DalamudPluginInterface _pi;

    public ConfigurationMigrator(ILogger<ConfigurationMigrator> logger, DalamudPluginInterface pi)
    {
        _logger = logger;
        _pi = pi;
    }

    public void Migrate()
    {
#pragma warning disable CS0618 // ignore Obsolete tag, the point of this migrator is to migrate obsolete configs to new ones
        if (_pi.GetPluginConfig() is Configuration oldConfig)
        {
            _logger.LogInformation("Migrating Configuration from old config style to 1");

            var config = oldConfig.ToMareConfig(_logger);
            File.Move(_pi.ConfigFile.FullName, _pi.ConfigFile.FullName + ".old", overwrite: true);
            MigrateMareConfigV0ToV1(config);
        }

        if (File.Exists(ConfigurationPath(MareConfigService.ConfigName)))
        {
            var mareConfig = JsonConvert.DeserializeObject<MareConfigV0>(File.ReadAllText(ConfigurationPath(MareConfigService.ConfigName)))!;

            if (mareConfig.Version == 0)
            {
                MigrateMareConfigV0ToV1(mareConfig);
            }
        }
    }

    private void MigrateMareConfigV0ToV1(MareConfigV0 mareConfigV0)
    {
        _logger.LogInformation("Migrating Configuration from version 0 to 1");
        if (File.Exists(ConfigurationPath(MareConfigService.ConfigName)))
            File.Copy(ConfigurationPath(MareConfigService.ConfigName), ConfigurationPath(MareConfigService.ConfigName) + ".migrated." + mareConfigV0.Version + ".bak", overwrite: true);

        MareConfig mareConfigV1 = mareConfigV0.ToV1();

        var serverConfig = new ServerConfig()
        {
            CurrentServer = mareConfigV0.CurrentServer,
            ServerStorage = mareConfigV0.ServerStorage.ToDictionary(p => p.Key, p => p.Value.ToV1(), StringComparer.Ordinal)
        };
        var transientConfig = new TransientConfig()
        {
            PlayerPersistentTransientCache = mareConfigV0.PlayerPersistentTransientCache
        };
        var tagConfig = new ServerTagConfig()
        {
            ServerTagStorage = mareConfigV0.ServerStorage.ToDictionary(p => p.Key, p => new ServerTagStorage()
            {
                UidServerPairedUserTags = p.Value.UidServerPairedUserTags.ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.Ordinal),
                OpenPairTags = p.Value.OpenPairTags.ToHashSet(StringComparer.Ordinal),
                ServerAvailablePairTags = p.Value.ServerAvailablePairTags.ToHashSet(StringComparer.Ordinal)
            }, StringComparer.Ordinal)
        };
        var notesConfig = new UidNotesConfig()
        {
            ServerNotes = mareConfigV0.ServerStorage.ToDictionary(p => p.Key, p => new ServerNotesStorage()
            {
                GidServerComments = p.Value.GidServerComments,
                UidServerComments = p.Value.UidServerComments
            }, StringComparer.Ordinal)
        };

        SaveConfig(mareConfigV1, ConfigurationPath(MareConfigService.ConfigName));
        SaveConfig(serverConfig, ConfigurationPath(ServerConfigService.ConfigName));
        SaveConfig(transientConfig, ConfigurationPath(TransientConfigService.ConfigName));
        SaveConfig(tagConfig, ConfigurationPath(ServerTagConfigService.ConfigName));
        SaveConfig(notesConfig, ConfigurationPath(NotesConfigService.ConfigName));
    }
    private string ConfigurationPath(string configName) => Path.Combine(_pi.ConfigDirectory.FullName, configName);


    private static void SaveConfig(IMareConfiguration config, string path)
    {
        File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Migrate();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}