using Dalamud.Plugin.Services;
using MareSynchronos.MareConfiguration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MareSynchronos.Interop;

internal sealed class DalamudLogger : ILogger
{
    private readonly MareConfigService _mareConfigService;
    private readonly string _name;
    private readonly IPluginLog _pluginLog;

    public DalamudLogger(string name, MareConfigService mareConfigService, IPluginLog pluginLog)
    {
        _name = name;
        _mareConfigService = mareConfigService;
        _pluginLog = pluginLog;
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
            _pluginLog.Information($"[{_name}]{{{(int)logLevel}}} {state}");
        else
        {
            StringBuilder sb = new();
            sb.AppendLine($"[{_name}]{{{(int)logLevel}}} {state}: {exception?.Message}");
            sb.AppendLine(exception?.StackTrace);
            var innerException = exception?.InnerException;
            while (innerException != null)
            {
                sb.AppendLine($"InnerException {innerException}: {innerException.Message}");
                sb.AppendLine(innerException.StackTrace);
                innerException = innerException.InnerException;
            }
            if (logLevel == LogLevel.Warning)
                _pluginLog.Warning(sb.ToString());
            else if (logLevel == LogLevel.Error)
                _pluginLog.Error(sb.ToString());
            else
                _pluginLog.Fatal(sb.ToString());
        }
    }
}