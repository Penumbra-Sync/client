using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class ServerTagConfigService : ConfigurationServiceBase<ServerTagConfig>
{
    public const string ConfigName = "servertags.json";
    protected override string ConfigurationName => ConfigName;
    public ServerTagConfigService(string configDir) : base(configDir) { }
}