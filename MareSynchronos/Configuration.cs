using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos;

public static class ConfigurationExtensions
{
    public static bool HasValidSetup(this Configuration configuration)
    {
        return configuration.AcceptedAgreement && configuration.InitialScanComplete
                    && !string.IsNullOrEmpty(configuration.CacheFolder)
                    && Directory.Exists(configuration.CacheFolder)
                    && configuration.ClientSecret.ContainsKey(configuration.ApiUri);
    }

    public static Dictionary<string, string> GetCurrentServerUidComments(this Configuration configuration)
    {
        return configuration.UidServerComments.ContainsKey(configuration.ApiUri)
            ? configuration.UidServerComments[configuration.ApiUri]
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static Dictionary<string, string> GetCurrentServerGidComments(this Configuration configuration)
    {
        return configuration.GidServerComments.ContainsKey(configuration.ApiUri)
            ? configuration.GidServerComments[configuration.ApiUri]
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static void SetCurrentServerGidComment(this Configuration configuration, string gid, string comment)
    {
        if (!configuration.GidServerComments.ContainsKey(configuration.ApiUri))
        {
            configuration.GidServerComments[configuration.ApiUri] = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        configuration.GidServerComments[configuration.ApiUri][gid] = comment;
    }

    public static void SetCurrentServerUidComment(this Configuration configuration, string uid, string comment)
    {
        if (!configuration.UidServerComments.ContainsKey(configuration.ApiUri))
        {
            configuration.UidServerComments[configuration.ApiUri] = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        configuration.UidServerComments[configuration.ApiUri][uid] = comment;
    }
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    private string _apiUri = string.Empty;
    [NonSerialized]
    private DalamudPluginInterface? _pluginInterface;

    public bool DarkSoulsAgreement { get; set; } = false;
    public bool AcceptedAgreement { get; set; } = false;
    public string ApiUri
    {
        get => string.IsNullOrEmpty(_apiUri) ? ApiController.MainServiceUri : _apiUri;
        set => _apiUri = value;
    }

    public string CacheFolder { get; set; } = string.Empty;
    public Dictionary<string, string> ClientSecret { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> CustomServerList { get; set; } = new(StringComparer.Ordinal);
    public double MaxLocalCacheInGiB { get; set; } = 20;
    public bool ReverseUserSort { get; set; } = false;

    public int TimeSpanBetweenScansInSeconds { get; set; } = 30;
    public bool FileScanPaused { get; set; } = false;

    public bool InitialScanComplete { get; set; } = false;

    public bool FullPause { get; set; } = false;
    public Dictionary<string, Dictionary<string, string>> UidServerComments { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, string>> GidServerComments { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> UidComments { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 5;

    public bool ShowTransferWindow { get; set; } = true;
    public bool OpenPopupOnAdd { get; set; } = false;

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
        if (Version == 0)
        {
            Logger.Debug("Migrating Configuration from V0 to V1");
            Version = 1;
            ApiUri = ApiUri.Replace("https", "wss", StringComparison.Ordinal);
            foreach (var kvp in ClientSecret.ToList())
            {
                var newKey = kvp.Key.Replace("https", "wss", StringComparison.Ordinal);
                ClientSecret.Remove(kvp.Key);
                if (ClientSecret.ContainsKey(newKey))
                {
                    ClientSecret[newKey] = kvp.Value;
                }
                else
                {
                    ClientSecret.Add(newKey, kvp.Value);
                }
            }
            UidServerComments.Add(ApiUri, UidComments.ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal));
            UidComments.Clear();
            Save();
        }

        if (Version == 1)
        {
            Logger.Debug("Migrating Configuration from V1 to V2");
            ApiUri = ApiUri.Replace("5001", "5000", StringComparison.Ordinal);
            foreach (var kvp in ClientSecret.ToList())
            {
                var newKey = kvp.Key.Replace("5001", "5000", StringComparison.Ordinal);
                ClientSecret.Remove(kvp.Key);
                if (ClientSecret.ContainsKey(newKey))
                {
                    ClientSecret[newKey] = kvp.Value;
                }
                else
                {
                    ClientSecret.Add(newKey, kvp.Value);
                }
            }

            foreach (var kvp in UidServerComments.ToList())
            {
                var newKey = kvp.Key.Replace("5001", "5000", StringComparison.Ordinal);
                UidServerComments.Remove(kvp.Key);
                UidServerComments.Add(newKey, kvp.Value);
            }

            Version = 2;
            Save();
        }

        if (Version == 2)
        {
            Logger.Debug("Migrating Configuration from V2 to V3");
            ApiUri = "wss://v2202207178628194299.powersrv.de:6871";
            ClientSecret.Clear();
            UidServerComments.Clear();

            Version = 3;
            Save();
        }

        if (Version == 3)
        {
            Logger.Debug("Migrating Configuration from V3 to V4");

            ApiUri = ApiUri.Replace("wss://v2202207178628194299.powersrv.de:6871", "wss://v2202207178628194299.powersrv.de:6872", StringComparison.Ordinal);
            foreach (var kvp in ClientSecret.ToList())
            {
                var newKey = kvp.Key.Replace("wss://v2202207178628194299.powersrv.de:6871", "wss://v2202207178628194299.powersrv.de:6872", StringComparison.Ordinal);
                ClientSecret.Remove(kvp.Key);
                if (ClientSecret.ContainsKey(newKey))
                {
                    ClientSecret[newKey] = kvp.Value;
                }
                else
                {
                    ClientSecret.Add(newKey, kvp.Value);
                }
            }

            foreach (var kvp in UidServerComments.ToList())
            {
                var newKey = kvp.Key.Replace("wss://v2202207178628194299.powersrv.de:6871", "wss://v2202207178628194299.powersrv.de:6872", StringComparison.Ordinal);
                UidServerComments.Remove(kvp.Key);
                UidServerComments.Add(newKey, kvp.Value);
            }

            Version = 4;
            Save();
        }

        if (Version == 4)
        {
            Logger.Debug("Migrating Configuration from V4 to V5");

            ApiUri = ApiUri.Replace("wss://v2202207178628194299.powersrv.de:6872", "wss://maresynchronos.com", StringComparison.Ordinal);
            ClientSecret.Remove("wss://v2202207178628194299.powersrv.de:6872");
            UidServerComments.Remove("wss://v2202207178628194299.powersrv.de:6872");

            Version = 5;
            Save();
        }
    }
}
