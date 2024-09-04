using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class PlayerPerformanceConfigService : ConfigurationServiceBase<PlayerPerformanceConfig>
{
    public const string ConfigName = "playerperformance.json";
    public PlayerPerformanceConfigService(string configDir) : base(configDir) { }

    protected override string ConfigurationName => ConfigName;
}