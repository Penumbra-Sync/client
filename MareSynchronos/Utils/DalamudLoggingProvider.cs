using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Utils;

[ProviderAlias("Dalamud")]
public class DalamudLoggingProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, Logger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);

    public DalamudLoggingProvider()
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new Logger(categoryName));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}
