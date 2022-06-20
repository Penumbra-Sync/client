using Dalamud.Game.ClientState;
using MareSynchronos.Managers;
using MareSynchronos.Models;

namespace MareSynchronos.Factories
{
    public class FileReplacementFactory
    {
        private readonly IpcManager ipcManager;

        public FileReplacementFactory(IpcManager ipcManager)
        {
            this.ipcManager = ipcManager;
        }

        public FileReplacement Create()
        {
            if (!ipcManager.CheckPenumbraApi())
            {
                throw new System.Exception();
            }

            return new FileReplacement(ipcManager.PenumbraModDirectory()!);
        }
    }
}
