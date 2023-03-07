using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services.Mediator;

public abstract class MediatorSubscriberBase : IMediatorSubscriber
{
    protected ILogger _logger { get; }
    public MareMediator Mediator { get; }
    protected MediatorSubscriberBase(ILogger logger, MareMediator mediator)
    {
        _logger = logger;
        Mediator = mediator;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public virtual void Dispose(bool disposing)
    {
        _logger.LogTrace("Disposing {type} ({this})", GetType(), this);
        Mediator.UnsubscribeAll(this);
    }
}
