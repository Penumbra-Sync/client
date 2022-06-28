using System.Diagnostics;
using Dalamud.Logging;

namespace MareSynchronos.Utils
{
    internal class Logger
    {
        public static void Debug(string debug)
        {
            var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
            PluginLog.Debug($"[{caller}] {debug}");
        }

        public static void Warn(string warn)
        {
            var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
            PluginLog.Warning($"[{caller}] {warn}");
        }

        public static void Verbose(string verbose)
        {
            var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
            PluginLog.Verbose($"[{caller}] {verbose}");
        }
    }
}
