namespace MareSynchronos.MareConfiguration.Models;

[Serializable]
public class ServerStorage
{
    public List<Authentication> Authentications { get; set; } = [];
    public bool FullPause { get; set; } = false;
    public Dictionary<int, SecretKey> SecretKeys { get; set; } = [];
    public string ServerName { get; set; } = string.Empty;
    public string ServerUri { get; set; } = string.Empty;
}