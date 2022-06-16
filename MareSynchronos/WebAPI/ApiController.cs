using Dalamud.Configuration;
using Dalamud.Logging;
using MareSynchronos.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MareSynchronos.WebAPI
{
    public class ApiController : IDisposable
    {
        private readonly Configuration pluginConfiguration;
        private const string mainService = "https://localhost:6591";
        public string UID { get; private set; } = string.Empty;
        public string SecretKey => pluginConfiguration.ClientSecret.ContainsKey(ApiUri) ? pluginConfiguration.ClientSecret[ApiUri] : string.Empty;
        private string CacheFolder => pluginConfiguration.CacheFolder;
        public bool UseCustomService
        {
            get => pluginConfiguration.UseCustomService;
            set
            {
                pluginConfiguration.UseCustomService = value;
                _ = Heartbeat();
                pluginConfiguration.Save();
            }
        }
        private string ApiUri => UseCustomService ? pluginConfiguration.ApiUri : mainService;

        public bool IsConnected { get; set; }

        Task heartbeatTask;
        CancellationTokenSource cts;

        public ApiController(Configuration pluginConfiguration)
        {
            this.pluginConfiguration = pluginConfiguration;
            cts = new CancellationTokenSource();

            heartbeatTask = Task.Run(async () =>
            {
                PluginLog.Debug("Starting heartbeat to " + ApiUri);
                while (true && !cts.IsCancellationRequested)
                {
                    await Heartbeat();
                    await Task.Delay(TimeSpan.FromSeconds(15), cts.Token);
                }
                PluginLog.Debug("Stopping heartbeat");
            }, cts.Token);
        }

        public async Task Heartbeat()
        {
            try
            {
                PluginLog.Debug("Sending heartbeat to " + ApiUri);
                if (ApiUri != mainService) throw new Exception();
                IsConnected = true;
            }
            catch
            {
                IsConnected = false;
            }
        }

        public async Task Register()
        {
            PluginLog.Debug("Registering at service " + ApiUri);
            var response = ("RandomSecretKey", "RandomUID");
            pluginConfiguration.ClientSecret[ApiUri] = response.Item1;
            UID = response.Item2;
            PluginLog.Debug(pluginConfiguration.ClientSecret[ApiUri]);
            // pluginConfiguration.Save();
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

        public void Dispose()
        {
            cts?.Cancel();
        }
    }
}
