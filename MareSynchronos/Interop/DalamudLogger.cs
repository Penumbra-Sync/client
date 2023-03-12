using Dalamud.Logging;
using MareSynchronos.MareConfiguration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MareSynchronos.Interop;

internal sealed class DalamudLogger : ILogger
{
    private readonly MareConfigService _mareConfigService;
    private readonly string _name;

    public DalamudLogger(string name, MareConfigService mareConfigService)
    {
        _name = name;
        _mareConfigService = mareConfigService;
    }

    public IDisposable BeginScope<TState>(TState state) => default!;

    public bool IsEnabled(LogLevel logLevel)
    {
        return (int)_mareConfigService.Current.LogLevel <= (int)logLevel;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        if ((int)logLevel <= (int)LogLevel.Information)
            PluginLog.Information($"[{_name}]{{{(int)logLevel}}} {state}");
        else
        {
            StringBuilder sb = new();
            sb.AppendLine($"[{_name}]{{{(int)logLevel}}} {state}: {exception?.Message}");
            sb.AppendLine(exception?.StackTrace);
            if (logLevel == LogLevel.Warning)
                PluginLog.Warning(sb.ToString());
            else if (logLevel == LogLevel.Error)
                PluginLog.Error(sb.ToString());
            else
                PluginLog.Fatal(sb.ToString());
        }
    }
}