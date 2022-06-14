using Dalamud.Game.ClientState;
using MareSynchronos.Managers;
using MareSynchronos.Models;

namespace MareSynchronos.Factories
{
    public class FileReplacementFactory
    {
        private readonly IpcManager ipcManager;
        private readonly ClientState clientState;

        public FileReplacementFactory(IpcManager ipcManager, ClientState clientState)
        {
            this.ipcManager = ipcManager;
            this.clientState = clientState;
        }

        public FileReplacement Create(string gamePath, bool resolve = true)
        {
            var fileReplacement = new FileReplacement(gamePath, ipcManager.PenumbraModDirectory()!);
            if (!resolve) return fileReplacement;

            string playerName = clientState.LocalPlayer!.Name.ToString();
            fileReplacement.SetResolvedPath(ipcManager.PenumbraResolvePath(gamePath, playerName)!);
            if (!fileReplacement.HasFileReplacement)
            {
                // try to resolve path with --filename instead?
                string[] tempGamePath = gamePath.Split('/');
                tempGamePath[tempGamePath.Length - 1] = "--" + tempGamePath[tempGamePath.Length - 1];
                string newTempGamePath = string.Join('/', tempGamePath);
                var resolvedPath = ipcManager.PenumbraResolvePath(newTempGamePath, playerName)!;
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
