using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class XivDataStorageService : ConfigurationServiceBase<XivDataStorageConfig>
{
    public const string ConfigName = "xivdatastorage.json";

    public XivDataStorageService(string configDir) : base(configDir) { }

    protected override string ConfigurationName => ConfigName;
}