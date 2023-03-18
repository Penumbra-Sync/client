using Dalamud.Plugin;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.MareConfiguration.Configurations.Obsolete;
using MareSynchronos.MareConfiguration.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MareSynchronos.MareConfiguration;
#pragma warning disable CS0618 // ignore Obsolete tag, the point of this migrator is to migrate obsolete configs to new ones
#pragma warning disable CS0612 // ignore Obsolete tag, the point of this migrator is to migrate obsolete configs to new ones

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
        if (_pi.GetPluginConfig() is Configuration oldConfig)
        {
            _logger.LogInformation("Migrating Configuration from old config style to 1");

            var config = oldConfig.ToMareConfig(_logger);
            File.Move(_pi.ConfigFile.FullName, _pi.ConfigFile.FullName + ".old", overwrite: true);
            MigrateMareConfigV0ToV1(config);
        }

        if (File.Exists(ConfigurationPath(MareConfigService.ConfigName)))
        {
            try
            {
                var mareConfig = JsonConvert.DeserializeObject<MareConfigV0>(File.ReadAllText(ConfigurationPath(MareConfigService.ConfigName)))!;

                if (mareConfig.Version == 0)
                {
                    MigrateMareConfigV0ToV1(mareConfig);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to migrate, skipping", ex);
            }
        }

        if (File.Exists(ConfigurationPath(ServerConfigService.ConfigName)))
        {
            try
            {
                var serverConfig = JsonConvert.DeserializeObject<ServerConfigV0>(File.ReadAllText(ConfigurationPath(ServerConfigService.ConfigName)))!;

                if (serverConfig.Version == 0)
                {
                    MigrateServerConfigV0toV1(serverConfig);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to migrate ServerConfig", ex);
            }
        }
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

    private static void SaveConfig(IMareConfiguration config, string path)
    {
        File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
    }

    private string ConfigurationPath(string configName) => Path.Combine(_pi.ConfigDirectory.FullName, configName);

    private void MigrateMareConfigV0ToV1(MareConfigV0 mareConfigV0)
    {
        _logger.LogInformation("Migrating Configuration from version 0 to 1");
        if (File.Exists(ConfigurationPath(MareConfigService.ConfigName)))
            File.Copy(ConfigurationPath(MareConfigService.ConfigName), ConfigurationPath(MareConfigService.ConfigName) + ".migrated." + mareConfigV0.Version + ".bak", overwrite: true);

        MareConfig mareConfigV1 = mareConfigV0.ToV1();

        var serverConfig = new ServerConfig()
        {
            ServerStorage = mareConfigV0.ServerStorage.Select(p => p.Value.ToV1()).ToList()
        };
        serverConfig.CurrentServer = Array.IndexOf(serverConfig.ServerStorage.Select(s => s.ServerUri).ToArray(), mareConfigV0.CurrentServer);
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

    private void MigrateServerConfigV0toV1(ServerConfigV0 serverConfigV0)
    {
        _logger.LogInformation("Migration Server Configuration from version 0 to 1");
        if (File.Exists(ConfigurationPath(ServerConfigService.ConfigName)))
            File.Copy(ConfigurationPath(ServerConfigService.ConfigName), ConfigurationPath(ServerConfigService.ConfigName) + ".migrated." + serverConfigV0.Version + ".bak", overwrite: true);

        ServerConfig migrated = new();

        var currentServer = serverConfigV0.CurrentServer;
        var currentServerIdx = Array.IndexOf(serverConfigV0.ServerStorage.Keys.ToArray(), currentServer);

        if (currentServerIdx == -1) currentServerIdx = 0;

        migrated.CurrentServer = currentServerIdx;
        migrated.ServerStorage = new();

        foreach (var server in serverConfigV0.ServerStorage)
        {
            migrated.ServerStorage.Add(server.Value);
        }

        SaveConfig(migrated, ConfigurationPath(ServerConfigService.ConfigName));
    }
}

#pragma warning restore CS0612 // ignore Obsolete tag, the point of this migrator is to migrate obsolete configs to new ones
#pragma warning restore CS0618 // ignore Obsolete tag, the point of this migrator is to migrate obsolete configs to new ones