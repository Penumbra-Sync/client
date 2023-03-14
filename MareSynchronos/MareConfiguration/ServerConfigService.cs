using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class ServerConfigService : ConfigurationServiceBase<ServerConfig>
{
    public const string ConfigName = "server.json";

    public ServerConfigService(string configDir) : base(configDir)
    {
    }

    protected override string ConfigurationName => ConfigName;
}