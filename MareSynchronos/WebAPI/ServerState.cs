namespace MareSynchronos.WebAPI;

public enum ServerState
{
    Offline,
    Disconnected,
    Connected,
    Unauthorized,
    VersionMisMatch,
    RateLimited
}
