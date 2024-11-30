using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class ServerTagConfigService : ConfigurationServiceBase<ServerTagConfig>
{
    public const string ConfigName = "servertags.json";

    public ServerTagConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}