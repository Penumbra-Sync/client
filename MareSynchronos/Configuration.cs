using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace MareSynchronos
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public string CacheFolder { get; set; } = string.Empty;
        public string ClientSecret { get; internal set; } = string.Empty;
        public string ApiUri { get; internal set; } = string.Empty;

        // the below exist just to make saving less cumbersome

        [NonSerialized]
        private DalamudPluginInterface? pluginInterface;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.pluginInterface!.SavePluginConfig(this);
        }
    }
}
