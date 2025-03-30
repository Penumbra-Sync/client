using Microsoft.AspNetCore.Http.Connections;

namespace MareSynchronos.MareConfiguration.Models;

[Serializable]
public class ServerStorage
{
    public List<Authentication> Authentications { get; set; } = [];
    public bool FullPause { get; set; } = false;
    public Dictionary<int, SecretKey> SecretKeys { get; set; } = [];
    public string ServerName { get; set; } = string.Empty;
    public string ServerUri { get; set; } = string.Empty;
    public bool UseOAuth2 { get; set; } = false;
    public string? OAuthToken { get; set; } = null;
    public HttpTransportType HttpTransportType { get; set; } = HttpTransportType.WebSockets;
    public bool ForceWebSockets { get; set; } = false;
}