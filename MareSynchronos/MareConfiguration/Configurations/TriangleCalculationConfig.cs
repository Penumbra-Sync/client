using System.Collections.Concurrent;

namespace MareSynchronos.MareConfiguration.Configurations;

public class TriangleCalculationConfig : IMareConfiguration
{
    public ConcurrentDictionary<string, long> TriangleDictionary { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; set; } = 0;
}