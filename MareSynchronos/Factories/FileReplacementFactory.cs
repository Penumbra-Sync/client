using MareSynchronos.Managers;
using MareSynchronos.Models;
using MareSynchronos.Utils;

namespace MareSynchronos.Factories
{
    public class FileReplacementFactory
    {
        private readonly IpcManager _ipcManager;

        public FileReplacementFactory(IpcManager ipcManager)
        {
            Logger.Debug("Creating " + nameof(FileReplacementFactory));

            this._ipcManager = ipcManager;
        }

        public FileReplacement Create()
        {
            if (!_ipcManager.CheckPenumbraApi())
            {
                throw new System.Exception();
            }

            return new FileReplacement(_ipcManager.PenumbraModDirectory()!);
        }
    }
}
