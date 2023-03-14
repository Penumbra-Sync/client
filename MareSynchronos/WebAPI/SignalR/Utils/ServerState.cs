namespace MareSynchronos.WebAPI.SignalR.Utils;

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
