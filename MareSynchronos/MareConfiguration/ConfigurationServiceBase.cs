using Dalamud.Plugin;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.Utils;
using Newtonsoft.Json;

namespace MareSynchronos.MareConfiguration;

public abstract class ConfigurationServiceBase<T> : IDisposable where T : IMareConfiguration
{
    protected abstract string ConfigurationName { get; }
    public string ConfigurationDirectory => _pluginInterface.ConfigDirectory.FullName;
    public T Current => _currentConfigInternal.Value;

    protected readonly DalamudPluginInterface _pluginInterface;
    private readonly CancellationTokenSource _periodicCheckCts = new();
    private DateTime _configLastWriteTime;
    private bool _configIsDirty = false;
    private Lazy<T> _currentConfigInternal;

    protected string ConfigurationPath => Path.Combine(ConfigurationDirectory, ConfigurationName);
    protected ConfigurationServiceBase(DalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;

        Task.Run(CheckForConfigUpdatesInternal, _periodicCheckCts.Token);
        Task.Run(CheckForDirtyConfigInternal, _periodicCheckCts.Token);

        _currentConfigInternal = LazyConfig();
    }

    private Lazy<T> LazyConfig()
    {
        _configLastWriteTime = GetConfigLastWriteTime();
        return new Lazy<T>(() => LoadConfig());
    }
    private DateTime GetConfigLastWriteTime() => new FileInfo(ConfigurationPath).LastWriteTimeUtc;

    private async Task CheckForConfigUpdatesInternal()
    {
        while (!_periodicCheckCts.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), _periodicCheckCts.Token).ConfigureAwait(false);

            var lastWriteTime = GetConfigLastWriteTime();
            if (lastWriteTime != _configLastWriteTime)
            {
                Logger.Debug($"Config {ConfigurationName} changed, reloading config");
                _currentConfigInternal = LazyConfig();
            }
        }
    }

    private async Task CheckForDirtyConfigInternal()
    {
        while (!_periodicCheckCts.IsCancellationRequested)
        {
            if (_configIsDirty)
            {
                SaveDirtyConfig();
            }

            await Task.Delay(TimeSpan.FromSeconds(1), _periodicCheckCts.Token).ConfigureAwait(false);
        }
    }

    protected T LoadConfig()
    {
        T? config;
        if (!File.Exists(ConfigurationPath))
        {
            config = (T)Activator.CreateInstance(typeof(T))!;
            Save();
        }
        else
        {
            config = JsonConvert.DeserializeObject<T>(File.ReadAllText(ConfigurationPath));
            if (config == null)
            {
                config = (T)Activator.CreateInstance(typeof(T))!;
                Save();
            }
        }

        _configLastWriteTime = GetConfigLastWriteTime();
        return config;
    }

    protected void SaveDirtyConfig()
    {
        _configIsDirty = false;
        var existingConfigs = Directory.EnumerateFiles(ConfigurationDirectory, ConfigurationName + ".bak.*").Select(c => new FileInfo(c))
            .OrderByDescending(c => c.LastWriteTime).ToList();
        if (existingConfigs.Skip(10).Any())
        {
            foreach (var config in existingConfigs.Skip(10).ToList())
            {
                config.Delete();
            }
        }

        Logger.Debug("Saving dirty config " + ConfigurationName);

        try
        {
            File.Copy(ConfigurationPath, ConfigurationPath + ".bak." + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
        }
        catch { }

        File.WriteAllText(ConfigurationPath, JsonConvert.SerializeObject(Current, Formatting.Indented));
        _configLastWriteTime = new FileInfo(ConfigurationPath).LastWriteTimeUtc;
    }

    public void Save()
    {
        _configIsDirty = true;
    }

    public void Dispose()
    {
        Logger.Verbose($"Disposing {GetType()}");
        _periodicCheckCts.Cancel();
    }
}
