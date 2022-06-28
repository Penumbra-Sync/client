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

        public bool AcceptedAgreement { get; set; } = false;
        public string ApiUri
        {
            get => string.IsNullOrEmpty(_apiUri) ? ApiController.MainServiceUri : _apiUri;
            set => _apiUri = value;
        }

        public string CacheFolder { get; set; } = string.Empty;
        public Dictionary<string, string> ClientSecret { get; set; } = new();
        public Dictionary<string, string> CustomServerList { get; set; } = new();

        public bool InitialScanComplete { get; set; } = false;
        public int MaxParallelScan
        {
            get => _maxParallelScan;
            set
            {
                _maxParallelScan = value switch
                {
                    < 0 => 1,
                    > 20 => 10,
                    _ => value
                };
            }
        }

        public bool FullPause { get; set; } = false;
        public Dictionary<string, Dictionary<string, string>> UidServerComments { get; set; } = new();

        public Dictionary<string, string> UidComments { get; set; } = new();
        public int Version { get; set; } = 1;

        public bool ShowTransferWindow { get; set; } = true;

        // the below exist just to make saving less cumbersome
        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            _pluginInterface = pluginInterface;
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
        }
    }
}
