using MareSynchronos.API.Data.Enum;
using MareSynchronos.Mediator;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Factories;

public class GameObjectHandlerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mediator;
    private readonly PerformanceCollector _performanceCollector;

    public GameObjectHandlerFactory(ILoggerFactory loggerFactory, MareMediator mediator, PerformanceCollector performanceCollector)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
        _performanceCollector = performanceCollector;
    }

    public GameObjectHandler Create(ObjectKind objectKind, Func<IntPtr> getAddress, bool isWatched)
    {
        return new GameObjectHandler(_loggerFactory.CreateLogger<GameObjectHandler>(), _performanceCollector, _mediator, objectKind, getAddress, isWatched);
    }
}