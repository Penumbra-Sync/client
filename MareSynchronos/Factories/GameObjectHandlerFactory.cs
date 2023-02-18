using MareSynchronos.API.Data.Enum;
using MareSynchronos.Mediator;
using MareSynchronos.Models;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Factories;

public class GameObjectHandlerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mediator;

    public GameObjectHandlerFactory(ILoggerFactory loggerFactory, MareMediator mediator)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
    }

    public GameObjectHandler Create(ObjectKind objectKind, Func<IntPtr> getAddress, bool isWatched)
    {
        return new GameObjectHandler(_loggerFactory.CreateLogger<GameObjectHandler>(), _mediator, objectKind, getAddress, isWatched);
    }
}