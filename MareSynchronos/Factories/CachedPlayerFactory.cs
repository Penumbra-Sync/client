using MareSynchronos.API.Dto.User;
using MareSynchronos.FileCache;
using MareSynchronos.Managers;
using MareSynchronos.Mediator;
using MareSynchronos.Utils;

namespace MareSynchronos.Factories;

public class CachedPlayerFactory
{
    private readonly IpcManager _ipcManager;
    private readonly DalamudUtil _dalamudUtil;
    private readonly FileCacheManager _fileCacheManager;
    private readonly MareMediator _mediator;
    private readonly TransferManager _transferManager;

    public CachedPlayerFactory(IpcManager ipcManager, DalamudUtil dalamudUtil, FileCacheManager fileCacheManager, MareMediator mediator, TransferManager transferManager)
    {
        _ipcManager = ipcManager;
        _dalamudUtil = dalamudUtil;
        _fileCacheManager = fileCacheManager;
        _mediator = mediator;
        _transferManager = transferManager;
    }

    public CachedPlayer Create(OnlineUserIdentDto dto)
    {
        return new CachedPlayer(dto, _ipcManager, _transferManager, _dalamudUtil, _fileCacheManager, _mediator);
    }
}
