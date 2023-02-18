using Dalamud.Plugin;
using MareSynchronos.MareConfiguration.Configurations;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.MareConfiguration;

public class TransientConfigService : ConfigurationServiceBase<TransientConfig>
{
    public const string ConfigName = "transient.json";
    protected override string ConfigurationName => ConfigName;
    public TransientConfigService(DalamudPluginInterface pluginInterface) : base(pluginInterface) { }
}
