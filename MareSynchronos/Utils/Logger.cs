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
    }
}
