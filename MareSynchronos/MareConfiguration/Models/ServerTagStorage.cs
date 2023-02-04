namespace MareSynchronos.MareConfiguration.Models;

[Serializable]
public class ServerTagStorage
{
    public Dictionary<string, List<string>> UidServerPairedUserTags = new(StringComparer.Ordinal);
    public HashSet<string> ServerAvailablePairTags { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> OpenPairTags { get; set; } = new(StringComparer.Ordinal);
}
