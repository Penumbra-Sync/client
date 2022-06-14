using Dalamud.Game.ClientState;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Models;

namespace MareSynchronos.Factories
{
    public class FileReplacementFactory
    {
        private readonly ClientState clientState;
        private ICallGateSubscriber<string, string, string> resolvePath;
        private string penumbraDirectory;

        public FileReplacementFactory(DalamudPluginInterface pluginInterface, ClientState clientState)
        {
            resolvePath = pluginInterface.GetIpcSubscriber<string, string, string>("Penumbra.ResolveCharacterPath");
            penumbraDirectory = pluginInterface.GetIpcSubscriber<string>("Penumbra.GetModDirectory").InvokeFunc().ToLower() + '\\';
            this.clientState = clientState;
        }
        public FileReplacement Create(string gamePath, bool resolve = true)
        {
            var fileReplacement = new FileReplacement(gamePath, penumbraDirectory);
            if (!resolve) return fileReplacement;

            fileReplacement.SetResolvedPath(resolvePath.InvokeFunc(gamePath, clientState.LocalPlayer!.Name.ToString()));
            if (!fileReplacement.HasFileReplacement)
            {
                // try to resolve path with --filename instead?
                string[] tempGamePath = gamePath.Split('/');
                tempGamePath[tempGamePath.Length - 1] = "--" + tempGamePath[tempGamePath.Length - 1];
                string newTempGamePath = string.Join('/', tempGamePath);
                var resolvedPath = resolvePath.InvokeFunc(newTempGamePath, clientState.LocalPlayer!.Name.ToString());
                if (resolvedPath != newTempGamePath)
                {
                    fileReplacement.SetResolvedPath(resolvedPath);
                    fileReplacement.SetGamePath(newTempGamePath);
                }
            }
            return fileReplacement;
        }
    }
}
