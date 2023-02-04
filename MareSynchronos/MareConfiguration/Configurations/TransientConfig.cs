namespace MareSynchronos.MareConfiguration.Configurations;

public class TransientConfig : IMareConfiguration
{
    public int Version { get; set; } = 0;

    public Dictionary<string, HashSet<string>> PlayerPersistentTransientCache { get; set; } = new(StringComparer.Ordinal);

}