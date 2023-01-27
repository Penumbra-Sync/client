using MareSynchronos.API.Dto.User;
using MareSynchronos.FileCache;
using MareSynchronos.Managers;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Factories;

public class CachedPlayerFactory
{
    private readonly IpcManager _ipcManager;
    private readonly DalamudUtil _dalamudUtil;
    private readonly FileCacheManager _fileCacheManager;

    public CachedPlayerFactory(IpcManager ipcManager, DalamudUtil dalamudUtil, FileCacheManager fileCacheManager)
    {
        _ipcManager = ipcManager;
        _dalamudUtil = dalamudUtil;
        _fileCacheManager = fileCacheManager;
    }

    public CachedPlayer Create(OnlineUserIdentDto dto, ApiController apiController)
    {
        return new CachedPlayer(dto, _ipcManager, apiController, _dalamudUtil, _fileCacheManager);
    }
}
