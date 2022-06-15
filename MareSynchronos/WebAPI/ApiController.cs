using Dalamud.Configuration;
using Dalamud.Logging;
using MareSynchronos.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MareSynchronos.WebAPI
{
    public class ApiController
    {
        private readonly Configuration pluginConfiguration;

        private string SecretKey => pluginConfiguration.ClientSecret;
        private string CacheFolder => pluginConfiguration.CacheFolder;
        private string ApiUri => pluginConfiguration.ApiUri;

        public ApiController(Configuration pluginConfiguration)
        {
            this.pluginConfiguration = pluginConfiguration;
        }

        public async Task Heartbeat()
        {
            PluginLog.Debug("Sending heartbeat to " + ApiUri);
        }

        public async Task<(string, string)> Register()
        {
            PluginLog.Debug("Registering at service " + ApiUri);
            return (string.Empty, string.Empty);
        }

        public async Task UploadFile(string filePath)
        {
            PluginLog.Debug("Uploading file " + filePath + " to " + ApiUri);
        }

        public async Task<byte[]> DownloadFile(string hash)
        {
            PluginLog.Debug("Downloading file from service " + ApiUri);

            return Array.Empty<byte>();
        }

        public async Task<List<string>> SendCharacterData(CharacterCache character)
        {
            PluginLog.Debug("Sending Character data to service " + ApiUri);

            List<string> list = new();
            return list;
        }

        public async Task<CharacterCache> GetCharacterData(string uid)
        {
            PluginLog.Debug("Getting character data for " + uid + " from service " + ApiUri);

            CharacterCache characterCache = new();
            return characterCache;
        }

        public async Task SendWhitelist()
        {
            PluginLog.Debug("Sending whitelist to service " + ApiUri);
        }

        public async Task<List<string>> GetWhitelist()
        {
            PluginLog.Debug("Getting whitelist from service " + ApiUri);

            List<string> whitelist = new();
            return whitelist;
        }
    }
}
