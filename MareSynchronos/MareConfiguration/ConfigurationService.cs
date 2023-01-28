using Dalamud.Plugin;
using MareSynchronos.Utils;
using Newtonsoft.Json;

namespace MareSynchronos.MareConfiguration;

public class ConfigurationService : IDisposable
{
    private const string _configurationName = "Config.json";
    private string ConfigurationPath => Path.Combine(_pluginInterface.ConfigDirectory.FullName, _configurationName);
    public string ConfigurationDirectory => _pluginInterface.ConfigDirectory.FullName;
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly CancellationTokenSource _periodicCheckCts = new();
    private DateTime _configLastWriteTime;

    public MareConfig Current { get; private set; }

    public ConfigurationService(DalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;

        if (pluginInterface.GetPluginConfig() is Configuration oldConfig)
        {
            Current = oldConfig.ToMareConfig();
            File.Move(pluginInterface.ConfigFile.FullName, pluginInterface.ConfigFile.FullName + ".old", true);
        }
        else
        {
            Current = LoadConfig();
        }

        Save();

        Task.Run(CheckForConfigUpdatesInternal, _periodicCheckCts.Token);
    }

    private async Task CheckForConfigUpdatesInternal()
    {
        while (!_periodicCheckCts.IsCancellationRequested)
        {
            var lastWriteTime = GetConfigLastWriteTime();
            if (lastWriteTime != _configLastWriteTime)
            {
                Logger.Debug("Config changed, reloading config");
                Current = LoadConfig();
            }

            await Task.Delay(TimeSpan.FromSeconds(5), _periodicCheckCts.Token).ConfigureAwait(false);
        }
    }

    private MareConfig LoadConfig()
    {
        MareConfig config;
        if (!File.Exists(ConfigurationPath))
        {
            config = new();
        }
        else
        {
            config = JsonConvert.DeserializeObject<MareConfig>(File.ReadAllText(ConfigurationPath)) ?? new MareConfig();
        }

        _configLastWriteTime = GetConfigLastWriteTime();
        return config;
    }

    private DateTime GetConfigLastWriteTime() => new FileInfo(ConfigurationPath).LastWriteTimeUtc;

    public void Save()
    {
        File.WriteAllText(ConfigurationPath, JsonConvert.SerializeObject(Current, Formatting.Indented));
        _configLastWriteTime = new FileInfo(ConfigurationPath).LastWriteTimeUtc;
    }

    public void Dispose()
    {
        Save();
        _periodicCheckCts.Cancel();
    }
}
