using MareSynchronos.MareConfiguration.Configurations;
using System.Text.Json;

namespace MareSynchronos.MareConfiguration;

public abstract class ConfigurationServiceBase<T> : IConfigService<T> where T : IMareConfiguration
{
    private readonly CancellationTokenSource _periodicCheckCts = new();
    private DateTime _configLastWriteTime;
    private Lazy<T> _currentConfigInternal;
    private bool _disposed = false;

    public event EventHandler? ConfigSave;

    protected ConfigurationServiceBase(string configDirectory)
    {
        ConfigurationDirectory = configDirectory;

        _ = Task.Run(CheckForConfigUpdatesInternal, _periodicCheckCts.Token);

        _currentConfigInternal = LazyConfig();
    }

    public string ConfigurationDirectory { get; init; }
    public T Current => _currentConfigInternal.Value;
    public abstract string ConfigurationName { get; }
    public string ConfigurationPath => Path.Combine(ConfigurationDirectory, ConfigurationName);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public void Save()
    {
        ConfigSave?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateLastWriteTime()
    {
        _configLastWriteTime = GetConfigLastWriteTime();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed) return;
        _disposed = true;
        _periodicCheckCts.Cancel();
        _periodicCheckCts.Dispose();
    }

    protected T LoadConfig()
    {
        T? config;
        if (!File.Exists(ConfigurationPath))
        {
            config = AttemptToLoadBackup();
            if (Equals(config, default(T)))
            {
                config = (T)Activator.CreateInstance(typeof(T))!;
                Save();
            }
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
                config = AttemptToLoadBackup();
            }

            if (config == null || Equals(config, default(T)))
            {
                config = (T)Activator.CreateInstance(typeof(T))!;
                Save();
            }
        }

        _configLastWriteTime = GetConfigLastWriteTime();
        return config;
    }

    private T? AttemptToLoadBackup()
    {
        var configBackupFolder = Path.Join(ConfigurationDirectory, ConfigurationSaveService.BackupFolder);
        var configNameSplit = ConfigurationName.Split(".");
        if (!Directory.Exists(configBackupFolder))
            return default;

        var existingBackups = Directory.EnumerateFiles(configBackupFolder, configNameSplit[0] + "*").OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc);
        foreach (var file in existingBackups)
        {
            try
            {
                var config = JsonSerializer.Deserialize<T>(File.ReadAllText(file));
                if (Equals(config, default(T)))
                {
                    File.Delete(file);
                }

                File.Copy(file, ConfigurationPath, true);
                return config;
            }
            catch
            {
                // couldn't load backup, might as well delete it
                File.Delete(file);
            }

        }

        return default;
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

    private DateTime GetConfigLastWriteTime()
    {
        try { return new FileInfo(ConfigurationPath).LastWriteTimeUtc; }
        catch { return DateTime.MinValue; }
    }


    private Lazy<T> LazyConfig()
    {
        _configLastWriteTime = GetConfigLastWriteTime();
        return new Lazy<T>(LoadConfig);
    }
}