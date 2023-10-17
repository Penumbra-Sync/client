using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services.Mediator;

public abstract class DisposableMediatorSubscriberBase : MediatorSubscriberBase, IDisposable
{
    protected DisposableMediatorSubscriberBase(ILogger logger, MareMediator mediator) : base(logger, mediator)
    {
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        Logger.LogTrace("Disposing {type} ({this})", GetType().Name, this);
        UnsubscribeAll();
    }
}