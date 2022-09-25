using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos
{
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
                : new Dictionary<string, string>();
        }

        public static void SetCurrentServerUidComment(this Configuration configuration, string uid, string comment)
        {
            if (!configuration.UidServerComments.ContainsKey(configuration.ApiUri))
            {
                configuration.UidServerComments[configuration.ApiUri] = new Dictionary<string, string>();
            }

            configuration.UidServerComments[configuration.ApiUri][uid] = comment;
        }
    }

    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        private string _apiUri = string.Empty;
        private int _maxParallelScan = 10;
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
        public Dictionary<string, string> ClientSecret { get; set; } = new();
        public Dictionary<string, string> CustomServerList { get; set; } = new();
        public int MaxLocalCacheInGiB { get; set; } = 20;
        public bool ReverseUserSort { get; set; } = true;

        public int TimeSpanBetweenScansInSeconds { get; set; } = 30;
        public bool FileScanPaused { get; set; } = false;

        public bool InitialScanComplete { get; set; } = false;

        public bool FullPause { get; set; } = false;
        public Dictionary<string, Dictionary<string, string>> UidServerComments { get; set; } = new();

        public Dictionary<string, string> UidComments { get; set; } = new();
        public int Version { get; set; } = 5;

        public bool ShowTransferWindow { get; set; } = true;

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
                ApiUri = ApiUri.Replace("https", "wss");
                foreach (var kvp in ClientSecret.ToList())
                {
                    var newKey = kvp.Key.Replace("https", "wss");
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
                UidServerComments.Add(ApiUri, UidComments.ToDictionary(k => k.Key, k => k.Value));
                UidComments.Clear();
                Save();
            }

            if (Version == 1)
            {
                Logger.Debug("Migrating Configuration from V1 to V2");
                ApiUri = ApiUri.Replace("5001", "5000");
                foreach (var kvp in ClientSecret.ToList())
                {
                    var newKey = kvp.Key.Replace("5001", "5000");
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
                    var newKey = kvp.Key.Replace("5001", "5000");
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

                ApiUri = ApiUri.Replace("wss://v2202207178628194299.powersrv.de:6871", "wss://v2202207178628194299.powersrv.de:6872");
                foreach (var kvp in ClientSecret.ToList())
                {
                    var newKey = kvp.Key.Replace("wss://v2202207178628194299.powersrv.de:6871", "wss://v2202207178628194299.powersrv.de:6872");
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
                    var newKey = kvp.Key.Replace("wss://v2202207178628194299.powersrv.de:6871", "wss://v2202207178628194299.powersrv.de:6872");
                    UidServerComments.Remove(kvp.Key);
                    UidServerComments.Add(newKey, kvp.Value);
                }

                Version = 4;
                Save();
            }

            if (Version == 4)
            {
                Logger.Debug("Migrating Configuration from V4 to V5");

                ApiUri = ApiUri.Replace("wss://v2202207178628194299.powersrv.de:6872", "wss://maresynchronos.com");
                ClientSecret.Remove("wss://v2202207178628194299.powersrv.de:6872");
                UidServerComments.Remove("wss://v2202207178628194299.powersrv.de:6872");

                Version = 5;
                Save();
            }
        }
    }
}
