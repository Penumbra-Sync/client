using Microsoft.Extensions.Logging;

namespace MareSynchronos.Mediator;

public abstract class MediatorSubscriberBase : IMediatorSubscriber
{
    protected ILogger _logger { get; }
    public MareMediator Mediator { get; }
    protected MediatorSubscriberBase(ILogger logger, MareMediator mediator)
    {
        _logger = logger;
        Mediator = mediator;
    }

    public virtual void Dispose()
    {
        _logger.LogTrace($"Disposing {GetType()}");
        Mediator.UnsubscribeAll(this);
    }
}
