using System.Diagnostics;
using Dalamud.Logging;
using Dalamud.Utility;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Utils;

internal class Logger : ILogger
{
    private readonly string name;

    public static void Info(string info)
    {
        var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
        PluginLog.Information($"[{caller}] {info}");
    }

    public static void Debug(string debug, string stringToHighlight = "")
    {
        var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
        // todo: remove this again
        if (caller == "CharacterDataFactory") return;
        if (debug.Contains(stringToHighlight, StringComparison.Ordinal) && !stringToHighlight.IsNullOrEmpty())
        {
            PluginLog.Warning($"[{caller}] {debug}");
        }
        else
        {
            PluginLog.Debug($"[{caller}] {debug}");
        }
    }

    public static void Error(string msg, Exception ex)
    {
        var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
        PluginLog.Error($"[{caller}] {msg} {Environment.NewLine} Exception: {ex.Message} {Environment.NewLine} {ex.StackTrace}");
    }

    public static void Warn(string msg, Exception ex)
    {
        var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
        PluginLog.Warning($"[{caller}] {msg} {Environment.NewLine} Exception: {ex.Message} {Environment.NewLine} {ex.StackTrace}");
    }

    public static void Error(string msg)
    {
        var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
        PluginLog.Error($"[{caller}] {msg}");
    }

    public static void Warn(string warn)
    {
        var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
        PluginLog.Warning($"[{caller}] {warn}");
    }

    public static void Verbose(string verbose)
    {
        var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
        // todo: remove this again
        if (caller == "CharacterDataFactory") return;
#if DEBUG
        PluginLog.Debug($"[{caller}] {verbose}");
#else
        PluginLog.Verbose($"[{caller}] {verbose}");
#endif
    }

    public Logger(string name)
    {
        this.name = name;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        switch (logLevel)
        {
            case LogLevel.Debug:
                PluginLog.Debug($"[{name}] [{eventId}] {formatter(state, exception)}");
                break;
            case LogLevel.Error:
            case LogLevel.Critical:
                PluginLog.Error($"[{name}] [{eventId}] {formatter(state, exception)}");
                break;
            case LogLevel.Information:
                PluginLog.Information($"[{name}] [{eventId}] {formatter(state, exception)}");
                break;
            case LogLevel.Warning:
                PluginLog.Warning($"[{name}] [{eventId}] {formatter(state, exception)}");
                break;
            case LogLevel.Trace:
            default:
#if DEBUG
                PluginLog.Verbose($"[{name}] [{eventId}] {formatter(state, exception)}");
#else
                PluginLog.Verbose($"[{name}] {eventId} {state} {formatter(state, exception)}");
#endif
                break;
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public IDisposable BeginScope<TState>(TState state) => default!;
}
