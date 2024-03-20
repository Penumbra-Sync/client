namespace MareSynchronos.MareConfiguration.Configurations;

public class TransientConfig : IMareConfiguration
{
    public Dictionary<string, HashSet<string>> PlayerPersistentTransientCache { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 0;
}
