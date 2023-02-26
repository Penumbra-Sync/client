using MareSynchronos.API.Dto.User;
using MareSynchronos.FileCache;
using MareSynchronos.Interop;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI.FileTransfer;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Pairs;

public class CachedPlayerFactory
{
    private readonly IpcManager _ipcManager;
    private readonly DalamudUtil _dalamudUtil;
    private readonly FileCacheManager _fileCacheManager;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly MareMediator _mediator;
    private readonly ILoggerFactory _loggerFactory;
    private readonly FileTransferManager _fileTransferManager;

    public CachedPlayerFactory(IpcManager ipcManager, DalamudUtil dalamudUtil, FileCacheManager fileCacheManager,
        GameObjectHandlerFactory gameObjectHandlerFactory,
        MareMediator mediator, ILoggerFactory loggerFactory, FileTransferManager fileTransferManager)
    {
        _ipcManager = ipcManager;
        _dalamudUtil = dalamudUtil;
        _fileCacheManager = fileCacheManager;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _mediator = mediator;
        _loggerFactory = loggerFactory;
        _fileTransferManager = fileTransferManager;
    }

    public CachedPlayer Create(OnlineUserIdentDto dto)
    {
        return new CachedPlayer(_loggerFactory.CreateLogger<CachedPlayer>(), dto, _gameObjectHandlerFactory, _ipcManager, _fileTransferManager, _dalamudUtil, _fileCacheManager, _mediator);
    }
}
