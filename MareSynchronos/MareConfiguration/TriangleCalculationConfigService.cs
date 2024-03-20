using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class TriangleCalculationConfigService : ConfigurationServiceBase<TriangleCalculationConfig>
{
    public const string ConfigName = "trianglecache.json";

    public TriangleCalculationConfigService(string configDir) : base(configDir) { }

    protected override string ConfigurationName => ConfigName;
}