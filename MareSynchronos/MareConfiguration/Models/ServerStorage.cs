namespace MareSynchronos.MareConfiguration.Models;

[Serializable]
public class ServerStorage
{
    public List<Authentication> Authentications { get; set; } = new();
    public bool FullPause { get; set; } = false;
    public Dictionary<int, SecretKey> SecretKeys { get; set; } = new();
    public string ServerName { get; set; } = string.Empty;
    public string ServerUri { get; set; } = string.Empty;
}