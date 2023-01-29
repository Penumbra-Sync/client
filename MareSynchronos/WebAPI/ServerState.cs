namespace MareSynchronos.WebAPI;

public enum ServerState
{
    Offline,
    Connecting,
    Reconnecting,
    Disconnected,
    Connected,
    Unauthorized,
    VersionMisMatch,
    RateLimited,
    NoSecretKey,
}
