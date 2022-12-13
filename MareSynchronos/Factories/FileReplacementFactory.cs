using MareSynchronos.FileCache;
using MareSynchronos.Managers;
using MareSynchronos.Models;

namespace MareSynchronos.Factories;

public class FileReplacementFactory
{
    private readonly FileCacheManager fileCacheManager;
    private readonly IpcManager ipcManager;

    public FileReplacementFactory(FileCacheManager fileCacheManager, IpcManager ipcManager)
    {
        this.fileCacheManager = fileCacheManager;
        this.ipcManager = ipcManager;
    }
    
    public FileReplacement Create()
    {
        return new FileReplacement(fileCacheManager, ipcManager);
    }
}
