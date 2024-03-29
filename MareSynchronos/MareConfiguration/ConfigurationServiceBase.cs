using MareSynchronos.MareConfiguration.Configurations;
using System.Text.Json;

namespace MareSynchronos.MareConfiguration;

public abstract class ConfigurationServiceBase<T> : IDisposable where T : IMareConfiguration
{
    private readonly CancellationTokenSource _periodicCheckCts = new();
    private bool _configIsDirty = false;
    private DateTime _configLastWriteTime;
    private Lazy<T> _currentConfigInternal;

    protected ConfigurationServiceBase(string configurationDirectory)
    {
        ConfigurationDirectory = configurationDirectory;

        _ = Task.Run(CheckForConfigUpdatesInternal, _periodicCheckCts.Token);
        _ = Task.Run(CheckForDirtyConfigInternal, _periodicCheckCts.Token);

        _currentConfigInternal = LazyConfig();
    }

    public string ConfigurationDirectory { get; init; }
    public T Current => _currentConfigInternal.Value;
    protected abstract string ConfigurationName { get; }
    protected string ConfigurationPath => Path.Combine(ConfigurationDirectory, ConfigurationName);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public void Save()
    {
        _configIsDirty = true;
    }

    protected virtual void Dispose(bool disposing)
    {
        _periodicCheckCts.Cancel();
        _periodicCheckCts.Dispose();
        if (_configIsDirty) SaveDirtyConfig();
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
            try
            {
                config = JsonSerializer.Deserialize<T>(File.ReadAllText(ConfigurationPath));
            }
            catch
            {
                // config failed to load for some reason
                config = default;
            }
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

        try
        {
            File.Copy(ConfigurationPath, ConfigurationPath + ".bak." + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
        }
        catch
        {
            // ignore if file cannot be backupped once
        }

        var temp = ConfigurationPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Current, new JsonSerializerOptions()
        {
            WriteIndented = true
        }));
        File.Move(temp, ConfigurationPath, true);
        _configLastWriteTime = new FileInfo(ConfigurationPath).LastWriteTimeUtc;
    }

    private async Task CheckForConfigUpdatesInternal()
    {
        while (!_periodicCheckCts.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), _periodicCheckCts.Token).ConfigureAwait(false);

            var lastWriteTime = GetConfigLastWriteTime();
            if (lastWriteTime != _configLastWriteTime)
            {
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

    private DateTime GetConfigLastWriteTime() => new FileInfo(ConfigurationPath).LastWriteTimeUtc;

    private Lazy<T> LazyConfig()
    {
        _configLastWriteTime = GetConfigLastWriteTime();
        return new Lazy<T>(LoadConfig);
    }
}