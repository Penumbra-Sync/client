using Dalamud.Game.ClientState;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace MareSynchronos.Models
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
        public FileReplacement Create(string gamePath)
        {
            var fileReplacement = new FileReplacement(gamePath, penumbraDirectory);
            fileReplacement.SetReplacedPath(resolvePath.InvokeFunc(gamePath, clientState.LocalPlayer!.Name.ToString()));
            if (!fileReplacement.HasFileReplacement)
            {
                // try to resolve path with -- instead?
                string[] tempGamePath = gamePath.Split('/');
                tempGamePath[tempGamePath.Length - 1] = "--" + tempGamePath[tempGamePath.Length - 1];
                string newTempGamePath = string.Join('/', tempGamePath);
                var resolvedPath = resolvePath.InvokeFunc(newTempGamePath, clientState.LocalPlayer!.Name.ToString());
                if (resolvedPath != newTempGamePath)
                {
                    fileReplacement.SetReplacedPath(resolvedPath);
                    fileReplacement.SetGamePath(newTempGamePath);
                }
            }
            return fileReplacement;
        }
    }
}
