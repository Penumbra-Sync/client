using System.Diagnostics;
using Dalamud.Logging;
using Dalamud.Utility;

namespace MareSynchronos.Utils
{
    internal class Logger
    {
        public static void Info(string info)
        {
            var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
            PluginLog.Information($"[{caller}] {info}");
        }

        public static void Debug(string debug, string stringToHighlight = "")
        {
            var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
            if (debug.Contains(stringToHighlight) && !stringToHighlight.IsNullOrEmpty())
            {
                PluginLog.Warning($"[{caller}] {debug}");
            }
            else
            {
                PluginLog.Debug($"[{caller}] {debug}");
            }
        }

        public static void Warn(string warn)
        {
            var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
            PluginLog.Warning($"[{caller}] {warn}");
        }

        public static void Verbose(string verbose)
        {
            var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
#if DEBUG
            PluginLog.Debug($"[{caller}] {verbose}");
#else
            PluginLog.Verbose($"[{caller}] {verbose}");
#endif
        }
    }
}
