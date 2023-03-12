using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services.Mediator;

public abstract class MediatorSubscriberBase : IMediatorSubscriber
{
    protected ILogger Logger { get; }
    public MareMediator Mediator { get; }
    protected MediatorSubscriberBase(ILogger logger, MareMediator mediator)
    {
        Logger = logger;
        Mediator = mediator;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        Logger.LogTrace("Disposing {type} ({this})", GetType(), this);
        Mediator.UnsubscribeAll(this);
    }
}
