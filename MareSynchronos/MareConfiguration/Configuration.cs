using Dalamud.Configuration;
using Dalamud.Plugin;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.MareConfiguration;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 6;
    [NonSerialized]
    private DalamudPluginInterface? _pluginInterface;
    public Dictionary<string, ServerStorage> ServerStorage { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { ApiController.MainServiceUri, new ServerStorage() { ServerName = ApiController.MainServer, ServerUri = ApiController.MainServiceUri } }
    };
    public bool AcceptedAgreement { get; set; } = false;
    public string CacheFolder { get; set; } = string.Empty;
    public double MaxLocalCacheInGiB { get; set; } = 20;
    public bool ReverseUserSort { get; set; } = false;
    public int TimeSpanBetweenScansInSeconds { get; set; } = 30;
    public bool FileScanPaused { get; set; } = false;
    public bool InitialScanComplete { get; set; } = false;
    public bool FullPause { get; set; } = false;
    public bool HideInfoMessages { get; set; } = false;
    public bool DisableOptionalPluginWarnings { get; set; } = false;
    public bool OpenGposeImportOnGposeStart { get; set; } = false;
    public bool ShowTransferWindow { get; set; } = true;
    public bool OpenPopupOnAdd { get; set; } = true;
    public string CurrentServer { get; set; } = string.Empty;

    [Obsolete]
    private string _apiUri = string.Empty;
    [Obsolete]
    public string ApiUri
    {
        get => string.IsNullOrEmpty(_apiUri) ? ApiController.MainServiceUri : _apiUri;
        set => _apiUri = value;
    }
    [Obsolete]
    public Dictionary<string, string> ClientSecret { get; set; } = new(StringComparer.Ordinal);
    [Obsolete]
    public Dictionary<string, string> CustomServerList { get; set; } = new(StringComparer.Ordinal);
    [Obsolete]
    public Dictionary<string, Dictionary<string, string>> UidServerComments { get; set; } = new(StringComparer.Ordinal);
    [Obsolete]
    public Dictionary<string, Dictionary<string, string>> GidServerComments { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// Each paired user can have multiple tags. Each tag will create a category, and the user will
    /// be displayed into that category.
    /// The dictionary first maps a server URL to a dictionary, and that
    /// dictionary maps the OtherUID of the <see cref="ClientPairDto"/> to a list of tags.
    /// </summary>
    [Obsolete]
    public Dictionary<string, Dictionary<string, List<string>>> UidServerPairedUserTags = new(StringComparer.Ordinal);
    /// <summary>
    /// A dictionary that maps a server URL to the tags the user has added for that server.
    /// </summary>
    [Obsolete]
    public Dictionary<string, HashSet<string>> ServerAvailablePairTags = new(StringComparer.Ordinal);
    [Obsolete]
    public HashSet<string> OpenPairTags = new(StringComparer.Ordinal);


    // the below exist just to make saving less cumbersome
    public void Initialize(DalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;

        if (!Directory.Exists(CacheFolder))
        {
            InitialScanComplete = false;
        }

        Save();
    }

    public void Save()
    {
        _pluginInterface!.SavePluginConfig(this);
    }

    public void Migrate()
    {
        if (Version == 5)
        {
            Logger.Info("Migrating Config to v6");
            // create all server storage based on current clientsecret
            foreach (var secret in ClientSecret)
            {
                Logger.Debug("Migrating " + secret.Key);
                var apiuri = secret.Key;
                var secretkey = secret.Value;
                ServerStorage toAdd = new();
                if (apiuri == ApiController.MainServiceUri)
                {
                    toAdd.ServerUri = ApiController.MainServiceUri;
                    toAdd.ServerName = ApiController.MainServer;
                }
                else
                {
                    toAdd.ServerUri = apiuri;
                    toAdd.ServerName = CustomServerList[apiuri];
                }

                toAdd.SecretKeys[0] = new SecretKey()
                {
                    FriendlyName = "Auto Migrated Secret Key (" + DateTime.Now.ToString("yyyy-MM-dd") + ")",
                    Key = secretkey
                };

                if (GidServerComments.TryGetValue(apiuri, out var gids))
                {
                    toAdd.GidServerComments = gids;
                }
                if (UidServerComments.TryGetValue(apiuri, out var uids))
                {
                    toAdd.UidServerComments = uids;
                }
                if (UidServerPairedUserTags.TryGetValue(apiuri, out var uidtag))
                {
                    toAdd.UidServerPairedUserTags = uidtag;
                }
                if (ServerAvailablePairTags.TryGetValue(apiuri, out var servertag))
                {
                    toAdd.ServerAvailablePairTags = servertag;
                }
                toAdd.OpenPairTags = OpenPairTags;

                ServerStorage[apiuri] = toAdd;
            }

            OpenPairTags.Clear();
            GidServerComments.Clear();
            UidServerComments.Clear();
            UidServerPairedUserTags.Clear();
            ServerAvailablePairTags.Clear();
            ClientSecret.Clear();
            CustomServerList.Clear();

            CurrentServer = ApiUri;
            Version = 6;
        }

        Save();
    }
}
