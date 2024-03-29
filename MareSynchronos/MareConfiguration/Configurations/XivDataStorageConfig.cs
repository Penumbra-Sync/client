using System.Collections.Concurrent;

namespace MareSynchronos.MareConfiguration.Configurations;

public class XivDataStorageConfig : IMareConfiguration
{
    public ConcurrentDictionary<string, long> TriangleDictionary { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, List<List<ushort>>> BoneDictionary { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; set; } = 0;
}