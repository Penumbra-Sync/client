using Dalamud.Configuration;
using Dalamud.Plugin;
using MareSynchronos.WebAPI;

namespace MareSynchronos.MareConfiguration;

[Serializable]
public class MareConfig : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public Dictionary<string, ServerStorage> ServerStorage { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { ApiController.MainServiceUri, new ServerStorage() { ServerName = ApiController.MainServer, ServerUri = ApiController.MainServiceUri } }
    };
    public Dictionary<string, HashSet<string>> PlayerPersistentTransientCache { get; set; } = new();
    public bool AcceptedAgreement { get; set; } = false;
    public string CacheFolder { get; set; } = string.Empty;
    public double MaxLocalCacheInGiB { get; set; } = 20;
    public bool ReverseUserSort { get; set; } = false;
    public int TimeSpanBetweenScansInSeconds { get; set; } = 30;
    public bool FileScanPaused { get; set; } = false;
    public bool InitialScanComplete { get; set; } = false;
    public bool HideInfoMessages { get; set; } = false;
    public bool DisableOptionalPluginWarnings { get; set; } = false;
    public bool OpenGposeImportOnGposeStart { get; set; } = false;
    public bool ShowTransferWindow { get; set; } = true;
    public bool OpenPopupOnAdd { get; set; } = true;
    public string CurrentServer { get; set; } = string.Empty;
}