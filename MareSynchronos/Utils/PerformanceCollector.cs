using MareSynchronos.MareConfiguration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace MareSynchronos.Utils;

public class PerformanceCollector : IDisposable
{
    private readonly ConcurrentDictionary<string, RollingList<Tuple<TimeOnly, long>>> _performanceCounters = new(StringComparer.Ordinal);
    private readonly ILogger<PerformanceCollector> _logger;
    private readonly MareConfigService _mareConfigService;
    private const string _counterSplit = "=>";
    private readonly CancellationTokenSource _periodicLogPruneTask = new();

    public PerformanceCollector(ILogger<PerformanceCollector> logger, MareConfigService mareConfigService)
    {
        _logger = logger;
        _mareConfigService = mareConfigService;
        _ = Task.Run(PeriodicLogPrune, _periodicLogPruneTask.Token);
    }

    public void Dispose()
    {
        _logger.LogTrace("Disposing {this}", GetType());
        _periodicLogPruneTask.Cancel();
    }

    private async Task PeriodicLogPrune()
    {
        while (!_periodicLogPruneTask.Token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(10), _periodicLogPruneTask.Token).ConfigureAwait(false);

            foreach (var entries in _performanceCounters.ToList())
            {
                if (entries.Value.Last().Item1.AddMinutes(10) < TimeOnly.FromDateTime(DateTime.Now))
                {
                    _performanceCounters.Remove(entries.Key, out _);
                }
            }
        }
    }

    public T LogPerformance<T>(object sender, string counterName, Func<T> func)
    {
        if (!_mareConfigService.Current.LogPerformance) return func.Invoke();

        counterName = sender.GetType().Name + _counterSplit + counterName;

        if (!_performanceCounters.TryGetValue(counterName, out var list))
        {
            list = _performanceCounters[counterName] = new(10000);
        }

        Stopwatch st = Stopwatch.StartNew();
        try
        {
            return func.Invoke();
        }
        catch
        {
            throw;
        }
        finally
        {
            st.Stop();
            list.Add(new(TimeOnly.FromDateTime(DateTime.Now), st.ElapsedTicks));
        }
    }

    public void LogPerformance(object sender, string counterName, Action act)
    {
        if (!_mareConfigService.Current.LogPerformance) { act.Invoke(); return; }

        counterName = sender.GetType().Name + _counterSplit + counterName;

        if (!_performanceCounters.TryGetValue(counterName, out var list))
        {
            list = _performanceCounters[counterName] = new(10000);
        }

        Stopwatch st = Stopwatch.StartNew();
        try
        {
            act.Invoke();
        }
        catch
        {
            throw;
        }
        finally
        {
            st.Stop();
            list.Add(new(TimeOnly.FromDateTime(DateTime.Now), st.ElapsedTicks));
        }

    }

    internal void PrintPerformanceStats(int limitBySeconds = 0)
    {
        if (!_mareConfigService.Current.LogPerformance)
        {
            _logger.LogWarning("Performance counters are disabled");
        }

        StringBuilder sb = new();
        if (limitBySeconds > 0)
        {
            sb.AppendLine($"Performance Metrics over the past {limitBySeconds} seconds of each counter");
        }
        else
        {
            sb.AppendLine("Performance metrics over total lifetime of each counter");
        }
        var data = _performanceCounters.ToList();
        var longestCounterName = data.OrderByDescending(d => d.Key.Length).First().Key.Length + 2;
        sb.Append("-Last".PadRight(15, '-'));
        sb.Append('|');
        sb.Append("-Max".PadRight(15, '-'));
        sb.Append('|');
        sb.Append("-Average".PadRight(15, '-'));
        sb.Append('|');
        sb.Append("-Last Update".PadRight(15, '-'));
        sb.Append('|');
        sb.Append("-Entries".PadRight(10, '-'));
        sb.Append('|');
        sb.Append("-Counter Name".PadRight(longestCounterName, '-'));
        sb.AppendLine();
        var orderedData = data.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToList();
        var previousCaller = orderedData.First().Key.Split(_counterSplit, StringSplitOptions.RemoveEmptyEntries)[0];
        foreach (var entry in orderedData)
        {
            var newCaller = entry.Key.Split(_counterSplit, StringSplitOptions.RemoveEmptyEntries)[0];
            if (!string.Equals(previousCaller, newCaller, StringComparison.Ordinal))
            {
                DrawSeparator(sb, longestCounterName);
            }

            var pastEntries = limitBySeconds > 0 ? entry.Value.Where(e => e.Item1.AddMinutes(limitBySeconds / 60.0d) >= TimeOnly.FromDateTime(DateTime.Now)).ToList() : entry.Value.ToList();

            if (pastEntries.Any())
            {
                sb.Append((" " + TimeSpan.FromTicks(pastEntries.LastOrDefault()?.Item2 ?? 0).TotalMilliseconds.ToString("0.00000", CultureInfo.InvariantCulture)).PadRight(15));
                sb.Append('|');
                sb.Append((" " + TimeSpan.FromTicks(pastEntries.Max(m => m.Item2)).TotalMilliseconds.ToString("0.00000", CultureInfo.InvariantCulture)).PadRight(15));
                sb.Append('|');
                sb.Append((" " + TimeSpan.FromTicks((long)pastEntries.Average(m => m.Item2)).TotalMilliseconds.ToString("0.00000", CultureInfo.InvariantCulture)).PadRight(15));
            }
            else
            {
                sb.Append(" -".PadRight(15));
                sb.Append('|');
                sb.Append(" -".PadRight(15));
                sb.Append('|');
                sb.Append(" -".PadRight(15));
            }
            sb.Append('|');
            sb.Append((" " + (pastEntries.LastOrDefault()?.Item1.ToString("HH:mm:ss.ffff", CultureInfo.InvariantCulture) ?? "-")).PadRight(15, ' '));
            sb.Append('|');
            sb.Append((" " + pastEntries.Count).PadRight(10));
            sb.Append('|');
            sb.Append(' ').Append(entry.Key);
            sb.AppendLine();

            previousCaller = newCaller;
        }

        DrawSeparator(sb, longestCounterName);

        _logger.LogInformation("{perf}", sb.ToString());
    }

    private static void DrawSeparator(StringBuilder sb, int longestCounterName)
    {
        sb.Append("".PadRight(15, '-'));
        sb.Append('+');
        sb.Append("".PadRight(15, '-'));
        sb.Append('+');
        sb.Append("".PadRight(15, '-'));
        sb.Append('+');
        sb.Append("".PadRight(15, '-'));
        sb.Append('+');
        sb.Append("".PadRight(10, '-'));
        sb.Append('+');
        sb.Append("".PadRight(longestCounterName, '-'));
        sb.AppendLine();
    }
}
