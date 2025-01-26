using MareSynchronos.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.MareConfiguration;

public class ConfigurationMigrator(ILogger<ConfigurationMigrator> logger, TransientConfigService transientConfigService,
    ServerConfigService serverConfigService) : IHostedService
{
    private readonly ILogger<ConfigurationMigrator> _logger = logger;

    public void Migrate()
    {
        if (transientConfigService.Current.Version == 0)
        {
            _logger.LogInformation("Migrating Transient Config V0 => V1");
            transientConfigService.Current.TransientConfigs.Clear();
            transientConfigService.Current.Version = 1;
            transientConfigService.Save();
        }

        if (serverConfigService.Current.Version == 1)
        {
            _logger.LogInformation("Migrating Server Config V1 => V2");
            var centralServer = serverConfigService.Current.ServerStorage.Find(f => f.ServerName.Equals("Lunae Crescere Incipientis (Central Server EU)", StringComparison.Ordinal));
            if (centralServer != null)
            {
                centralServer.ServerName = ApiController.MainServer;
            }
            serverConfigService.Current.Version = 2;
            serverConfigService.Save();
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
}
