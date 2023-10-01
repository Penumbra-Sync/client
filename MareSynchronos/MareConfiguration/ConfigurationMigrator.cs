using Dalamud.Plugin;
using MareSynchronos.MareConfiguration.Configurations;
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
        // currently nothing to migrate
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
}

#pragma warning restore CS0612 // ignore Obsolete tag, the point of this migrator is to migrate obsolete configs to new ones
#pragma warning restore CS0618 // ignore Obsolete tag, the point of this migrator is to migrate obsolete configs to new ones