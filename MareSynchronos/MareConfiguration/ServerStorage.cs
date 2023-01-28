namespace MareSynchronos.MareConfiguration;

[Serializable]
public class ServerStorage
{
    public string ServerUri { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public List<Authentication> Authentications { get; set; } = new();
    public Dictionary<string, string> UidServerComments { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> GidServerComments { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> UidServerPairedUserTags = new(StringComparer.Ordinal);
    public HashSet<string> ServerAvailablePairTags { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> OpenPairTags { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<int, SecretKey> SecretKeys { get; set; } = new();
}
