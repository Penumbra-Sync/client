using Dalamud.Plugin;
using MareSynchronos.MareConfiguration.Configurations;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.MareConfiguration;

public class ServerTagConfigService : ConfigurationServiceBase<ServerTagConfig>
{
    public const string ConfigName = "servertags.json";
    protected override string ConfigurationName => ConfigName;
    public ServerTagConfigService(DalamudPluginInterface pluginInterface) : base(pluginInterface) { }
}