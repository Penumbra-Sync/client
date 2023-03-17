using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.WebAPI;

namespace MareSynchronos.MareConfiguration.Configurations.Obsolete;

[Serializable]
[Obsolete]
public class ServerConfigV0 : IMareConfiguration
{
    public string CurrentServer { get; set; } = string.Empty;

    public Dictionary<string, ServerStorage> ServerStorage { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { ApiController.MainServiceUri, new ServerStorage() { ServerName = ApiController.MainServer, ServerUri = ApiController.MainServiceUri } },
    };

    public int Version { get; set; } = 0;
}
