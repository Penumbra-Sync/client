using MareSynchronos.API.Dto.User;
using MareSynchronos.FileCache;
using MareSynchronos.Interop;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Factories;

public class CachedPlayerFactory
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileDownloadManagerFactory _fileDownloadManagerFactory;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly IpcManager _ipcManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareConfigService _mareConfigService;
    private readonly MareMediator _mareMediator;

    public CachedPlayerFactory(ILoggerFactory loggerFactory, GameObjectHandlerFactory gameObjectHandlerFactory, IpcManager ipcManager,
        FileDownloadManagerFactory fileDownloadManagerFactory, MareConfigService mareConfigService, DalamudUtilService dalamudUtilService,
        IHostApplicationLifetime hostApplicationLifetime, FileCacheManager fileCacheManager, MareMediator mareMediator)
    {
        _loggerFactory = loggerFactory;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _ipcManager = ipcManager;
        _fileDownloadManagerFactory = fileDownloadManagerFactory;
        _mareConfigService = mareConfigService;
        _dalamudUtilService = dalamudUtilService;
        _hostApplicationLifetime = hostApplicationLifetime;
        _fileCacheManager = fileCacheManager;
        _mareMediator = mareMediator;
    }

    public CachedPlayer Create(OnlineUserIdentDto onlineUserIdentDto)
    {
        return new CachedPlayer(_loggerFactory.CreateLogger<CachedPlayer>(), onlineUserIdentDto, _gameObjectHandlerFactory,
            _ipcManager, _fileDownloadManagerFactory.Create(), _mareConfigService, _dalamudUtilService, _hostApplicationLifetime,
            _fileCacheManager, _mareMediator);
    }
}