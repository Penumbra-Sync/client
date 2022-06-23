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
        [JsonIgnore]
        public bool HasValidSetup => AcceptedAgreement && InitialScanComplete && !string.IsNullOrEmpty(CacheFolder) &&
                                     Directory.Exists(CacheFolder) && ClientSecret.ContainsKey(ApiUri);

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

        public Dictionary<string, string> UidComments { get; set; } = new();
        public bool UseCustomService { get; set; } = false;
        public int Version { get; set; } = 0;
        // the below exist just to make saving less cumbersome
        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            _pluginInterface = pluginInterface;
        }

        public void Save()
        {
            _pluginInterface!.SavePluginConfig(this);
        }
    }
}
