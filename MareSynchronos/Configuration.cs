using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using MareSynchronos.WebAPI;
using Newtonsoft.Json;

namespace MareSynchronos
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public string CacheFolder { get; set; } = string.Empty;
        public Dictionary<string, string> ClientSecret { get; set; } = new();
        public Dictionary<string, string> UidComments { get; set; } = new();
        private string _apiUri = string.Empty;
        public string ApiUri
        {
            get => string.IsNullOrEmpty(_apiUri) ? ApiController.MainServiceUri : _apiUri;
            set => _apiUri = value;
        }
        public bool UseCustomService { get; set; }
        public bool InitialScanComplete { get; set; }
        public bool AcceptedAgreement { get; set; }
        private int _maxParallelScan = 10;
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

        [JsonIgnore]
        public bool HasValidSetup => AcceptedAgreement && InitialScanComplete && !string.IsNullOrEmpty(CacheFolder) &&
                                     Directory.Exists(CacheFolder) && ClientSecret.ContainsKey(ApiUri);

        // the below exist just to make saving less cumbersome

        [NonSerialized]
        private DalamudPluginInterface? _pluginInterface;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this._pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this._pluginInterface!.SavePluginConfig(this);
        }
    }
}
