using Dalamud.Plugin;
using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class TransientConfigService : ConfigurationServiceBase<TransientConfig>
{
    public const string ConfigName = "transient.json";
    protected override string ConfigurationName => ConfigName;
    public TransientConfigService(string configDir) : base(configDir) { }
}
