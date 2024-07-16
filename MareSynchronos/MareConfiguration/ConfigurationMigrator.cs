using Dalamud.Plugin;
using MareSynchronos.MareConfiguration.Configurations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MareSynchronos.MareConfiguration;

public class ConfigurationMigrator(ILogger<ConfigurationMigrator> logger, IDalamudPluginInterface pi) : IHostedService
{
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

    private string ConfigurationPath(string configName) => Path.Combine(pi.ConfigDirectory.FullName, configName);
}
