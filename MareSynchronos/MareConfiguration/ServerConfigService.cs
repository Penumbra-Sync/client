using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class ServerConfigService : ConfigurationServiceBase<ServerConfig>
{
    public const string ConfigName = "server.json";
    protected override string ConfigurationName => ConfigName;
    public ServerConfigService(string configDir) : base(configDir) { }
}
