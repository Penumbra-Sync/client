using Dalamud.Plugin;
using MareSynchronos.MareConfiguration.Configurations;
using System.Text.Json;

namespace MareSynchronos.MareConfiguration;

public abstract class ConfigurationServiceBase<T> : IDisposable where T : IMareConfiguration
{
    protected abstract string ConfigurationName { get; }
    public string ConfigurationDirectory { get; init; }
    public T Current => _currentConfigInternal.Value;

    private readonly CancellationTokenSource _periodicCheckCts = new();
    private DateTime _configLastWriteTime;
    private bool _configIsDirty = false;
    private Lazy<T> _currentConfigInternal;

    protected string ConfigurationPath => Path.Combine(ConfigurationDirectory, ConfigurationName);
    protected ConfigurationServiceBase(string configurationDirectory)
    {
        ConfigurationDirectory = configurationDirectory;

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
            config = JsonSerializer.Deserialize<T>(File.ReadAllText(ConfigurationPath));
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

        File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(Current, new JsonSerializerOptions()
        {
            WriteIndented = true
        }));
        _configLastWriteTime = new FileInfo(ConfigurationPath).LastWriteTimeUtc;
    }

    public void Save()
    {
        _configIsDirty = true;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        _periodicCheckCts.Cancel();
        _periodicCheckCts.Dispose();
    }
}
