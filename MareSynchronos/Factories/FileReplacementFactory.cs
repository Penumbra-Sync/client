using Dalamud.Game.ClientState;
using MareSynchronos.Managers;
using MareSynchronos.Models;

namespace MareSynchronos.Factories
{
    public class FileReplacementFactory
    {
        private readonly IpcManager ipcManager;
        private readonly ClientState clientState;
        private string playerName;

        public FileReplacementFactory(IpcManager ipcManager, ClientState clientState)
        {
            this.ipcManager = ipcManager;
            this.clientState = clientState;
            playerName = null!;
        }

        public FileReplacement Create(string gamePath, bool resolve = true)
        {
            if (!ipcManager.CheckPenumbraAPI())
            {
                throw new System.Exception();
            }

            var fileReplacement = new FileReplacement(gamePath, ipcManager.PenumbraModDirectory()!);
            if (!resolve) return fileReplacement;

            if (clientState.LocalPlayer != null)
            {
                playerName = clientState.LocalPlayer.Name.ToString();
            }
            fileReplacement.SetResolvedPath(ipcManager.PenumbraResolvePath(gamePath, playerName)!);
            if (!fileReplacement.HasFileReplacement)
            {
                // try to resolve path with --filename instead?
                string[] tempGamePath = gamePath.Split('/');
                tempGamePath[^1] = "--" + tempGamePath[^1];
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
