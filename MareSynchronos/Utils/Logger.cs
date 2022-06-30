using System.Diagnostics;
using System.Text;
using Dalamud.Logging;
using Dalamud.Utility;

namespace MareSynchronos.Utils
{
    internal class Logger
    {
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
            PluginLog.Verbose($"[{caller}] {verbose}");
        }
    }
}
