namespace MareSynchronos.MareConfiguration.Models;

[Serializable]
public class ServerStorage
{
    public string ServerUri { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public List<Authentication> Authentications { get; set; } = new();
    public Dictionary<int, SecretKey> SecretKeys { get; set; } = new();
    public bool FullPause { get; set; } = false;
}
