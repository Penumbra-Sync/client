using Dalamud.Logging;
using MareSynchronos.MareConfiguration;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Utils;

internal class DalamudLogger : ILogger
{
    private readonly string _name;
    private readonly MareConfigService _mareConfigService;

    public DalamudLogger(string name, MareConfigService mareConfigService)
    {
        this._name = name;
        _mareConfigService = mareConfigService;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        if (exception == null)
            PluginLog.Information($"[{_name}]{{{(int)logLevel}}} {formatter(state, exception)}");
        else if (logLevel == LogLevel.Warning)
            PluginLog.Warning($"[{_name}]{{{(int)logLevel}}} {formatter(state, exception)}");
        else if (logLevel == LogLevel.Error)
            PluginLog.Error($"[{_name}]{{{(int)logLevel}}} {formatter(state, exception)}");
        else
            PluginLog.Fatal($"[{_name}]{{{(int)logLevel}}} {formatter(state, exception)}");
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return (int)_mareConfigService.Current.LogLevel <= (int)logLevel;
    }

    public IDisposable BeginScope<TState>(TState state) => default!;
}
