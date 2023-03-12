using MareSynchronos.API.Data.Enum;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Factories;

public class GameObjectHandlerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mediator;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly DalamudUtilService _dalamudUtil;

    public GameObjectHandlerFactory(ILoggerFactory loggerFactory, MareMediator mediator, PerformanceCollectorService performanceCollector, DalamudUtilService dalamudUtil)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
        _performanceCollector = performanceCollector;
        _dalamudUtil = dalamudUtil;
    }

    public GameObjectHandler Create(ObjectKind objectKind, Func<IntPtr> getAddress, bool isWatched)
    {
        return new GameObjectHandler(_loggerFactory.CreateLogger<GameObjectHandler>(), _performanceCollector, _mediator, _dalamudUtil, objectKind, getAddress, isWatched);
    }
}