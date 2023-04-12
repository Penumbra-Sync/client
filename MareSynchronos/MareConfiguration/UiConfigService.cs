using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class UiConfigService : ConfigurationServiceBase<UiConfig>
{
    public const string ConfigName = "ui.json";

    public UiConfigService(string configDir) : base(configDir)
    {
    }

    protected override string ConfigurationName => ConfigName;
}
